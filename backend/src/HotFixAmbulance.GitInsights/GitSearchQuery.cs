namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Disjunction of keywords matched against commit subject + body + changed file paths.
/// Translated by <see cref="IGitHistoryReader"/> into something equivalent to
/// <c>git log --grep -i &lt;keyword&gt;</c> per the <c>git-historian</c> agent.
/// </summary>
public sealed record GitSearchQuery(IReadOnlyCollection<string> Keywords, int MaxResults = 3);
