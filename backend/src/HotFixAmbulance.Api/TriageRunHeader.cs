namespace HotFixAmbulance.Api;

/// <summary>
/// HTTP shape for a triage run WITHOUT its error groups. Returned by run-creating and
/// run-fetching endpoints; the groups are fetched separately and paginated via
/// <c>GET /api/triage/runs/{id}/groups</c>.
/// </summary>
public sealed record TriageRunHeader(
    Guid Id,
    string ApiName,
    DateTimeOffset RequestedAtUtc,
    TimeSpan Lookback,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalLogs,
    bool IsTruncated,
    int TotalGroups,
    TriageSummary Summary);
