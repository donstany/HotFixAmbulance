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

builder.Services.AddScoped<DatabaseFailureSimulator>();

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

// 1) Real CRUD: Create order for a customer. May fail if customer doesn't exist (foreign key).
app.MapPost("/orders", async (OrderRequest? req, DatabaseFailureSimulator db, DemoApiDbContext context, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var request = req ?? new OrderRequest(null, 0m);
        
        // Ensure customer exists or create a default one
        var customer = await context.Customers
            .FirstOrDefaultAsync(c => c.Email == (request.CustomerEmail ?? "default@example.com"), ct)
            .ConfigureAwait(false);
        
        if (customer == null)
        {
            customer = await db.CreateCustomerAsync(
                request.CustomerEmail?.Split('@')[0] ?? "unknown",
                request.CustomerEmail ?? "default@example.com",
                ct);
        }

        // Create order for customer
        var order = await db.CreateOrderAsync(customer.Id, request.Amount, ct);
        
        return Results.Accepted($"/orders/{order.Id}", new { orderId = order.Id, ok = true });
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

// 2) Real CRUD: Get payment by ID; may fail with argument validation.
app.MapGet("/payments/{id}", async (string id, DatabaseFailureSimulator db, DemoApiDbContext context, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var payment = await context.Payments
            .FirstOrDefaultAsync(p => p.PaymentId == id, ct)
            .ConfigureAwait(false);
        
        if (payment == null)
        {
            throw new InvalidOperationException($"Payment {id} not found");
        }

        return Results.Ok(new { id = payment.PaymentId, amount = payment.Amount, status = payment.Status });
    }
    catch (Exception ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Payment lookup failed for id={PaymentId}", id);
        }
        return Results.Problem(ex.Message, statusCode: 404);
    }
});

// 2b) Real CRUD: Get payment settlement status; may timeout or fail.
app.MapGet("/payments/{id}/settlement", async (string id, DemoApiDbContext context, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var payment = await context.Payments
            .FirstOrDefaultAsync(p => p.PaymentId == id, ct)
            .ConfigureAwait(false);
        
        if (payment == null)
        {
            throw new InvalidOperationException($"Settlement not found for payment {id}");
        }

        return Results.Ok(new { id = payment.PaymentId, status = payment.Status });
    }
    catch (Exception ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Upstream payments-api settlement lookup failed for id={SettlementId}", id);
        }
        return Results.Problem("Upstream payments-api is unavailable.", statusCode: 502);
    }
});

// 3) Real CRUD: Get user by ID; may fail with ArgumentOutOfRangeException on negative ID.
app.MapGet("/users/{id:int}", async (int id, DatabaseFailureSimulator db, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, $"User lookup with invalid id {id}");
        }

        var user = await db.GetUserAsync(id, ct);
        return Results.Ok(new { id = user.Id, name = user.Name, email = user.Email });
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
    catch (Exception ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "User lookup failed for id={UserId}", id);
        }
        return Results.Problem(ex.Message, statusCode: 500, title: ex.GetType().Name);
    }
});

// 4) Real CRUD: Create duplicate invoices to trigger unique constraint violation.
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
            log.LogError(ex, "Database write failed due to unique invoice constraint violation");
        }
        return Results.Problem("Database constraint violation while persisting invoice.", statusCode: 500);
    }
});

// 5) Real CRUD: Query invoices (simulates timeout).
app.MapGet("/invoices/reprice", async (DemoApiDbContext context, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var invoices = await context.Invoices
            .Take(100)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        
        return Results.Ok(new { count = invoices.Count, invoices });
    }
    catch (TimeoutException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", ex.GetType().FullName))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(ex, "Database repricing query timed out");
        }
        return Results.Problem("Database timeout while repricing invoices.", statusCode: 500);
    }
});

// 5b) Real CRUD: Create transfer on-hold due to SQL limit reached.
app.MapPost("/transfers/on-hold", async (DatabaseFailureSimulator db, DemoApiDbContext context, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        // Create a test customer if not exists
        var customer = await context.Customers
            .FirstOrDefaultAsync(c => c.Email == "wallet-processing@internal.local", ct)
            .ConfigureAwait(false) 
            ?? await db.CreateCustomerAsync("Wallet Processor", "wallet-processing@internal.local", ct);

        // Create transfer on hold
        var transfer = await db.CreateTransferOnHoldAsync(
            customer.Id,
            10000m,
            "2962544476,2962552075",
            ct);

        return Results.Ok(new { ok = true, transferId = transfer.TransferId });
    }
    catch (DbUpdateException ex)
    {
        var (file, symbol, line) = ResolveFailingFrame(ex);
        using (LogContext.PushProperty("ExceptionType", "Microsoft.Data.SqlClient.SqlException"))
        using (LogContext.PushProperty("StackFile", file))
        using (LogContext.PushProperty("StackSymbol", symbol))
        using (LogContext.PushProperty("StackLine", line))
        {
            log.LogError(
                ex,
                "Transfers on hold due to Error = Sql; Limit reached while applying payments to customer wallets. Rails={Rails}. PaymentIds={PaymentIds}",
                "FPS,SWIFT",
                "2962544476,2962552075");
        }

        return Results.Problem(
            "Transfers moved to On Hold because SQL limit was reached during wallet payout processing.",
            statusCode: 500,
            title: "SqlLimitReached");
    }
});

// 6) Real CRUD: Create and store pricing calculation.
app.MapGet("/pricing/preview", async (decimal? subtotal, decimal? loyaltyMultiplier, DatabaseFailureSimulator db, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        var total = await db.CreatePricingRecordAsync(subtotal ?? 120m, loyaltyMultiplier ?? 0m, ct);
        return Results.Ok(new { subtotal = subtotal ?? 120m, loyaltyMultiplier = loyaltyMultiplier ?? 0m, total });
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

/// <summary>
/// Request model for creating orders.
/// </summary>
public record OrderRequest(string? CustomerEmail, decimal Amount);