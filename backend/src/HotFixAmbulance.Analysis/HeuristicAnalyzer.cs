using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Pure heuristic analyzer. Groups logs by <c>(ExceptionType, NormalizedMessage, Endpoint)</c>,
/// builds <see cref="ErrorGroup"/>s, fills <see cref="ErrorGroup.Suggestion"/> and
/// <see cref="ErrorGroup.HowToFix"/> using <see cref="DefaultRules"/> (or the rules supplied to the
/// constructor), and returns groups ranked by severity/count.
/// </summary>
public sealed class HeuristicAnalyzer : IAnalysisStrategy
{
    private readonly IReadOnlyList<AnalysisRule> _rules;

    public HeuristicAnalyzer() : this(DefaultRules.All)
    {
    }

    public HeuristicAnalyzer(IReadOnlyList<AnalysisRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    public IReadOnlyList<ErrorGroup> Analyze(IReadOnlyCollection<LogEntry> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        if (logs.Count == 0)
        {
            return [];
        }

        var groups = logs
            .GroupBy(static l => new GroupKey(
                l.ExceptionType ?? string.Empty,
                MessageNormalizer.Normalize(l.Message),
                l.Endpoint ?? string.Empty))
            .Select(g => ErrorGroup.FromLogs(g.ToArray()))
            .Select(EnrichWithRule)
            .ToArray();

        return ErrorGroup.RankBySeverity(groups);
    }

    private ErrorGroup EnrichWithRule(ErrorGroup group)
    {
        // Suggestion is always derived from the concrete facts of THIS group (exception type,
        // stack symbol, parsed message, endpoint, count) so the UI's "Suggestion for Error"
        // column reads like an AI triage note instead of a static rule label.
        var suggestion = SuggestionBuilder.Build(group);

        foreach (var rule in _rules)
        {
            if (rule.Matches(group))
            {
                return group with { Suggestion = suggestion, HowToFix = rule.HowToFix };
            }
        }
        // Even without a matching rule, surface the dynamic suggestion so the row is informative.
        return group with { Suggestion = suggestion };
    }

    private readonly record struct GroupKey(string ExceptionType, string NormalizedMessage, string Endpoint);
}
