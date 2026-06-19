using HotFixAmbulance.Core;
using HotFixAmbulance.GitInsights;

namespace HotFixAmbulance.Api;

/// <summary>
/// Deterministic group enricher: overrides <see cref="ErrorGroup.HowToFix"/> with the git-history
/// evidence from <see cref="IFixHintSource"/> (leaving the heuristic <see cref="ErrorGroup.Suggestion"/>
/// untouched). This is the Milestone-1 behaviour, the default strategy, and the fallback that
/// <see cref="LlmGroupEnricher"/> delegates to when the model is unavailable.
/// </summary>
public sealed class GitFixHintEnricher : IGroupEnricher
{
    private readonly IFixHintSource _fixHints;

    public GitFixHintEnricher(IFixHintSource fixHints)
    {
        ArgumentNullException.ThrowIfNull(fixHints);
        _fixHints = fixHints;
    }

    public async Task<EnrichedGroup> EnrichAsync(string apiName, ErrorGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentNullException.ThrowIfNull(group);

        var evidence = await _fixHints.BuildAsync(apiName, group, cancellationToken).ConfigureAwait(false);
        var enriched = evidence is null ? group : group with { HowToFix = evidence };
        return new EnrichedGroup(enriched, AnalysisStrategyNames.Heuristic);
    }
}
