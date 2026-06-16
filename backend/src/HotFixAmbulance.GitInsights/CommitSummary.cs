namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Read-only summary of a Git commit. Returned by <see cref="IGitHistoryReader"/>.
/// </summary>
public sealed record CommitSummary(
    string Sha,
    DateTimeOffset When,
    string Author,
    string Subject,
    IReadOnlyList<string> Files);
