namespace HotFixAmbulance.Core;

/// <summary>
/// A single log document, normalized from Elasticsearch. See <c>.claude/skills/serilog-mapping/SKILL.md</c>
/// for the source-field mapping.
/// </summary>
public sealed record LogEntry
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required Severity Severity { get; init; }
    public required string ApiName { get; init; }
    public string? ServiceVersion { get; init; }
    public string? ExceptionType { get; init; }
    public string? Message { get; init; }
    public string? RequestMethod { get; init; }
    public string? Endpoint { get; init; }
    public int? HttpStatus { get; init; }
    public string? CorrelationId { get; init; }

    /// <summary>
    /// File name (no path) of the topmost user-code frame in the captured stack trace,
    /// e.g. <c>OrderProcessor.cs</c>. Used by the git-insights layer to find commits
    /// that touched the same file.
    /// </summary>
    public string? StackFile { get; init; }

    /// <summary>
    /// Fully-qualified symbol of the topmost user-code frame, e.g. <c>OrderProcessor.GetCustomerEmail</c>.
    /// Helps the suggestion builder name the failing method.
    /// </summary>
    public string? StackSymbol { get; init; }

    /// <summary>
    /// Line number from the topmost user-code frame, when available.
    /// </summary>
    public int? StackLine { get; init; }
}
