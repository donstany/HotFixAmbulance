using System.Text.Json;
using HotFixAmbulance.Core;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Llm;

namespace HotFixAmbulance.Api;

/// <summary>
/// LLM-backed group enricher. Gathers the same git-history evidence the heuristic path uses, asks
/// the model (via <see cref="ILlmClient"/>) to write the two AI columns grounded in that evidence,
/// and parses its JSON answer. On ANY failure — model unreachable, timeout, empty or unparseable
/// response — it degrades to the deterministic heuristic result built from the already-fetched
/// evidence, so triage never fails because the LLM is down.
/// </summary>
public sealed class LlmGroupEnricher : IGroupEnricher
{
    private readonly IFixHintSource _fixHints;
    private readonly LlmPromptBuilder _promptBuilder;
    private readonly ILlmClient _llm;

    public LlmGroupEnricher(IFixHintSource fixHints, LlmPromptBuilder promptBuilder, ILlmClient llm)
    {
        ArgumentNullException.ThrowIfNull(fixHints);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(llm);
        _fixHints = fixHints;
        _promptBuilder = promptBuilder;
        _llm = llm;
    }

    public async Task<EnrichedGroup> EnrichAsync(string apiName, ErrorGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentNullException.ThrowIfNull(group);

        var evidence = await _fixHints.BuildAsync(apiName, group, cancellationToken).ConfigureAwait(false);

        var prompt = _promptBuilder.Build(group, evidence);
        var raw = await _llm.CompleteJsonAsync(prompt.System, prompt.User, cancellationToken).ConfigureAwait(false);

        if (TryParseColumns(raw, out var suggestion, out var howToFix))
        {
            var updated = group with
            {
                Suggestion = string.IsNullOrWhiteSpace(suggestion) ? group.Suggestion : suggestion,
                HowToFix = string.IsNullOrWhiteSpace(howToFix) ? (evidence ?? group.HowToFix) : howToFix,
            };
            return new EnrichedGroup(updated, AnalysisStrategyNames.Llm);
        }

        // Model unavailable / unparseable — reuse the evidence we already fetched (no second git call).
        return GitFixHintEnricher.FromGitEvidence(group, evidence);
    }

    /// <summary>
    /// Parses the model's JSON answer, tolerating key casing. Returns false (→ fallback) when the
    /// response is null/blank, not a JSON object, or carries neither column.
    /// </summary>
    private static bool TryParseColumns(string? raw, out string? suggestion, out string? howToFix)
    {
        suggestion = null;
        howToFix = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                if (string.Equals(prop.Name, "suggestion", StringComparison.OrdinalIgnoreCase))
                {
                    suggestion = prop.Value.GetString();
                }
                else if (string.Equals(prop.Name, "howToFix", StringComparison.OrdinalIgnoreCase))
                {
                    howToFix = prop.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(suggestion) || !string.IsNullOrWhiteSpace(howToFix);
    }
}
