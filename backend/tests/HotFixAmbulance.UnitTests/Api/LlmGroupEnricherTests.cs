using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Llm;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class LlmGroupEnricherTests
{
    private const string EvidenceMarker = "EVIDENCE-MARKER src/Orders/OrderProcessor.cs:54";

    private static ErrorGroup Baseline() => new()
    {
        Severity = Severity.Error,
        Count = 9,
        FirstSeenUtc = DateTimeOffset.Parse("2026-06-18T08:00:00Z"),
        LastSeenUtc = DateTimeOffset.Parse("2026-06-18T09:30:00Z"),
        ExceptionType = "System.NullReferenceException",
        Message = "Object reference not set to an instance of an object",
        Endpoint = "/orders/confirm",
        HttpStatus = 500,
        ServiceVersion = "1.4.2",
        CorrelationIdCount = 7,
        StackFile = "OrderProcessor.cs",
        StackSymbol = "OrderProcessor.GetCustomerEmail",
        StackLine = 54,
        Suggestion = "baseline heuristic suggestion",
        HowToFix = "baseline heuristic howtofix",
    };

    private static (LlmGroupEnricher Sut, IFixHintSource Hints, ILlmClient Llm) Build(
        string? evidence, string? llmResponse)
    {
        var hints = Substitute.For<IFixHintSource>();
        hints.BuildAsync(Arg.Any<string>(), Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>())
            .Returns(evidence);

        var llm = Substitute.For<ILlmClient>();
        llm.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        return (new LlmGroupEnricher(hints, new LlmPromptBuilder(), llm), hints, llm);
    }

    [Fact]
    public async Task EnrichAsync_writes_both_ai_columns_from_model_json_and_marks_llm()
    {
        const string response = "{\"suggestion\":\"A null customer lookup blew up email rendering.\","
            + "\"howToFix\":\"Guard GetCustomerEmail against a null customer; see commit 0f7ff6e.\"}";
        var (sut, _, llm) = Build(evidence: $"Where to fix: {EvidenceMarker}", llmResponse: response);

        var result = await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        result.Source.Should().Be("Llm");
        result.Group.Suggestion.Should().Be("A null customer lookup blew up email rendering.");
        result.Group.HowToFix.Should().Be("Guard GetCustomerEmail against a null customer; see commit 0f7ff6e.");

        // The model must have been handed the git evidence so its answer is grounded.
        await llm.Received(1).CompleteJsonAsync(
            Arg.Any<string>(),
            Arg.Is<string>(u => u.Contains(EvidenceMarker)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_falls_back_to_git_evidence_when_model_unavailable()
    {
        var (sut, _, _) = Build(evidence: $"Where to fix: {EvidenceMarker}", llmResponse: null);

        var result = await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        result.Source.Should().Be("Heuristic", "a null model response degrades to the heuristic path");
        result.Group.HowToFix.Should().Be($"Where to fix: {EvidenceMarker}", "fallback uses the already-fetched git evidence");
        result.Group.Suggestion.Should().Be("baseline heuristic suggestion");
    }

    [Fact]
    public async Task EnrichAsync_falls_back_when_model_returns_unparseable_json()
    {
        var (sut, _, _) = Build(evidence: null, llmResponse: "this is not json");

        var result = await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        result.Source.Should().Be("Heuristic");
        result.Group.HowToFix.Should().Be("baseline heuristic howtofix", "no evidence + bad json keeps the baseline");
        result.Group.Suggestion.Should().Be("baseline heuristic suggestion");
    }

    [Fact]
    public async Task EnrichAsync_does_not_call_model_more_than_once_per_group()
    {
        var (sut, hints, llm) = Build(evidence: "ev", llmResponse: "{\"suggestion\":\"s\",\"howToFix\":\"h\"}");

        await sut.EnrichAsync("checkout-api", Baseline(), CancellationToken.None);

        await llm.Received(1).CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await hints.Received(1).BuildAsync(Arg.Any<string>(), Arg.Any<ErrorGroup>(), Arg.Any<CancellationToken>());
    }
}
