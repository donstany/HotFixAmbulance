using System.Diagnostics;
using System.Reflection;
using DemoApi;
using Elastic.CommonSchema.Serilog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "HFA_");

var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
if (dbProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var sqlServerConnection = builder.Configuration.GetConnectionString("DemoDb")
        ?? "Server=localhost,14333;Database=HotFixDemo;User Id=sa;Password=Your_strong_Password123!;Encrypt=True;TrustServerCertificate=True;";
    builder.Services.AddDbContext<DemoApiDbContext>((_, opts) =>
        opts.UseSqlServer(sqlServerConnection, sql =>
        {
            sql.CommandTimeout(30);
            sql.EnableRetryOnFailure(3);
        }));
}
else
{
    // Fallback path for environments without SQL Server (tests/local experiments).
    var sqliteConnection = new SqliteConnection("Data Source=hotfix-demo;Mode=Memory;Cache=Shared");
    sqliteConnection.Open();
    builder.Services.AddSingleton(sqliteConnection);
    builder.Services.AddDbContext<DemoApiDbContext>((sp, opts) =>
        opts.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
}

builder.Services.AddSingleton<CustomerRepository>();
builder.Services.AddSingleton<OrderProcessor>();
builder.Services.AddSingleton<PaymentGateway>();
builder.Services.AddScoped<DatabaseFailureSimulator>();
builder.Services.AddSingleton<PricingEngine>();

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DemoApiDbContext>();
    db.Database.EnsureCreated();
}

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

// 2b) Simulated upstream HTTP 503 from external payment settlement dependency.
app.MapGet("/payments/{id}/settlement", async (string id, PaymentGateway gateway, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var status = await gateway.GetSettlementStatusAsync(id, ct);
        return Results.Ok(new { id, status });
    }
    catch (HttpRequestException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Upstream payments-api returned 503 for settlement id={SettlementId}", id);
        }
        return Results.Problem("Upstream payments-api is unavailable.", statusCode: 502);
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

// 4) Real DB write failure: unique invoice number conflict in SQL Server via EF Core.
app.MapPost("/invoices/duplicate", async (DatabaseFailureSimulator db, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        await db.TriggerDuplicateInvoiceAsync(ct);
        return Results.Ok(new { ok = true });
    }
    catch (DbUpdateException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "productiondatabase write failed due to unique invoice constraint violation");
        }
        return Results.Problem("Database constraint violation while persisting invoice.", statusCode: 500);
    }
});

// 5) Simulated DB timeout with descriptive productiondatabase wording.
app.MapGet("/invoices/reprice", (DatabaseFailureSimulator db, ILogger<Program> log) =>
{
    try
    {
        db.TriggerQueryTimeout();
        return Results.Ok(new { ok = true });
    }
    catch (TimeoutException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "productiondatabase repricing query timed out");
        }
        return Results.Problem("Database timeout while repricing invoices.", statusCode: 500);
    }
});

// 6) Code failure (not null reference): invalid pricing pipeline state.
app.MapGet("/pricing/preview", (decimal? subtotal, decimal? loyaltyMultiplier, PricingEngine pricing, ILogger<Program> log) =>
{
    try
    {
        var total = pricing.PreviewFinalAmount(subtotal ?? 120m, loyaltyMultiplier ?? 0m);
        return Results.Ok(new { total });
    }
    catch (InvalidOperationException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Pricing preview failed due to invalid discount pipeline configuration");
        }
        return Results.Problem(ex.Message, statusCode: 500, title: ex.GetType().Name);
    }
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

