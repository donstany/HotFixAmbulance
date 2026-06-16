using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Pluggable analysis strategy. Today only <see cref="HeuristicAnalyzer"/> is wired up;
/// <see cref="LlmAnalysisStrategy"/> is reserved for a future Phase.
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
