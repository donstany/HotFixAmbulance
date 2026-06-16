using FluentAssertions;
using HotFixAmbulance.Analysis;
using Xunit;

namespace HotFixAmbulance.UnitTests.Analysis;

public sealed class LlmAnalysisStrategyTests
{
    [Fact]
    public void Analyze_throws_not_implemented_with_meaningful_message()
    {
        var sut = new LlmAnalysisStrategy();
        var act = () => sut.Analyze([]);

        act.Should().Throw<NotImplementedException>()
            .WithMessage("*LLM*");
    }
}
