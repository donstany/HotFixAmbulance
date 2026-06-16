namespace HotFixAmbulance.Core;

/// <summary>
/// A group of log entries that share an exception fingerprint. Produced by the analysis layer and
/// presented as a single row in the triage UI.
/// </summary>
public sealed record ErrorGroup
{
    public required Severity Severity { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required string? ExceptionType { get; init; }
    public required string? Message { get; init; }
    public required string? Endpoint { get; init; }
    public required int? HttpStatus { get; init; }
    public required string? ServiceVersion { get; init; }
    public required int CorrelationIdCount { get; init; }

    /// <summary>AI-derived column 11. Filled by the analysis layer; may be null before analysis.</summary>
    public string? Purpose { get; init; }

    /// <summary>AI-derived column 12. Filled by the git-insights layer; may be null before lookup.</summary>
    public string? HowToFix { get; init; }

    public static ErrorGroup FromLogs(IReadOnlyCollection<LogEntry> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        if (logs.Count == 0)
        {
            throw new ArgumentException("At least one log entry is required to form an ErrorGroup.", nameof(logs));
        }

        var ordered = logs.OrderBy(l => l.TimestampUtc).ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var representative = ordered[^1];

        var distinctCorrelations = logs
            .Select(l => l.CorrelationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new ErrorGroup
        {
            Severity = logs.Max(l => l.Severity),
            Count = logs.Count,
            FirstSeenUtc = first.TimestampUtc,
            LastSeenUtc = last.TimestampUtc,
            ExceptionType = representative.ExceptionType,
            Message = representative.Message,
            Endpoint = representative.Endpoint,
            HttpStatus = representative.HttpStatus,
            ServiceVersion = representative.ServiceVersion,
            CorrelationIdCount = distinctCorrelations,
        };
    }

    /// <summary>
    /// Plan rule: severity desc, then count desc, then last-seen desc.
    /// </summary>
    public static IReadOnlyList<ErrorGroup> RankBySeverity(IEnumerable<ErrorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups
            .OrderByDescending(g => g.Severity)
            .ThenByDescending(g => g.Count)
            .ThenByDescending(g => g.LastSeenUtc)
            .ToList();
    }
}
