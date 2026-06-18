using System;
using FluentAssertions;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Core;

public class TimeWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Relative_AnchorsEndAtNow_AndSubtractsLookback()
    {
        var window = TimeWindow.Relative(Now, TimeSpan.FromHours(24));

        window.ToUtc.Should().Be(Now);
        window.FromUtc.Should().Be(Now.AddHours(-24));
        window.Duration.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Absolute_PreservesFromAndTo_NormalizedToUtc()
    {
        var from = new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.FromHours(3));
        var to = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.FromHours(3));

        var window = TimeWindow.Absolute(from, to);

        window.FromUtc.Should().Be(from.ToUniversalTime());
        window.ToUtc.Should().Be(to.ToUniversalTime());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Relative_RejectsNonPositiveLookback(int hours)
    {
        var act = () => TimeWindow.Relative(Now, TimeSpan.FromHours(hours));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Absolute_RejectsFromEqualOrAfterTo()
    {
        var act = () => TimeWindow.Absolute(Now, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Absolute_RejectsFromAfterTo()
    {
        var act = () => TimeWindow.Absolute(Now, Now.AddHours(-1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Absolute_EnforcesDefaultMaxSpanOf30Days()
    {
        var act = () => TimeWindow.Absolute(Now.AddDays(-31), Now);

        act.Should().Throw<ArgumentException>().WithMessage("*span*exceeds*");
    }

    [Fact]
    public void Absolute_AllowsCustomMaxSpan()
    {
        var act = () => TimeWindow.Absolute(Now.AddDays(-2), Now, maxSpan: TimeSpan.FromDays(1));

        act.Should().Throw<ArgumentException>().WithMessage("*span*exceeds*");
    }

    [Fact]
    public void Relative_AllowsExactlyTheMaxSpan()
    {
        var window = TimeWindow.Relative(Now, TimeSpan.FromDays(30));

        window.Duration.Should().Be(TimeSpan.FromDays(30));
    }
}
