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

    public static PagedResult<ErrorGroup> Paginate(
        IReadOnlyList<ErrorGroup> all, int page, int pageSize, GroupSortKey sort, SortDirection dir)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (!AllowedPageSizes.Contains(pageSize)) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var sorted = Sort(all, sort, dir);
        var totalItems = sorted.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<ErrorGroup>(items, page, pageSize, totalItems, totalPages);
    }

    public static bool TryParseSort(string? value, out GroupSortKey key)
    {
        key = GroupSortKey.Severity;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "severity": key = GroupSortKey.Severity; return true;
            case "count": key = GroupSortKey.Count; return true;
            case "firstseen": key = GroupSortKey.FirstSeen; return true;
            case "lastseen": key = GroupSortKey.LastSeen; return true;
            case "endpoint": key = GroupSortKey.Endpoint; return true;
            case "exceptiontype": key = GroupSortKey.ExceptionType; return true;
            case "correlations": key = GroupSortKey.Correlations; return true;
            default: return false;
        }
    }

    public static bool TryParseDir(string? value, out SortDirection dir)
    {
        dir = SortDirection.Desc;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "desc": dir = SortDirection.Desc; return true;
            case "asc": dir = SortDirection.Asc; return true;
            default: return false;
        }
    }

    private static List<ErrorGroup> Sort(IReadOnlyList<ErrorGroup> all, GroupSortKey key, SortDirection dir)
    {
        var primary = key switch
        {
            GroupSortKey.Count => Order(all, g => (IComparable)g.Count, dir),
            GroupSortKey.FirstSeen => Order(all, g => g.FirstSeenUtc, dir),
            GroupSortKey.LastSeen => Order(all, g => g.LastSeenUtc, dir),
            GroupSortKey.Endpoint => Order(all, g => g.Endpoint ?? string.Empty, dir),
            GroupSortKey.ExceptionType => Order(all, g => g.ExceptionType ?? string.Empty, dir),
            GroupSortKey.Correlations => Order(all, g => g.CorrelationIdCount, dir),
            _ => Order(all, g => g.Severity, dir),
        };
        // Stable tiebreaker mirrors ErrorGroup.RankBySeverity: count desc, then last-seen desc.
        return primary.ThenByDescending(g => g.Count).ThenByDescending(g => g.LastSeenUtc).ToList();
    }

    private static IOrderedEnumerable<ErrorGroup> Order<TKey>(
        IReadOnlyList<ErrorGroup> all, Func<ErrorGroup, TKey> selector, SortDirection dir)
        => dir == SortDirection.Desc ? all.OrderByDescending(selector) : all.OrderBy(selector);
}
