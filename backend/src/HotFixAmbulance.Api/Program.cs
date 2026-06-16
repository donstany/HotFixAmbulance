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
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var path = cfg["Apis:ConfigPath"] ?? Path.Combine(AppContext.BaseDirectory, "apis.config.json");
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
