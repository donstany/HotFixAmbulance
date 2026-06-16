using LibGit2Sharp;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Walks the local clone with LibGit2Sharp and keeps commits whose subject, body, or changed file
/// path contains any of the supplied keywords (case-insensitive). Mirrors the read-only commands
/// the <c>git-historian</c> agent is allowed to run.
/// </summary>
public sealed class LibGit2SharpHistoryReader : IGitHistoryReader
{
    public Task<IReadOnlyList<CommitSummary>> SearchCommitsAsync(
        string repoPath,
        GitSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Keywords.Count == 0 || !Repository.IsValid(repoPath))
        {
            return Task.FromResult<IReadOnlyList<CommitSummary>>([]);
        }

        var results = new List<CommitSummary>(query.MaxResults);
        using var repo = new Repository(repoPath);

        var keywords = query.Keywords.ToArray();
        var filter = new CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
        };

        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(commit, keywords, repo, out var files))
            {
                continue;
            }

            results.Add(new CommitSummary(
                Sha: commit.Sha,
                When: commit.Author.When,
                Author: commit.Author.Name,
                Subject: commit.MessageShort ?? string.Empty,
                Files: files));

            if (results.Count >= query.MaxResults)
            {
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<CommitSummary>>(results);
    }

    private static bool Matches(Commit commit, string[] keywords, Repository repo, out IReadOnlyList<string> files)
    {
        foreach (var keyword in keywords)
        {
            if (Contains(commit.Message, keyword) || Contains(commit.MessageShort, keyword))
            {
                files = ChangedFiles(commit, repo);
                return true;
            }
        }

        var changed = ChangedFiles(commit, repo);
        foreach (var path in changed)
        {
            foreach (var keyword in keywords)
            {
                if (Contains(path, keyword))
                {
                    files = changed;
                    return true;
                }
            }
        }

        files = Array.Empty<string>();
        return false;
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string[] ChangedFiles(Commit commit, Repository repo)
    {
        if (commit.Parents.FirstOrDefault() is not { } parent)
        {
            return Array.Empty<string>();
        }
        using var patch = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
        return patch.Select(c => c.Path).ToArray();
    }
}
