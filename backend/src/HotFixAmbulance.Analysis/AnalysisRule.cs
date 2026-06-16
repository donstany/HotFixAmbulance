using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// A pattern-matching rule that produces the <see cref="ErrorGroup.Purpose"/> string when it matches.
/// Rules are evaluated in order and the FIRST match wins, so list more specific rules first.
/// </summary>
public sealed record AnalysisRule(string Name, string Purpose, Func<ErrorGroup, bool> Matches);
