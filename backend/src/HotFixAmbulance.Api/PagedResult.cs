namespace HotFixAmbulance.Api;

/// <summary>
/// Transport envelope for a single page of results plus the totals a UI needs to render
/// pagination controls. Serialized to the React app.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
