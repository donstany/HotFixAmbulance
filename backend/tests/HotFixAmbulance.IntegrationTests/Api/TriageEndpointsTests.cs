using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.IntegrationTests.Api;

public sealed class TriageEndpointsTests : IClassFixture<TriageEndpointsTests.HfaFactory>
{
    private readonly HfaFactory _factory;

    public TriageEndpointsTests(HfaFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_200()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_triage_runs_pipeline_and_persists()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TriagePayload>();
        payload.Should().NotBeNull();
        payload!.ApiName.Should().Be("checkout-api");
        payload.TotalLogs.Should().Be(1);
        payload.Groups.Should().HaveCount(1);

        // Latest endpoint should now return the same run.
        var latest = await client.GetAsync(new Uri("/api/triage/checkout-api/latest", UriKind.Relative));
        latest.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record TriagePayload(Guid Id, string ApiName, int TotalLogs, IReadOnlyList<object> Groups);

    public sealed class HfaFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"hfa-it-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            var dbName = _dbName;

            // Bypass the FileSystem ApisConfig with an in-memory instance.
            var apis = new ApisConfig(new Dictionary<string, ApiRepoEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout-api"] = new ApiRepoEntry("checkout-api", new Uri("https://example.com/checkout-api.git"), "main"),
            });

            // Fake Elastic source — yields one Error log.
            var source = Substitute.For<IElasticLogSource>();
            source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
                .Returns(OneError());

            // Fake git collaborators — return no commits so HowToFix stays null.
            var cache = Substitute.For<IGitRepoCache>();
            cache.EnsureUpToDateAsync(Arg.Any<ApiRepoEntry>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult("/tmp/fake"));
            var reader = Substitute.For<IGitHistoryReader>();
            reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

            builder.ConfigureServices(services =>
            {
                // Swap real implementations for fakes.
                Remove<IElasticLogSource>(services);
                services.AddSingleton(source);

                Remove<IGitRepoCache>(services);
                services.AddSingleton(cache);

                Remove<IGitHistoryReader>(services);
                services.AddSingleton(reader);

                Remove<ApisConfig>(services);
                services.AddSingleton(apis);

                // Replace SQLite with EF InMemory so we don't touch disk.
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
    }
}
