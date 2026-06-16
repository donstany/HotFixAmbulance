using System.Globalization;
using System.Text;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Produces the column-12 <c>HowToFix</c> hint for an <see cref="ErrorGroup"/> by querying the
/// related API's git history for commits that mention the same exception, endpoint, or symptom.
/// </summary>
public sealed class FixHintBuilder
{
    /// <summary>
    /// Patterns we look for in <see cref="ErrorGroup.Message"/>. The key is the substring to search,
    /// the value is the canonical keyword to emit so that git history search uses one stable term.
    /// </summary>
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
        if (keywords.Count == 0)
        {
            return null;
        }

        var query = new GitSearchQuery(keywords, MaxResults: 3);
        var commits = await _reader.SearchCommitsAsync(repoPath, query, cancellationToken).ConfigureAwait(false);
        if (commits.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var c in commits.Take(3))
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0} ({1:yyyy-MM-dd}) — {2}",
                c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha,
                c.When,
                c.Subject);
        }
        return sb.ToString();
    }

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
