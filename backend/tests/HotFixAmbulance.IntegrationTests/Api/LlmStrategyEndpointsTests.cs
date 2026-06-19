using System.Text.Json;
using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Llm;
using HotFixAmbulance.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.IntegrationTests.Api;

/// <summary>
/// End-to-end proof that <c>Analysis:Strategy=Llm</c> routes triage through the LLM enricher: the
/// two AI columns come from the (stubbed) model and the run is tagged <c>AnalyzedBy=Llm</c>.
/// </summary>
public sealed class LlmStrategyEndpointsTests : IClassFixture<LlmStrategyEndpointsTests.LlmFactory>
{
    private readonly LlmFactory _factory;

    public LlmStrategyEndpointsTests(LlmFactory factory) => _factory = factory;

    [Fact]
    public async Task POST_triage_with_Llm_strategy_fills_ai_columns_from_model_and_tags_run()
    {
        using var client = _factory.CreateClient();

        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();

        using var postDoc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        postDoc.RootElement.GetProperty("analyzedBy").GetString().Should().Be("Llm");
        var id = postDoc.RootElement.GetProperty("id").GetGuid();

        // Groups carry the model's two columns verbatim.
        var groups = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups", UriKind.Relative));
        groups.EnsureSuccessStatusCode();
        using var groupsDoc = JsonDocument.Parse(await groups.Content.ReadAsStringAsync());
        var first = groupsDoc.RootElement.GetProperty("items")[0];
        first.GetProperty("suggestion").GetString().Should().Be("LLM-SUGGESTION");
        first.GetProperty("howToFix").GetString().Should().Be("LLM-HOWTOFIX");

        // AnalyzedBy round-trips through persistence.
        var run = await client.GetAsync(new Uri($"/api/triage/runs/{id}", UriKind.Relative));
        run.EnsureSuccessStatusCode();
        using var runDoc = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        runDoc.RootElement.GetProperty("analyzedBy").GetString().Should().Be("Llm");
    }

    public sealed class LlmFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"hfa-llm-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            var dbName = _dbName;

            var apis = new ApisConfig(new Dictionary<string, ApiRepoEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout-api"] = new ApiRepoEntry("checkout-api", new Uri("https://example.com/checkout-api.git"), "main"),
            });

            var source = Substitute.For<IElasticLogSource>();
            source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(OneError());

            var cache = Substitute.For<IGitRepoCache>();
            cache.EnsureUpToDateAsync(Arg.Any<ApiRepoEntry>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult("/tmp/fake"));
            var reader = Substitute.For<IGitHistoryReader>();
            reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

            builder.ConfigureServices(services =>
            {
                Remove<IElasticLogSource>(services);
                services.AddSingleton(source);
                Remove<IGitRepoCache>(services);
                services.AddSingleton(cache);
                Remove<IGitHistoryReader>(services);
                services.AddSingleton(reader);
                Remove<ApisConfig>(services);
                services.AddSingleton(apis);

                // Replace the real Ollama client with a deterministic stub.
                RemoveAll<ILlmClient>(services);
                services.AddSingleton<ILlmClient>(new StubLlmClient());

                // Select the LLM enricher. The Analysis:Strategy config branch is decided at
                // registration time (before WebApplicationFactory can override config), so we drive
                // the strategy here by overriding the service; the config branch itself is unit-tested.
                RemoveAll<IGroupEnricher>(services);
                services.AddScoped<IGroupEnricher, LlmGroupEnricher>();

                var efDescriptors = services
                    .Where(d => d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
                        || d.ServiceType == typeof(DbContextOptions<HotFixDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType == typeof(HotFixDbContext))
                    .ToList();
                foreach (var d in efDescriptors)
                {
                    services.Remove(d);
                }
                services.AddDbContext<HotFixDbContext>(o => o.UseInMemoryDatabase(dbName));
            });
        }

        private static async IAsyncEnumerable<LogEntry> OneError()
        {
            await Task.CompletedTask;
            yield return new LogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                Severity = Severity.Error,
                ApiName = "checkout-api",
                ExceptionType = "System.NullReferenceException",
                Message = "Object reference not set to an instance of an object",
                Endpoint = "/checkout/confirm",
                HttpStatus = 500,
                StackFile = "ConfirmHandler.cs",
                StackSymbol = "ConfirmHandler.Handle",
                StackLine = 77,
            };
        }

        private static void Remove<TService>(IServiceCollection services)
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
            if (existing is not null)
            {
                services.Remove(existing);
            }
        }

        private static void RemoveAll<TService>(IServiceCollection services)
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(TService)).ToList())
            {
                services.Remove(d);
            }
        }

        private sealed class StubLlmClient : ILlmClient
        {
            public Task<string?> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>("{\"suggestion\":\"LLM-SUGGESTION\",\"howToFix\":\"LLM-HOWTOFIX\"}");
        }
    }
}
