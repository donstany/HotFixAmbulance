using HotFixAmbulance.Core;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// A typed query for the Elasticsearch log source. Built by <see cref="ElasticLogIngestor"/> and
/// translated to a DSL document by <see cref="IElasticLogSource"/> implementations.
/// </summary>
public sealed record LogQuery
{
    public required string ApiName { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyCollection<Severity> Severities { get; init; }
}
