using HotFixAmbulance.Core;

namespace HotFixAmbulance.Api;

/// <summary>
/// Returned by <see cref="TriageService.RunAsync(string, System.TimeSpan, System.Threading.CancellationToken)"/>
/// and serialized to the React UI.
/// <see cref="FromUtc"/>/<see cref="ToUtc"/> always describe the absolute window the query used.
/// <see cref="IsTruncated"/> warns the UI when Elastic hit its <c>MaxDocuments</c> cap.
/// </summary>
public sealed record TriageResult(
    Guid Id,
    string ApiName,
    DateTimeOffset RequestedAtUtc,
    TimeSpan Lookback,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalLogs,
    bool IsTruncated,
    IReadOnlyList<ErrorGroup> Groups,
    string? AnalyzedBy = null);
