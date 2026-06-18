using FluentAssertions;
using HotFixAmbulance.Api;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class TriageOptionsTests
{
    [Fact]
    public void Defaults_match_documented_24h_lookback_and_30d_cap()
    {
        var opts = new TriageOptions();

        opts.DefaultLookbackHours.Should().Be(24);
        opts.MaxRangeDays.Should().Be(30);
    }

    [Fact]
    public void SectionName_is_Triage()
    {
        TriageOptions.SectionName.Should().Be("Triage");
    }
}
