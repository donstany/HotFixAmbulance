using FluentAssertions;
using HotFixAmbulance.Analysis;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Persistence;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class TriageServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    private static LogEntry Log(Severity sev = Severity.Error, string ex = "System.NullReferenceException", string ep = "/checkout") =>
        new()
        {
            TimestampUtc = FixedNow.AddMinutes(-5),
            Severity = sev,
            ApiName = "checkout-api",
            ExceptionType = ex,
            Message = "Object reference not set to an instance of an object",
            Endpoint = ep,
            HttpStatus = 500,
        };

    private static (TriageService Sut,
                    IElasticLogSource Source,
                    FixHintBuilder Hinter,
                    ITriageRunRepository Repo,
                    IGitRepoCache Cache,
                    IGitHistoryReader Reader) BuildSut(IGroupEnricher? enricher = null)
    {
        var clock = new FakeClock(FixedNow);
        var source = Substitute.For<IElasticLogSource>();
        var ingestor = new ElasticLogIngestor(source, clock);
        var analyzer = new HeuristicAnalyzer();

        var apis = new Dictionary<string, ApiRepoEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["checkout-api"] = new ApiRepoEntry("checkout-api", new Uri("https://example.com/checkout-api.git"), "main"),
        };
        var config = new ApisConfig(apis);

        var cache = Substitute.For<IGitRepoCache>();
        cache.EnsureUpToDateAsync(Arg.Any<ApiRepoEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(@"C:\fake\repo"));
        var reader = Substitute.For<IGitHistoryReader>();
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));
        var hinter = new FixHintBuilder(config, cache, reader);

        var repo = Substitute.For<ITriageRunRepository>();
        repo.AddAsync(Arg.Any<TriageRun>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<TriageRun>()));

        // Default path mirrors production: the deterministic git enricher wrapping FixHintBuilder.
        var enr = enricher ?? new GitFixHintEnricher(hinter);
        var sut = new TriageService(ingestor, analyzer, enr, repo, clock);
        return (sut, source, hinter, repo, cache, reader);
    }

    private static async IAsyncEnumerable<LogEntry> ToAsync(IEnumerable<LogEntry> logs)
    {
        await Task.CompletedTask;
        foreach (var l in logs) yield return l;
    }

    [Fact]
    public async Task RunAsync_throws_on_blank_apiName()
    {
        var (sut, _, _, _, _, _) = BuildSut();
        Func<Task> act = () => sut.RunAsync(" ", TimeSpan.FromHours(24));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_returns_empty_groups_when_no_logs()
    {
        var (sut, source, _, _, _, _) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(ToAsync([]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.ApiName.Should().Be("checkout-api");
        result.TotalLogs.Should().Be(0);
        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_analyzes_and_returns_groups_ranked_by_severity()
    {
        var (sut, source, _, _, _, _) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ToAsync([Log(Severity.Warning), Log(Severity.Fatal, ex: "System.OutOfMemoryException", ep: "/orders")]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.Groups.Should().HaveCount(2);
        result.Groups[0].Severity.Should().Be(Severity.Fatal);
        result.TotalLogs.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_fills_HowToFix_from_FixHintBuilder()
    {
        var (sut, source, _, _, _, reader) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(ToAsync([Log()]));
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>(
                [new CommitSummary("abcdef1234567890", FixedNow, "Bob", "Fix null in checkout", [])]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.Groups[0].HowToFix.Should().NotBeNullOrWhiteSpace();
        result.Groups[0].HowToFix.Should().Contain("abcdef1");
    }

    [Fact]
    public async Task RunAsync_persists_run_via_repository()
    {
        var (sut, source, _, repo, _, _) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(ToAsync([Log()]));

        await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        await repo.Received(1).AddAsync(
            Arg.Is<TriageRun>(r =>
                r.ApiName == "checkout-api"
                && r.TotalLogs == 1
                && r.GroupCount == 1
                && r.Lookback == TimeSpan.FromHours(24)
                && !string.IsNullOrEmpty(r.ErrorGroupsJson)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_tags_AnalyzedBy_Heuristic_for_the_default_git_enricher()
    {
        var (sut, source, _, _, _, _) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(ToAsync([Log()]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.AnalyzedBy.Should().Be("Heuristic");
    }

    [Fact]
    public async Task RunAsync_aggregates_AnalyzedBy_from_enricher_sources_and_persists_it()
    {
        var enricher = Substitute.For<IGroupEnricher>();
        enricher.EnrichAsync(Arg.Any<string>(), Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new EnrichedGroup(ci.Arg<ErrorGroup>(), "Llm")));
        var (sut, source, _, repo, _, _) = BuildSut(enricher);
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ToAsync([Log(), Log(Severity.Fatal, ex: "System.OutOfMemoryException", ep: "/orders")]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.AnalyzedBy.Should().Be("Llm", "all groups were enriched by the LLM strategy");
        await repo.Received(1).AddAsync(
            Arg.Is<TriageRun>(r => r.AnalyzedBy == "Llm"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_sets_AnalyzedBy_on_each_group_from_enricher_source()
    {
        var enricher = Substitute.For<IGroupEnricher>();
        enricher.EnrichAsync(Arg.Any<string>(), Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new EnrichedGroup(ci.Arg<ErrorGroup>(), "Llm")));
        var (sut, source, _, _, _, _) = BuildSut(enricher);
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ToAsync([Log()]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.Groups[0].AnalyzedBy.Should().Be("Llm");
    }

    [Fact]
    public async Task RunAsync_sets_AnalyzedBy_Heuristic_on_groups_for_default_git_enricher()
    {
        var (sut, source, _, _, _, _) = BuildSut();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ToAsync([Log()]));

        var result = await sut.RunAsync("checkout-api", TimeSpan.FromHours(24));

        result.Groups[0].AnalyzedBy.Should().Be("Heuristic");
    }

    [Fact]
    public async Task RunAsync_passes_lookback_to_ingestor()
    {
        var (sut, source, _, _, _, _) = BuildSut();
        LogQuery? captured = null;
        source.SearchAsync(Arg.Do<LogQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(ToAsync([]));

        await sut.RunAsync("checkout-api", TimeSpan.FromHours(12));

        captured.Should().NotBeNull();
        (captured!.To - captured.From).Should().Be(TimeSpan.FromHours(12));
        captured.ApiName.Should().Be("checkout-api");
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
