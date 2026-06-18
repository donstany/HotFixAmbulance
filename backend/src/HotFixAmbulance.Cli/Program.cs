using System.Text.Json;
using HotFixAmbulance.Analysis;
using HotFixAmbulance.Api;
using HotFixAmbulance.Cli;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var parsed = CliArgs.Parse(args);
if (!parsed.IsValid)
{
    await Console.Error.WriteLineAsync(parsed.Error).ConfigureAwait(false);
    return 2;
}

var cli = parsed.Args!;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "HFA_");

var configuration = builder.Configuration;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHotFixElastic(configuration);
builder.Services.AddHotFixGitInsights(configuration);
builder.Services.AddHotFixPersistence(configuration);
builder.Services.AddSingleton<IAnalysisStrategy, HeuristicAnalyzer>();

var apisConfigPath = configuration["Apis:ConfigPath"];
if (string.IsNullOrWhiteSpace(apisConfigPath))
{
    apisConfigPath = Path.Combine(AppContext.BaseDirectory, "apis.config.json");
}
builder.Services.AddSingleton(ApisConfig.LoadFromFile(apisConfigPath));
builder.Services.AddScoped<TriageService>();

using var host = builder.Build();

// Ensure database is created (SQLite by default).
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HotFixDbContext>();
    await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

try
{
    using var runScope = host.Services.CreateScope();
    var triage = runScope.ServiceProvider.GetRequiredService<TriageService>();
    var clock = runScope.ServiceProvider.GetRequiredService<TimeProvider>();
    var window = cli.ToWindow(clock.GetUtcNow());
    var result = await triage.RunAsync(cli.ApiName, window).ConfigureAwait(false);

    switch (cli.Format)
    {
        case CliOutputFormat.Json:
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
            })).ConfigureAwait(false);
            break;

        case CliOutputFormat.Table:
            await CliRenderer.RenderTableAsync(Console.Out, result).ConfigureAwait(false);
            break;
    }

    await Console.Error.WriteLineAsync($"Analysis id: {result.Id}").ConfigureAwait(false);
    if (cli.OpenBrowser)
    {
        await Console.Error.WriteLineAsync($"View full analysis: http://localhost:5173/?analysisId={result.Id}").ConfigureAwait(false);
    }
    return 0;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"hot-fix-ambulance failed: {ex.Message}").ConfigureAwait(false);
    return 1;
}
