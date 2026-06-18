namespace HotFixAmbulance.GitInsights;

public interface IGitHistoryReader
{
    Task<IReadOnlyList<CommitSummary>> SearchCommitsAsync(
        string repoPath,
        GitSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the file at <paramref name="fileHint"/> from the tip of the tracked branch and returns
    /// a snippet of <paramref name="contextLines"/> lines either side of <paramref name="line"/>,
    /// together with a <c>git blame</c> attribution for the offending line. The <paramref name="fileHint"/>
    /// may be a basename (e.g. <c>DemoDatabase.cs</c>) or a partial path; the reader resolves the
    /// closest match in the tree. Returns <c>null</c> when the file cannot be located, the line
    /// number is out of range, or the repo is not valid.
    /// </summary>
    Task<FileLineContext?> GetLineContextAsync(
        string repoPath,
        string fileHint,
        int line,
        int contextLines = 2,
        CancellationToken cancellationToken = default);
}
