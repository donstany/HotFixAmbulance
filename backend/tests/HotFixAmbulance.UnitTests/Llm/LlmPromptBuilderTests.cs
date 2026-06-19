using System;
using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.Llm;
using Xunit;

namespace HotFixAmbulance.UnitTests.Llm;

public sealed class LlmPromptBuilderTests
{
    [Fact]
    public void Build_packs_group_facts_and_git_evidence_and_demands_json()
    {
        var group = new ErrorGroup
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
            HowToFix = "baseline heuristic how-to-fix",
        };

        const string gitEvidence =
            "Where to fix: src/Orders/OrderProcessor.cs:54 · OrderProcessor.GetCustomerEmail\n"
            + "  └ likely introduced in 0f7ff6e (2026-06-16) by Stan";

        var prompt = new LlmPromptBuilder().Build(group, gitEvidence);

        // 1. The system prompt pins the JSON contract the model must answer with.
        prompt.System.Should().NotBeNullOrWhiteSpace(
            "the system prompt instructs the model and must carry content");
        prompt.System.Should().ContainEquivalentOf("json",
            "the model is told to reply with JSON only");
        prompt.System.Should().Contain("suggestion",
            "\"suggestion\" is one of the JSON keys the model must return");
        prompt.System.Should().Contain("howToFix",
            "\"howToFix\" is one of the JSON keys the model must return");

        // 2. The user prompt carries the concrete group facts plus the verbatim git evidence.
        prompt.User.Should().Contain("System.NullReferenceException",
            "the exception type is a core fact for the model to reason about");
        prompt.User.Should().Contain("/orders/confirm",
            "the failing endpoint should be surfaced to the model");
        prompt.User.Should().Contain("OrderProcessor.GetCustomerEmail",
            "the stack symbol points the model at the offending method");
        prompt.User.Should().Contain("9",
            "the occurrence count signals impact to the model");
        prompt.User.Should().Contain("OrderProcessor.cs:54",
            "the git-history evidence must be passed through verbatim");
    }
}
