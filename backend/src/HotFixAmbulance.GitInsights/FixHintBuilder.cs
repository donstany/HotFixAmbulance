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

        var keywords = ExtractKeywords(group);
        var preferredFile = group.StackFile;
        if (keywords.Count == 0 && string.IsNullOrWhiteSpace(preferredFile))
        {
            return null;
        }

        var query = new GitSearchQuery(keywords, MaxResults: 3, PreferredFile: preferredFile);
        var commits = await _reader.SearchCommitsAsync(repoPath, query, cancellationToken).ConfigureAwait(false);
        if (commits.Count == 0)
        {
            return null;
        }

        // Defense-in-depth: cap to MaxResults even when the reader returned more (some
        // mock implementations ignore the cap; the production LibGit2 reader honours it).
        var capped = commits.Take(query.MaxResults).ToArray();
        return FormatHint(group, capped);
    }

    private static string FormatHint(ErrorGroup group, CommitSummary[] commits)
    {
        var top = commits[0];
        var sb = new StringBuilder();

        // Header: "<repo-relative-path>:<line> · <symbol>"
        var displayPath = ResolveDisplayPath(top.Files, group.StackFile) ?? group.StackFile ?? "(unknown file)";
        sb.Append("Where to fix: ");
        sb.Append(displayPath);
        if (group.StackLine is int line)
        {
            sb.Append(':').Append(line.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(group.StackSymbol))
        {
            sb.Append(" · ").Append(group.StackSymbol);
        }
        sb.AppendLine();

        // Top commit attribution.
        sb.Append("  └ likely introduced in ").Append(ShortSha(top.Sha))
          .Append(' ').Append('(').Append(top.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')')
          .Append(" by ").Append(top.Author).AppendLine();
        sb.Append("  └ \"").Append(top.Subject).Append('"').AppendLine();

        // Related commits.
        foreach (var c in commits.Skip(1))
        {
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
