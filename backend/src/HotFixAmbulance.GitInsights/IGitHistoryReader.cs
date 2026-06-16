namespace HotFixAmbulance.GitInsights;

public interface IGitHistoryReader
{
    Task<IReadOnlyList<CommitSummary>> SearchCommitsAsync(
        string repoPath,
        GitSearchQuery query,
        CancellationToken cancellationToken = default);
}
