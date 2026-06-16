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
    public string? StackFile { get; init; }
    public string? StackSymbol { get; init; }
}
