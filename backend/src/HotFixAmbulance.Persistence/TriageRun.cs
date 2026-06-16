using HotFixAmbulance.Core;

namespace HotFixAmbulance.Persistence;

/// <summary>
/// One execution of <c>/hot-fix-ambulance &lt;apiName&gt;</c>. Persisted so the UI can show history
/// and the user can revisit a previous triage without re-hitting Elastic.
/// </summary>
public sealed class TriageRun
{
    public Guid Id { get; init; }
    public required string ApiName { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required TimeSpan Lookback { get; init; }
    public required int TotalLogs { get; init; }
    public required int GroupCount { get; init; }

    /// <summary>JSON-serialized <see cref="IReadOnlyList{ErrorGroup}"/> for the triage table.</summary>
    public required string ErrorGroupsJson { get; init; }
}
