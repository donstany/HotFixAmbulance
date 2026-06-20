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

    /// <summary>
    /// File name of the topmost user-code frame from the most recent occurrence in this group,
    /// e.g. <c>OrderProcessor.cs</c>. Carried forward from the representative <see cref="LogEntry"/>
    /// so the git-insights layer can locate the offending source file.
    /// </summary>
    public string? StackFile { get; init; }

    /// <summary>
    /// Fully-qualified symbol of the topmost user-code frame, e.g. <c>OrderProcessor.GetCustomerEmail</c>.
    /// </summary>
    public string? StackSymbol { get; init; }

    /// <summary>
    /// Line number of the topmost user-code frame, when available.
    /// </summary>
    public int? StackLine { get; init; }

    /// <summary>
    /// AI-derived column 11 ("Suggestion for Error" in the UI). A short interpretation of WHAT the
    /// error means. Filled by the analysis layer; may be null before analysis or when no rule matches.
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary>
    /// AI-derived column 12. A concrete remediation hint (HOW to fix). Baseline value comes from the
    /// matched <see cref="AnalysisRule"/>; the git-insights layer may override it with recent
    /// <c>origin/main</c> commits when they exist. May be null when no rule matches.
    /// </summary>
    public string? HowToFix { get; init; }

    /// <summary>
    /// Strategy that wrote the AI columns for this group ("Heuristic", "Llm", etc.).
    /// Null on groups produced before this field was introduced.
    /// </summary>
    public string? AnalyzedBy { get; init; }

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
            StackFile = representative.StackFile,
            StackSymbol = representative.StackSymbol,
            StackLine = representative.StackLine,
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
