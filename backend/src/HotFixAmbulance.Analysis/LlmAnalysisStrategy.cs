using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Reserved for a future Phase that delegates analysis to an LLM. Today it throws so callers can
/// register it via DI behind a feature flag without having to comment-out the implementation.
/// </summary>
public sealed class LlmAnalysisStrategy : IAnalysisStrategy
{
    public IReadOnlyList<ErrorGroup> Analyze(IReadOnlyCollection<LogEntry> logs) =>
        throw new NotImplementedException(
            "LLM-backed analysis is not yet implemented. Use HeuristicAnalyzer until Phase L lands.");
}
