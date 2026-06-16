namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Disjunction of keywords matched against commit subject + body + changed file paths.
/// Translated by <see cref="IGitHistoryReader"/> into something equivalent to
/// <c>git log --grep -i &lt;keyword&gt;</c> per the <c>git-historian</c> agent.
/// When <paramref name="PreferredFile"/> is set, commits that touched a file whose basename
/// equals it are ranked first (so the hint can point at the actual source file from the stack
/// trace, e.g. <c>BrokenServices.cs</c>).
/// </summary>
public sealed record GitSearchQuery(
    IReadOnlyCollection<string> Keywords,
    int MaxResults = 3,
    string? PreferredFile = null);
