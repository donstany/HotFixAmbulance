using System.Diagnostics;
using System.Reflection;
using DemoApi;
using Elastic.CommonSchema.Serilog;
using Serilog;
using Serilog.Context;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "HFA_");

builder.Services.AddSingleton<CustomerRepository>();
builder.Services.AddSingleton<OrderProcessor>();
builder.Services.AddSingleton<PaymentGateway>();

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

// 1) Real NullReferenceException on a missing customer field (".Email on a null Customer").
//    The stack frame of the throw points at OrderProcessor.GetCustomerEmail in BrokenServices.cs,
//    which is what the triage tool's git-insights layer keys off of.
app.MapPost("/orders", (OrderRequest? req, OrderProcessor processor, ILogger<Program> log) =>
{
    var request = req ?? new OrderRequest(null, 0m);
    try
    {
        var orderId = processor.PlaceOrder(request);
        return Results.Accepted($"/orders/{orderId}", new { ok = true });
    }
    catch (Exception ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "POST /orders failed: {Message}", ex.Message);
        }
        return Results.Problem(ex.Message, statusCode: 500, title: ex.GetType().Name);
    }
});

// 2) Simulated upstream timeout (deadline < provider p99).
app.MapGet("/payments/{id}", async (string id, PaymentGateway gateway, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var amount = await gateway.AuthorizeAsync(id, ct);
        return Results.Ok(new { id, amount });
    }
    catch (OperationCanceledException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Payment provider call timed out for id={PaymentId} after 50ms deadline", id);
        }
        return Results.Problem("Operation has timed out talking to payment provider.", statusCode: 504);
    }
});

// 3) Negative ids -> ArgumentOutOfRangeException -> 500.
app.MapGet("/users/{id:int}", (int id, ILogger<Program> log) =>
{
    if (id < 0)
    {
        try
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "id must be non-negative");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            var (file, symbol, line) = ResolveFailingFrame(ex);
            using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
            using (LogContext.PushProperty("StackFile", file))
            using (LogContext.PushProperty("StackSymbol", symbol))
            using (LogContext.PushProperty("StackLine", line))
            {
                log.LogError(ex, "User lookup with invalid id {UserId}", id);
            }
            return Results.Problem(ex.Message, statusCode: 500, title: ex.GetType().Name);
        }
    }

    return Results.Ok(new { id, name = $"user-{id}" });
});

app.Run();

// --- helpers ---

static (string? File, string? Symbol, int? Line) ResolveFailingFrame(Exception ex)
{
    var trace = new StackTrace(ex, fNeedFileInfo: true);
    foreach (var frame in trace.GetFrames())
    {
        var method = frame.GetMethod();
        if (method?.DeclaringType is null)
        {
            continue;
        }
        var ns = method.DeclaringType.Namespace ?? string.Empty;
        // Keep only frames in our own assemblies so we don't pick framework code.
        if (!ns.StartsWith("DemoApi", StringComparison.Ordinal) && ns != string.Empty)
        {
            continue;
        }
        var file = frame.GetFileName();
        if (string.IsNullOrEmpty(file))
        {
            continue;
        }
        var symbol = $"{method.DeclaringType.Name}.{method.Name}";
        var line = frame.GetFileLineNumber();
        return (Path.GetFileName(file), symbol, line > 0 ? line : null);
    }
    return (null, null, null);
}

