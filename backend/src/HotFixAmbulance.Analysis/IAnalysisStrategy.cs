using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Pluggable analysis strategy responsible for grouping and ranking logs. <see cref="HeuristicAnalyzer"/>
/// is the implementation; LLM involvement lives in the enrichment layer (the AI columns), not here, so
/// grouping/ranking stay deterministic.
/// </summary>
public interface IAnalysisStrategy
{
    /// <summary>
    /// Groups the supplied logs, fills <see cref="ErrorGroup.Suggestion"/> and
    /// <see cref="ErrorGroup.HowToFix"/> where a rule matches, and returns them ranked per
    /// <see cref="ErrorGroup.RankBySeverity"/>.
    /// </summary>
    IReadOnlyList<ErrorGroup> Analyze(IReadOnlyCollection<LogEntry> logs);
}
