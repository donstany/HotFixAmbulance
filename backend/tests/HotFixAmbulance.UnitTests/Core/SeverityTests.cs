using FluentAssertions;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Core;

public class SeverityTests
{
    [Fact]
    public void Compare_Fatal_IsHigherThan_Error()
    {
        Severity.Fatal.CompareTo(Severity.Error).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_Error_IsHigherThan_Warning()
    {
        Severity.Error.CompareTo(Severity.Warning).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Fatal", Severity.Fatal)]
    [InlineData("fatal", Severity.Fatal)]
    [InlineData("ERROR", Severity.Error)]
    [InlineData("Warning", Severity.Warning)]
    [InlineData("warn", Severity.Warning)]
    public void Parse_ReturnsExpectedSeverity(string input, Severity expected)
    {
        Severities.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Info")]
    [InlineData("Debug")]
    [InlineData(null)]
    public void Parse_ReturnsFalse_ForUnsupportedLevels(string? input)
    {
        Severities.TryParse(input, out _).Should().BeFalse();
    }
}
