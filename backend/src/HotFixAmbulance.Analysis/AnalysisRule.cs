using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// A pattern-matching rule that, when it matches an <see cref="ErrorGroup"/>, populates the two
/// AI-derived columns shown in the triage UI:
/// <list type="bullet">
///   <item><description><see cref="Suggestion"/> — WHAT the error means (renders in the
///     "Suggestion for Error" column).</description></item>
///   <item><description><see cref="HowToFix"/> — HOW to remediate it (renders in the "How to fix"
///     column; may be overridden later by the git-insights layer with a specific commit).</description></item>
/// </list>
/// Rules are evaluated in order and the FIRST match wins, so list more specific rules first.
/// </summary>
public sealed record AnalysisRule(
    string Name,
    string Suggestion,
    string HowToFix,
    Func<ErrorGroup, bool> Matches);
