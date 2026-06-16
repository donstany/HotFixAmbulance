using HotFixAmbulance.Core;

namespace HotFixAmbulance.Api;

/// <summary>
/// Returned by <see cref="TriageService.RunAsync"/> and serialized to the React UI.
/// </summary>
public sealed record TriageResult(
    Guid Id,
    string ApiName,
    DateTimeOffset RequestedAtUtc,
    TimeSpan Lookback,
    int TotalLogs,
    IReadOnlyList<ErrorGroup> Groups);
