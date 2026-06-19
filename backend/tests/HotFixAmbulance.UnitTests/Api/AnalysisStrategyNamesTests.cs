using FluentAssertions;
using HotFixAmbulance.Api;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class AnalysisStrategyNamesTests
{
    [Fact]
    public void Combine_returns_Heuristic_for_no_groups()
    {
        AnalysisStrategyNames.Combine([]).Should().Be("Heuristic");
    }

    [Fact]
    public void Combine_returns_the_single_strategy_when_all_groups_agree()
    {
        AnalysisStrategyNames.Combine(["Llm", "Llm", "Llm"]).Should().Be("Llm");
        AnalysisStrategyNames.Combine(["Heuristic", "Heuristic"]).Should().Be("Heuristic");
    }

    [Fact]
    public void Combine_returns_Mixed_when_strategies_differ()
    {
        AnalysisStrategyNames.Combine(["Llm", "Heuristic"]).Should().Be("Mixed");
    }
}
