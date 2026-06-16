using System.Reflection;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "HFA_");

var applicationName = string.IsNullOrWhiteSpace(builder.Environment.ApplicationName)
    ? "demo-api"
    : builder.Environment.ApplicationName;

builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("Application", applicationName)
        .Enrich.WithProperty("Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0")
        .WriteTo.Console();

    var logFile = ctx.Configuration["LogFile:Path"];
    if (!string.IsNullOrWhiteSpace(logFile))
    {
        cfg.WriteTo.File(logFile, rollingInterval: RollingInterval.Day, shared: true);
    }

    var elasticUri = ctx.Configuration["Elastic:Uri"];
    if (!string.IsNullOrWhiteSpace(elasticUri))
    {
        cfg.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUri))
        {
            IndexFormat = ctx.Configuration["Elastic:IndexFormat"] ?? "logs-{0:yyyy.MM.dd}",
            AutoRegisterTemplate = true,
            TypeName = null,
            CustomFormatter = new EcsTextFormatter(),
            FailureCallback = (e, ex) => Console.Error.WriteLine($"[elastic-sink] {ex?.Message} (event: {e.MessageTemplate.Text})"),
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog | EmitEventFailureHandling.WriteToFailureSink,
        });
    }
});

var app = builder.Build();

app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("RequestMethod", http.Request.Method);
        diag.Set("RequestPath", http.Request.Path.Value);
        diag.Set("StatusCode", http.Response.StatusCode);
        diag.Set(
            "CorrelationId",
            http.Request.Headers.TryGetValue("X-Correlation-Id", out var v)
                ? v.ToString()
                : http.TraceIdentifier);
    };
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// 1) NullReferenceException on certain payloads.
app.MapPost("/orders", (OrderRequest? req, ILogger<Program> log) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.CustomerId))
    {
        // Intentional NRE for the triage demo.
        string? cart = null;
        log.LogError("Order request rejected because cart could not be resolved for null customer");
        return Results.Problem(detail: cart!.ToString(), statusCode: 500);
    }

    log.LogInformation("Order accepted for customer {CustomerId}", req.CustomerId);
    return Results.Accepted($"/orders/{Guid.NewGuid()}", new { ok = true });
});

// 2) Simulated upstream timeout.
app.MapGet("/payments/{id}", async (string id, ILogger<Program> log, CancellationToken ct) =>
{
    if (id.Length % 2 == 0)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            log.LogError(ex, "Payment provider call timed out for id={PaymentId}", id);
            return Results.Problem("Operation has timed out talking to payment provider.", statusCode: 504);
        }
    }

    return Results.Ok(new { id, amount = 42m });
});

// 3) Negative ids -> 500.
app.MapGet("/users/{id:int}", (int id, ILogger<Program> log) =>
{
    if (id < 0)
    {
        log.LogError("User lookup with invalid id {UserId} threw ArgumentOutOfRangeException", id);
        throw new ArgumentOutOfRangeException(nameof(id), id, "id must be non-negative");
    }

    return Results.Ok(new { id, name = $"user-{id}" });
});

app.Run();

internal sealed record OrderRequest(string? CustomerId, decimal Amount);
