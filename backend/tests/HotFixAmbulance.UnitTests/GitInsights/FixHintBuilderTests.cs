using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.GitInsights;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.GitInsights;

public sealed class FixHintBuilderTests
{
    private static ErrorGroup MakeGroup(
        string? exceptionType = "System.NullReferenceException",
        string? message = "Object reference not set to an instance of an object",
        string? endpoint = "/checkout/confirm") =>
        new()
        {
            Severity = Severity.Error,
            Count = 1,
            FirstSeenUtc = DateTimeOffset.UnixEpoch,
            LastSeenUtc = DateTimeOffset.UnixEpoch,
            ExceptionType = exceptionType,
            Message = message,
            Endpoint = endpoint,
            HttpStatus = 500,
            ServiceVersion = "1.0",
            CorrelationIdCount = 1,
        };

    private static (FixHintBuilder Sut, IGitRepoCache Cache, IGitHistoryReader Reader, ApisConfig Config) BuildSut(
        bool registerApi = true)
    {
        var entries = new Dictionary<string, ApiRepoEntry>(StringComparer.OrdinalIgnoreCase);
        if (registerApi)
        {
            entries["checkout-api"] = new ApiRepoEntry("checkout-api", new Uri("https://example.com/checkout-api.git"), "main");
        }
        var config = new ApisConfig(entries);

        var cache = Substitute.For<IGitRepoCache>();
        cache.EnsureUpToDateAsync(Arg.Any<ApiRepoEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(@"C:\fake\repo"));

        var reader = Substitute.For<IGitHistoryReader>();
        var sut = new FixHintBuilder(config, cache, reader);
        return (sut, cache, reader, config);
    }

    [Fact]
    public async Task BuildAsync_throws_when_apiName_blank()
    {
        var (sut, _, _, _) = BuildSut();
        Func<Task> act = () => sut.BuildAsync(" ", MakeGroup());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BuildAsync_returns_null_when_api_unknown()
    {
        var (sut, _, _, _) = BuildSut(registerApi: false);
        var hint = await sut.BuildAsync("checkout-api", MakeGroup());
        hint.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_returns_null_when_reader_finds_no_commits()
    {
        var (sut, _, reader, _) = BuildSut();
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

        var hint = await sut.BuildAsync("checkout-api", MakeGroup());
        hint.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_formats_single_commit_hint()
    {
        var (sut, _, reader, _) = BuildSut();
        var commit = new CommitSummary(
            Sha: "abcdef1234567890",
            When: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            Author: "Alice",
            Subject: "Guard against null cart in confirm flow",
            Files: ["src/Checkout/ConfirmHandler.cs"]);
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([commit]));

        var hint = await sut.BuildAsync("checkout-api", MakeGroup());

        hint.Should().NotBeNull();
        hint!.Should().Contain("abcdef1");
        hint.Should().Contain("Guard against null cart");
    }

    [Fact]
    public async Task BuildAsync_includes_all_commits_up_to_max()
    {
        var (sut, _, reader, _) = BuildSut();
        // SHAs must be 40 chars and differ in the first 7 so ShortSha produces unique tokens.
        var prefixes = new[] { "abc1234", "def5678", "ace9876", "feedbee", "deadbee" };
        var commits = prefixes.Select((p, i) => new CommitSummary(
                Sha: p + new string('0', 40 - p.Length),
                When: new DateTimeOffset(2026, 1, i + 1, 0, 0, 0, TimeSpan.Zero),
                Author: $"A{i}",
                Subject: $"Subject {i}",
                Files: ["src/file.cs"]))
            .ToArray();
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>(commits));

        var hint = await sut.BuildAsync("checkout-api", MakeGroup());

        hint.Should().NotBeNull();
        // The first three commits are surfaced; the rest are dropped (MaxResults = 3 by default).
        hint!.Should().Contain("abc1234");
        hint.Should().Contain("def5678");
        hint.Should().Contain("ace9876");
        hint.Should().NotContain("feedbee");
        hint.Should().NotContain("deadbee");
    }

    [Fact]
    public async Task BuildAsync_emits_file_line_and_symbol_header_when_stack_info_present()
    {
        var (sut, _, reader, _) = BuildSut();
        var commit = new CommitSummary(
            Sha: "0123456789abcdef0123456789abcdef01234567",
            When: new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero),
            Author: "Stan",
            Subject: "feat: order email notification",
            Files: ["demo-api/BrokenServices.cs"]);
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([commit]));

        var group = MakeGroup() with
        {
            StackFile = "BrokenServices.cs",
            StackSymbol = "OrderProcessor.GetCustomerEmail",
            StackLine = 54,
        };
        var hint = await sut.BuildAsync("checkout-api", group);

        hint.Should().NotBeNull();
        hint!.Should().Contain("demo-api/BrokenServices.cs:54");
        hint.Should().Contain("OrderProcessor.GetCustomerEmail");
        hint.Should().Contain("0123456");
        hint.Should().Contain("Stan");
        hint.Should().Contain("feat: order email notification");
    }

    [Fact]
    public async Task BuildAsync_passes_stack_file_as_preferred_file_to_reader()
    {
        var (sut, _, reader, _) = BuildSut();
        GitSearchQuery? captured = null;
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Do<GitSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

        var group = MakeGroup() with { StackFile = "BrokenServices.cs" };
        await sut.BuildAsync("checkout-api", group);

        captured.Should().NotBeNull();
        captured!.PreferredFile.Should().Be("BrokenServices.cs");
    }

    [Fact]
    public async Task BuildAsync_derives_keywords_from_exception_endpoint_and_message()
    {
        var (sut, _, reader, _) = BuildSut();
        GitSearchQuery? captured = null;
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Do<GitSearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

        await sut.BuildAsync(
            "checkout-api",
            MakeGroup(
                exceptionType: "System.TimeoutException",
                message: "Operation has timed out while contacting payments",
                endpoint: "/checkout/confirm"));

        captured.Should().NotBeNull();
        captured!.Keywords.Should().Contain("TimeoutException");
        captured.Keywords.Should().Contain(k => k.Contains("checkout", StringComparison.OrdinalIgnoreCase));
        captured.Keywords.Should().Contain(k => k.Equals("timeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_passes_branch_from_config_to_cache()
    {
        var (sut, cache, reader, _) = BuildSut();
        reader.SearchCommitsAsync(Arg.Any<string>(), Arg.Any<GitSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CommitSummary>>([]));

        await sut.BuildAsync("checkout-api", MakeGroup());

        await cache.Received(1).EnsureUpToDateAsync(
            Arg.Is<ApiRepoEntry>(e => e.Branch == "main" && e.Name == "checkout-api"),
            Arg.Any<CancellationToken>());
    }
}
