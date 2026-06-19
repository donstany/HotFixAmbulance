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

    [Fact]
    public void Paginate_default_severity_desc_then_count_desc()
    {
        var groups = new[]
        {
            Group(Severity.Warning, 9),
            Group(Severity.Fatal, 1),
            Group(Severity.Error, 2),
            Group(Severity.Error, 8),
        };

        var page = GroupPager.Paginate(groups, page: 1, pageSize: 10, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Select(g => (g.Severity, g.Count)).Should().ContainInOrder(
            (Severity.Fatal, 1),
            (Severity.Error, 8),
            (Severity.Error, 2),
            (Severity.Warning, 9));
    }

    [Fact]
    public void Paginate_slices_and_reports_totals()
    {
        var groups = Enumerable.Range(0, 23).Select(i => Group(Severity.Error, i)).ToArray();

        var page2 = GroupPager.Paginate(groups, page: 2, pageSize: 10, GroupSortKey.Count, SortDirection.Asc);

        page2.Page.Should().Be(2);
        page2.PageSize.Should().Be(10);
        page2.TotalItems.Should().Be(23);
        page2.TotalPages.Should().Be(3);
        page2.Items.Should().HaveCount(10);
        page2.Items.Select(g => g.Count).Should().ContainInOrder(10, 11, 12, 13, 14, 15, 16, 17, 18, 19);
    }

    [Fact]
    public void Paginate_out_of_range_page_returns_empty_items_with_correct_totals()
    {
        var groups = Enumerable.Range(0, 5).Select(i => Group(Severity.Error, i)).ToArray();

        var page = GroupPager.Paginate(groups, page: 9, pageSize: 10, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(5);
        page.TotalPages.Should().Be(1);
    }

    [Fact]
    public void Paginate_empty_list_has_zero_pages()
    {
        var page = GroupPager.Paginate(Array.Empty<ErrorGroup>(), page: 1, pageSize: 25, GroupSortKey.Severity, SortDirection.Desc);

        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(0);
        page.TotalPages.Should().Be(0);
    }

    [Theory]
    [InlineData("severity", true, GroupSortKey.Severity)]
    [InlineData("COUNT", true, GroupSortKey.Count)]
    [InlineData("firstSeen", true, GroupSortKey.FirstSeen)]
    [InlineData(null, true, GroupSortKey.Severity)]
    [InlineData("bogus", false, GroupSortKey.Severity)]
    public void TryParseSort_handles_known_unknown_and_default(string? value, bool ok, GroupSortKey expected)
    {
        GroupPager.TryParseSort(value, out var key).Should().Be(ok);
        if (ok) key.Should().Be(expected);
    }

    [Theory]
    [InlineData("asc", true, SortDirection.Asc)]
    [InlineData("DESC", true, SortDirection.Desc)]
    [InlineData(null, true, SortDirection.Desc)]
    [InlineData("sideways", false, SortDirection.Desc)]
    public void TryParseDir_handles_known_unknown_and_default(string? value, bool ok, SortDirection expected)
    {
        GroupPager.TryParseDir(value, out var dir).Should().Be(ok);
        if (ok) dir.Should().Be(expected);
    }
}
