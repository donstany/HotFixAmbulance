using HotFixAmbulance.Core;

namespace HotFixAmbulance.Api;

/// <summary>Sort keys the paged groups endpoint accepts.</summary>
public enum GroupSortKey { Severity, Count, FirstSeen, LastSeen, Endpoint, ExceptionType, Correlations }

public enum SortDirection { Asc, Desc }

/// <summary>
/// Pure (no I/O) helpers that summarize, sort, and slice a fully-materialized list of
/// <see cref="ErrorGroup"/>. Used by the HTTP layer; unit-tested in isolation.
/// </summary>
public static class GroupPager
{
    public static readonly IReadOnlyList<int> AllowedPageSizes = new[] { 10, 25, 50, 100 };

    public static TriageSummary Summarize(IReadOnlyList<ErrorGroup> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return new TriageSummary(
            TotalGroups: all.Count,
            TotalOccurrences: all.Sum(g => g.Count),
            Fatal: all.Count(g => g.Severity == Severity.Fatal),
            Error: all.Count(g => g.Severity == Severity.Error),
            Warning: all.Count(g => g.Severity == Severity.Warning),
            WithSuggestions: all.Count(g => !string.IsNullOrWhiteSpace(g.Suggestion)),
            WithFixes: all.Count(g => !string.IsNullOrWhiteSpace(g.HowToFix)));
    }
}
