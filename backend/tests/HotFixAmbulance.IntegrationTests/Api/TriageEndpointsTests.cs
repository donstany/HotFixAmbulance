using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task POST_triage_includes_explicit_where_to_fix_guidance_in_howtofix()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var groups = doc.RootElement.GetProperty("groups");
        groups.GetArrayLength().Should().BeGreaterThan(0);

        var howToFix = groups[0].GetProperty("howToFix").GetString();
        howToFix.Should().NotBeNullOrWhiteSpace();
        howToFix!.Should().Contain("Where to fix", because: "recommendations must explicitly tell developers where to apply the change");
    }

    [Fact]
    public async Task POST_triage_with_absolute_fromUtc_and_toUtc_succeeds_and_echoes_window()
    {
        using var client = _factory.CreateClient();
        var fromUtc = "2026-06-18T08:00:00Z";
        var toUtc = "2026-06-18T10:00:00Z";

        var response = await client.PostAsync(
            new Uri($"/api/triage/checkout-api?fromUtc={fromUtc}&toUtc={toUtc}", UriKind.Relative),
            content: null);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("fromUtc").GetString().Should().Be("2026-06-18T08:00:00+00:00");
        doc.RootElement.GetProperty("toUtc").GetString().Should().Be("2026-06-18T10:00:00+00:00");
        doc.RootElement.GetProperty("isTruncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task POST_triage_with_both_lookback_and_absolute_returns_400()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/triage/checkout-api?lookbackHours=24&fromUtc=2026-06-18T08:00:00Z&toUtc=2026-06-18T10:00:00Z", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_triage_with_only_fromUtc_returns_400()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/triage/checkout-api?fromUtc=2026-06-18T08:00:00Z", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_triage_with_inverted_range_returns_400()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/triage/checkout-api?fromUtc=2026-06-18T10:00:00Z&toUtc=2026-06-18T08:00:00Z", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_triage_with_no_params_uses_default_24h_lookback()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(new Uri("/api/triage/checkout-api", UriKind.Relative), content: null);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var lookback = doc.RootElement.GetProperty("lookback").GetString();
        lookback.Should().Be("1.00:00:00");
    }

    [Fact]
    public async Task GET_api_apis_returns_known_api_names_sorted()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/apis", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        var names = await response.Content.ReadFromJsonAsync<string[]>();
        names.Should().Contain("checkout-api");
    }

    [Fact]
    public async Task POST_absolute_then_GET_run_round_trips_FromUtc_and_ToUtc()
    {
        using var client = _factory.CreateClient();
        var fromUtc = "2026-06-18T08:00:00Z";
        var toUtc = "2026-06-18T10:00:00Z";

        var post = await client.PostAsync(
            new Uri($"/api/triage/checkout-api?fromUtc={fromUtc}&toUtc={toUtc}", UriKind.Relative),
            content: null);
        post.EnsureSuccessStatusCode();
        var postJson = await post.Content.ReadAsStringAsync();
        using var postDoc = JsonDocument.Parse(postJson);
        var runId = postDoc.RootElement.GetProperty("id").GetGuid();

        var get = await client.GetAsync(new Uri($"/api/triage/runs/{runId}", UriKind.Relative));
        get.EnsureSuccessStatusCode();
        var getJson = await get.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getJson);
        getDoc.RootElement.GetProperty("fromUtc").GetString().Should().Be("2026-06-18T08:00:00+00:00");
        getDoc.RootElement.GetProperty("toUtc").GetString().Should().Be("2026-06-18T10:00:00+00:00");
    }

    [Fact]
    public async Task POST_triage_with_absolute_range_exceeding_MaxRangeDays_returns_400()
    {
        using var client = _factory.CreateClient();
        // Default MaxRangeDays = 30; pick a 31-day span.
        var fromUtc = "2026-05-01T00:00:00Z";
        var toUtc = "2026-06-02T00:00:00Z";

        var response = await client.PostAsync(
            new Uri($"/api/triage/checkout-api?fromUtc={fromUtc}&toUtc={toUtc}", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_triage_with_fractional_lookbackHours_succeeds()
    {
        // The TimeRangePicker '15m' preset sends lookbackHours=0.25; this must not 400.
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/triage/checkout-api?lookbackHours=0.25", UriKind.Relative),
            content: null);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("lookback").GetString().Should().Be("00:15:00");
    }

    [Fact]
    public async Task GET_groups_returns_first_page_with_metadata()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups", UriKind.Relative));

        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(25);
        root.GetProperty("totalItems").GetInt32().Should().Be(1);
        root.GetProperty("totalPages").GetInt32().Should().Be(1);
        root.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GET_groups_with_invalid_pageSize_returns_400()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?pageSize=7", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_groups_with_unknown_sort_returns_400()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?sort=bogus", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_groups_out_of_range_page_returns_empty_items()
    {
        using var client = _factory.CreateClient();
        var post = await client.PostAsync(new Uri("/api/triage/checkout-api?lookbackHours=24", UriKind.Relative), content: null);
        post.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var res = await client.GetAsync(new Uri($"/api/triage/runs/{id}/groups?page=5", UriKind.Relative));

        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("totalItems").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GET_groups_for_unknown_run_returns_404()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync(new Uri($"/api/triage/runs/{Guid.NewGuid()}/groups", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

            // Fake git collaborators — return no commits so HowToFix falls back to the heuristic baseline.
            var cache = Substitute.For<IGitRepoCache>();
            cache.EnsureUpToDateAsync(Arg.Any<ApiRepoEntry>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult("/tmp/fake"));
            var reader = Substitute.For<IGitHistoryReader>();
            reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>(
                [
                    new CommitSummary(
                        Sha: "0123456789abcdef0123456789abcdef01234567",
                        When: new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero),
                        Author: "Demo Dev",
                        Subject: "Fix null cart handling",
                        Files: ["src/Checkout/ConfirmHandler.cs"]),
                ]));

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
    }
}
