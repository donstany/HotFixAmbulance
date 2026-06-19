using HotFixAmbulance.Core;

namespace HotFixAmbulance.Api;

/// <summary>
/// Canonical names for the analysis strategy that produced a group's AI columns. Used both as the
/// per-group <see cref="EnrichedGroup.Source"/> and, aggregated, as the run-level "analyzed by" tag.
/// </summary>
public static class AnalysisStrategyNames
{
    public const string Heuristic = "Heuristic";
    public const string Llm = "Llm";

    /// <summary>Run-level tag when a run's groups were produced by more than one strategy.</summary>
    public const string Mixed = "Mixed";
}

/// <summary>An <see cref="ErrorGroup"/> after enrichment, tagged with the strategy that wrote its AI columns.</summary>
public sealed record EnrichedGroup(ErrorGroup Group, string Source);

/// <summary>
/// Post-analysis step that fills an <see cref="ErrorGroup"/>'s AI columns (Suggestion / HowToFix).
/// Implementations run once per group inside the triage pipeline. The deterministic
/// <see cref="GitFixHintEnricher"/> is the default and the fallback for <c>LlmGroupEnricher</c>.
/// </summary>
public interface IGroupEnricher
{
    Task<EnrichedGroup> EnrichAsync(string apiName, ErrorGroup group, CancellationToken cancellationToken = default);
}
