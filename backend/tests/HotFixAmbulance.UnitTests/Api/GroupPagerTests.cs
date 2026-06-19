using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class GroupPagerTests
{
    private static ErrorGroup Group(Severity sev, int count, string? suggestion = null, string? howToFix = null) => new()
    {
        Severity = sev,
        Count = count,
        FirstSeenUtc = new DateTimeOffset(2026, 6, 19, 8, 0, 0, TimeSpan.Zero),
        LastSeenUtc = new DateTimeOffset(2026, 6, 19, 9, 0, 0, TimeSpan.Zero),
        ExceptionType = null,
        Message = null,
        Endpoint = null,
        HttpStatus = null,
        ServiceVersion = null,
        CorrelationIdCount = 0,
        Suggestion = suggestion,
        HowToFix = howToFix,
    };

    [Fact]
    public void Summarize_counts_occurrences_severities_and_ai_columns()
    {
        var groups = new[]
        {
            Group(Severity.Fatal, 3, suggestion: "s"),
            Group(Severity.Error, 5, howToFix: "fix"),
            Group(Severity.Warning, 2, suggestion: "s", howToFix: "fix"),
        };

        var summary = GroupPager.Summarize(groups);

        summary.TotalGroups.Should().Be(3);
        summary.TotalOccurrences.Should().Be(10);
        summary.Fatal.Should().Be(1);
        summary.Error.Should().Be(1);
        summary.Warning.Should().Be(1);
        summary.WithSuggestions.Should().Be(2);
        summary.WithFixes.Should().Be(2);
    }
}
