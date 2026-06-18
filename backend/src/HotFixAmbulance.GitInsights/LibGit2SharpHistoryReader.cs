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
        var preferredFile = query.PreferredFile;
        var filter = new CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
        };

        // Pass 1: commits that touched the preferred source file (e.g. BrokenServices.cs).
        //         These are the strongest evidence -- they point at the exact file from the stack trace.
        if (!string.IsNullOrWhiteSpace(preferredFile))
        {
            foreach (var commit in repo.Commits.QueryBy(filter))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var files = ChangedFiles(commit, repo);
                if (!files.Any(f => EndsWithFile(f, preferredFile)))
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
                    return Task.FromResult<IReadOnlyList<CommitSummary>>(results);
                }
            }
        }

        // Pass 2: keyword search against subject/body/paths.
        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(commit, keywords, repo, out var files))
            {
                continue;
            }
            // De-dup against pass 1.
            if (results.Any(r => r.Sha == commit.Sha))
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

    private static bool EndsWithFile(string path, string fileName) =>
        path.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith('\\' + fileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, fileName, StringComparison.OrdinalIgnoreCase);

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

    public Task<FileLineContext?> GetLineContextAsync(
        string repoPath,
        string fileHint,
        int line,
        int contextLines = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileHint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Repository.IsValid(repoPath))
        {
            return Task.FromResult<FileLineContext?>(null);
        }

        using var repo = new Repository(repoPath);
        var head = repo.Head?.Tip;
        if (head is null)
        {
            return Task.FromResult<FileLineContext?>(null);
        }

        var resolvedPath = ResolvePathInTree(head.Tree, fileHint);
        if (resolvedPath is null)
        {
            return Task.FromResult<FileLineContext?>(null);
        }

        var treeEntry = head[resolvedPath];
        if (treeEntry?.Target is not Blob blob)
        {
            return Task.FromResult<FileLineContext?>(null);
        }

        string content;
        using (var reader = new StreamReader(blob.GetContentStream()))
        {
            content = reader.ReadToEnd();
        }
        var allLines = content.Split('\n');
        // Strip trailing CR left over from CRLF endings.
        for (var i = 0; i < allLines.Length; i++)
        {
            if (allLines[i].EndsWith('\r'))
            {
                allLines[i] = allLines[i][..^1];
            }
        }

        if (line > allLines.Length)
        {
            return Task.FromResult<FileLineContext?>(null);
        }

        var startIndex = Math.Max(0, line - 1 - contextLines);
        var endIndex = Math.Min(allLines.Length - 1, line - 1 + contextLines);
        var slice = new string[endIndex - startIndex + 1];
        Array.Copy(allLines, startIndex, slice, 0, slice.Length);

        CommitSummary? blame = null;
        try
        {
            var blameHunks = repo.Blame(resolvedPath, new BlameOptions
            {
                Strategy = BlameStrategy.Default,
            });
            // BlameHunk.FinalStartLineNumber is 0-based; covers ContentLineCount lines.
            foreach (var hunk in blameHunks)
            {
                var start = hunk.FinalStartLineNumber + 1;
                var endExclusive = start + hunk.LineCount;
                if (line >= start && line < endExclusive)
                {
                    var c = hunk.FinalCommit;
                    if (c is not null)
                    {
                        blame = new CommitSummary(
                            Sha: c.Sha,
                            When: c.Author.When,
                            Author: c.Author.Name,
                            Subject: c.MessageShort ?? string.Empty,
                            Files: new[] { resolvedPath });
                    }
                    break;
                }
            }
        }
        catch (LibGit2SharpException)
        {
            // Blame can fail on binary or untracked content; the snippet alone is still useful.
        }

        return Task.FromResult<FileLineContext?>(new FileLineContext(
            ResolvedPath: resolvedPath,
            StartLine: startIndex + 1,
            OffendingLine: line,
            Lines: slice,
            Blame: blame));
    }

    /// <summary>
    /// Locates <paramref name="fileHint"/> inside <paramref name="tree"/>. Tries (in order):
    /// direct lookup (treats <paramref name="fileHint"/> as a repo-relative path), then a
    /// recursive walk that matches by basename or path suffix. Returns the repo-relative path
    /// using forward slashes, or <c>null</c> when no match exists.
    /// </summary>
    private static string? ResolvePathInTree(Tree tree, string fileHint)
    {
        var normalized = fileHint.Replace('\\', '/').TrimStart('/');

        // Fast path: hint already matches a tree entry.
        if (tree[normalized] is { Target: Blob }) return normalized;

        var basename = normalized.Contains('/') ? normalized[(normalized.LastIndexOf('/') + 1)..] : normalized;
        string? suffixMatch = null;
        string? basenameMatch = null;

        var stack = new Stack<(Tree Node, string Prefix)>();
        stack.Push((tree, string.Empty));
        while (stack.Count > 0)
        {
            var (node, prefix) = stack.Pop();
            foreach (var entry in node)
            {
                var full = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
                switch (entry.TargetType)
                {
                    case TreeEntryTargetType.Tree:
                        stack.Push(((Tree)entry.Target, full));
                        break;
                    case TreeEntryTargetType.Blob:
                        if (full.EndsWith('/' + normalized, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(full, normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            return full;
                        }
                        if (suffixMatch is null && normalized.Contains('/')
                            && full.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            suffixMatch = full;
                        }
                        if (basenameMatch is null
                            && string.Equals(entry.Name, basename, StringComparison.OrdinalIgnoreCase))
                        {
                            basenameMatch = full;
                        }
                        break;
                }
            }
        }

        return suffixMatch ?? basenameMatch;
    }
}
