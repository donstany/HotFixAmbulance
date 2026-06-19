using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using HotFixAmbulance.GitInsights;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class GitFixHintEnricherTests
{
    private static ErrorGroup Baseline() => new()
    {
        Severity = Severity.Error,
        Count = 3,
        FirstSeenUtc = DateTimeOffset.Parse("2026-06-18T08:00:00Z"),
        LastSeenUtc = DateTimeOffset.Parse("2026-06-18T08:30:00Z"),
        ExceptionType = "System.NullReferenceException",
        Message = "boom",
        Endpoint = "/orders/confirm",
        HttpStatus = 500,
        ServiceVersion = "1.0.0",
        CorrelationIdCount = 2,
        StackFile = "OrderProcessor.cs",
        StackSymbol = "OrderProcessor.Handle",
        StackLine = 12,
        Suggestion = "baseline suggestion",
        HowToFix = "baseline howtofix",
    };

    [Fact]
    public async Task EnrichAsync_overrides_howtofix_with_git_evidence_and_marks_heuristic()
    {
        const string evidence = "Where to fix: src/Orders/OrderProcessor.cs:12 · OrderProcessor.Handle";
        var hints = Substitute.For<IFixHintSource>();
        hints.BuildAsync("checkout-api", Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>())
            .Returns(evidence);
        var sut = new GitFixHintEnricher(hints);

        var result = await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        result.Source.Should().Be("Heuristic");
        result.Group.HowToFix.Should().Be(evidence, "git evidence overrides the rule baseline");
        result.Group.Suggestion.Should().Be("baseline suggestion", "the git enricher only touches HowToFix");
    }

    [Fact]
    public async Task EnrichAsync_keeps_baseline_howtofix_when_no_git_evidence()
    {
        var hints = Substitute.For<IFixHintSource>();
        hints.BuildAsync(Arg.Any<string>(), Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var sut = new GitFixHintEnricher(hints);

        var result = await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        result.Source.Should().Be("Heuristic");
        result.Group.HowToFix.Should().Be("baseline howtofix", "no git history leaves the rule baseline intact");
    }
}
