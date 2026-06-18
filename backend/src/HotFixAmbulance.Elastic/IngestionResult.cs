namespace HotFixAmbulance.Elastic;

/// <summary>
/// Outcome of a single <see cref="ElasticLogIngestor.FetchAsync(string, HotFixAmbulance.Core.TimeWindow, System.Threading.CancellationToken)"/>
/// call. <see cref="IsTruncated"/> is <c>true</c> when the fetch hit
/// <see cref="ElasticOptions.MaxDocuments"/>, signalling that more matching logs likely exist
/// in Elasticsearch but were not materialised — the caller should warn the user.
/// </summary>
public sealed record IngestionResult(IReadOnlyList<HotFixAmbulance.Core.LogEntry> Logs, bool IsTruncated);
