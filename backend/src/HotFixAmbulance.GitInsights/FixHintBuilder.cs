using System.Globalization;
using System.Text;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Produces the "How to fix" column for an <see cref="ErrorGroup"/> by mining the API's git
/// history. The output is shaped like a short Claude-Code-style report:
/// <code>
///   demo-api/BrokenServices.cs:54 · OrderProcessor.GetCustomerEmail
///     └ likely introduced in 0f7ff6e (2026-06-16) by Stanislav Stanev
///     └ "refactor(triage): split Suggestion from HowToFix; fix Api config 500"
///     └ related: b773ed7 (2026-06-16) "Phase 8: demo-api with Serilog instrumentation"
/// </code>
/// When the analyzer surfaced a <see cref="ErrorGroup.StackFile"/>, that file is searched first
/// because it is the strongest evidence (the actual code path that threw). Otherwise the builder
/// falls back to keyword search over commit subject + body + paths.
/// </summary>
public sealed class FixHintBuilder
{
    private static readonly (string Pattern, string Canonical)[] MessageTokens =
    {
        ("deadlock", "deadlock"),
        ("timed out", "timeout"),
        ("timeout", "timeout"),
        ("null", "null"),
        ("validation", "validation"),
        ("unauthorized", "unauthorized"),
        ("forbidden", "forbidden"),
        ("cancelled", "cancelled"),
    };

    private readonly ApisConfig _config;
    private readonly IGitRepoCache _cache;
    private readonly IGitHistoryReader _reader;

    public FixHintBuilder(ApisConfig config, IGitRepoCache cache, IGitHistoryReader reader)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(reader);

        _config = config;
        _cache = cache;
        _reader = reader;
    }

    public async Task<string?> BuildAsync(string apiName, ErrorGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentNullException.ThrowIfNull(group);

        if (!_config.TryGet(apiName, out var entry))
        {
            return null;
        }

        string repoPath;
        try
        {
            repoPath = await _cache.EnsureUpToDateAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        // Step 1: when we know the exact stack file + line, pull the source snippet and a blame
        // attribution from the tip of the tracked branch. This is the strongest evidence we can
        // surface and lets the UI's "How to fix" column show the actual offending line.
        FileLineContext? snippet = null;
        if (!string.IsNullOrWhiteSpace(group.StackFile) && group.StackLine is int stackLine && stackLine > 0)
        {
            try
            {
                snippet = await _reader.GetLineContextAsync(
                    repoPath,
                    group.StackFile!,
                    stackLine,
                    contextLines: 2,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                snippet = null;
            }
        }

        // Step 2: keyword + preferred-file commit search for related fixes / history context.
        var keywords = ExtractKeywords(group);
        var preferredFile = group.StackFile;
        IReadOnlyList<CommitSummary> commits = Array.Empty<CommitSummary>();
        if (keywords.Count > 0 || !string.IsNullOrWhiteSpace(preferredFile))
        {
            var query = new GitSearchQuery(keywords, MaxResults: 3, PreferredFile: preferredFile);
            commits = await _reader.SearchCommitsAsync(repoPath, query, cancellationToken).ConfigureAwait(false);
        }

        if (snippet is null && commits.Count == 0)
        {
            return null;
        }

        var capped = commits.Take(3).ToArray();
        return FormatHint(group, snippet, capped);
    }

    private static string FormatHint(ErrorGroup group, FileLineContext? snippet, CommitSummary[] commits)
    {
        var sb = new StringBuilder();

        // Header: "Where to fix: <repo-path>:<line> · <symbol>"
        var displayPath = snippet?.ResolvedPath
            ?? ResolveDisplayPath(commits.Length > 0 ? commits[0].Files : Array.Empty<string>(), group.StackFile)
            ?? group.StackFile
            ?? "(unknown file)";
        sb.Append("Where to fix: ");
        sb.Append(displayPath);
        var headerLine = group.StackLine ?? snippet?.OffendingLine;
        if (headerLine is int hl)
        {
            sb.Append(':').Append(hl.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(group.StackSymbol))
        {
            sb.Append(" · ").Append(group.StackSymbol);
        }
        sb.AppendLine();

        // Code snippet block — the actual lines around the offending one, read from the tip of
        // the tracked branch. Marker '>>' highlights the throwing line.
        if (snippet is { Lines.Count: > 0 })
        {
            sb.AppendLine("  code (from origin/main):");
            var pad = (snippet.StartLine + snippet.Lines.Count - 1)
                .ToString(CultureInfo.InvariantCulture).Length;
            for (var i = 0; i < snippet.Lines.Count; i++)
            {
                var ln = snippet.StartLine + i;
                var marker = ln == snippet.OffendingLine ? ">>" : "  ";
                sb.Append("    ").Append(marker).Append(' ')
                  .Append(ln.ToString(CultureInfo.InvariantCulture).PadLeft(pad))
                  .Append(" | ")
                  .AppendLine(snippet.Lines[i]);
            }
        }

        // Precise blame attribution beats keyword guesses. Surface it as the first commit line.
        var emittedShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (snippet?.Blame is { } blame)
        {
            sb.Append("  └ blame: ").Append(ShortSha(blame.Sha))
              .Append(' ').Append('(').Append(blame.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')')
              .Append(" by ").Append(blame.Author).AppendLine();
            sb.Append("    \"").Append(blame.Subject).Append('"').AppendLine();
            emittedShas.Add(blame.Sha);
        }
        else if (commits.Length > 0)
        {
            var top = commits[0];
            sb.Append("  └ likely introduced in ").Append(ShortSha(top.Sha))
              .Append(' ').Append('(').Append(top.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')')
              .Append(" by ").Append(top.Author).AppendLine();
            sb.Append("    \"").Append(top.Subject).Append('"').AppendLine();
            emittedShas.Add(top.Sha);
        }

        foreach (var c in commits)
        {
            if (!emittedShas.Add(c.Sha))
            {
                continue;
            }
            sb.Append("  └ related: ").Append(ShortSha(c.Sha))
              .Append(' ').Append('(').Append(c.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')')
              .Append(" \"").Append(c.Subject).Append('"').AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string? ResolveDisplayPath(IReadOnlyList<string> files, string? stackFile)
    {
        if (string.IsNullOrWhiteSpace(stackFile))
        {
            return files.Count > 0 ? files[0] : null;
        }
        foreach (var f in files)
        {
            if (f.EndsWith('/' + stackFile, StringComparison.OrdinalIgnoreCase)
                || f.EndsWith('\\' + stackFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, stackFile, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }
        return files.Count > 0 ? files[0] : null;
    }

    private static string ShortSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private static HashSet<string> ExtractKeywords(ErrorGroup group)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(group.ExceptionType))
        {
            var leaf = group.ExceptionType.Split('.', StringSplitOptions.RemoveEmptyEntries)[^1];
            if (!string.IsNullOrEmpty(leaf))
            {
                set.Add(leaf);
            }
        }

        if (!string.IsNullOrWhiteSpace(group.StackSymbol))
        {
            // e.g. "OrderProcessor.GetCustomerEmail" -> add both halves.
            foreach (var part in group.StackSymbol.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 3)
                {
                    set.Add(part);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(group.Endpoint))
        {
            foreach (var seg in group.Endpoint.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg.Length >= 3 && !seg.StartsWith('{'))
                {
                    set.Add(seg);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(group.Message))
        {
            foreach (var (pattern, canonical) in MessageTokens)
            {
                if (group.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(canonical);
                }
            }
        }

        return set;
    }
}
