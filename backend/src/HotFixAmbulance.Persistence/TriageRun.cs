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

    /// <summary>
    /// Inclusive start of the analysis window. Nullable for back-compat with rows persisted before
    /// Phase 12.C; <c>Rehydrate</c> falls back to <c>RequestedAtUtc - Lookback</c> when null.
    /// </summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>
    /// Inclusive end of the analysis window. Nullable for back-compat with rows persisted before
    /// Phase 12.C; <c>Rehydrate</c> falls back to <c>RequestedAtUtc</c> when null.
    /// </summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>JSON-serialized <see cref="IReadOnlyList{ErrorGroup}"/> for the triage table.</summary>
    public required string ErrorGroupsJson { get; init; }

    /// <summary>
    /// Which analysis strategy produced this run's AI columns: <c>Heuristic</c>, <c>Llm</c>, or
    /// <c>Mixed</c> (some groups fell back). Nullable for back-compat with rows persisted before
    /// Milestone 2; <c>Rehydrate</c> tolerates null.
    /// </summary>
    public string? AnalyzedBy { get; init; }
}
