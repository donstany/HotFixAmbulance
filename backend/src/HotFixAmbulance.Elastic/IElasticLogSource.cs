using HotFixAmbulance.Core;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// Source of raw log documents. The default implementation talks to Elasticsearch; tests substitute a fake.
/// Implementations must stream results to allow paging without buffering everything in memory.
/// </summary>
public interface IElasticLogSource
{
    IAsyncEnumerable<LogEntry> SearchAsync(LogQuery query, CancellationToken cancellationToken = default);
}
