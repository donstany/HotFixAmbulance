using System.Text.Json;
using System.Text.Json.Serialization;
using HotFixAmbulance.Analysis;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "HFA_");

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHotFixElastic(builder.Configuration);
builder.Services.AddHotFixGitInsights(builder.Configuration);
builder.Services.AddHotFixPersistence(builder.Configuration);
builder.Services.AddSingleton<IAnalysisStrategy, HeuristicAnalyzer>();

// ApisConfig is loaded eagerly from a JSON file pointed to by Apis:ConfigPath.
// An empty string in config counts as "use default" — checked-in appsettings ships
// with `"Apis": { "ConfigPath": "" }` as a placeholder.
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var configured = cfg["Apis:ConfigPath"];
    var path = string.IsNullOrWhiteSpace(configured)
        ? ApisConfigPathResolver.Resolve()
        : configured;
    return ApisConfig.LoadFromFile(path);
});

builder.Services.AddScoped<TriageService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddProblemDetails();

var app = builder.Build();

// Ensure SQLite schema exists in development.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HotFixDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapPost("/api/triage/{apiName}", async (
    string apiName,
    [FromQuery] int? lookbackHours,
    TriageService service,
    CancellationToken ct) =>
{
    var lookback = TimeSpan.FromHours(lookbackHours is > 0 ? lookbackHours.Value : 24);
    var result = await service.RunAsync(apiName, lookback, ct);
    return Results.Ok(result);
});

app.MapGet("/api/triage/{apiName}/latest", async (
    string apiName,
    ITriageRunRepository repo,
    CancellationToken ct) =>
{
    var run = await repo.GetLatestAsync(apiName, ct);
    return run is null ? Results.NotFound() : Results.Ok(Rehydrate(run));
});

app.MapGet("/api/triage/runs/{id:guid}", async (
    Guid id,
    ITriageRunRepository repo,
    CancellationToken ct) =>
{
    var run = await repo.GetByIdAsync(id, ct);
    return run is null ? Results.NotFound() : Results.Ok(Rehydrate(run));
});

app.MapGet("/api/triage/{apiName}/history", async (
    string apiName,
    [FromQuery] int? take,
    ITriageRunRepository repo,
    CancellationToken ct) =>
{
    var runs = await repo.ListAsync(apiName, take is > 0 ? take.Value : 20, ct);
    return Results.Ok(runs);
});

app.Run();

static TriageResult Rehydrate(TriageRun run)
{
    var groups = JsonSerializer.Deserialize<List<ErrorGroup>>(run.ErrorGroupsJson) ?? new List<ErrorGroup>();
    return new TriageResult(
        run.Id,
        run.ApiName,
        run.RequestedAtUtc,
        run.Lookback,
        run.TotalLogs,
        groups);
}

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;

internal static class ApisConfigPathResolver
{
    /// <summary>
    /// Resolves the default <c>apis.config.json</c> location when <c>Apis:ConfigPath</c> is missing
    /// or blank: first the binary directory (production deployment layout), then the repo's
    /// <c>config/</c> folder so a developer running <c>dotnet run</c> just works, and finally the
    /// example file checked into <c>config/apis.config.example.json</c>.
    /// </summary>
    public static string Resolve()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "apis.config.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "config", "apis.config.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "config", "apis.config.example.json")),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        // Return the first candidate so the resulting error message points at the expected location.
        return candidates[0];
    }
}
