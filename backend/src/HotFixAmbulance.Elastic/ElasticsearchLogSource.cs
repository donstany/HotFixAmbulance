using System.Runtime.CompilerServices;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using HotFixAmbulance.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// Production <see cref="IElasticLogSource"/>. Talks to the configured myPos Elasticsearch cluster using the
/// official v8 client, retries transient failures via Polly, and pages results with <c>search_after</c> per
/// the <c>elastic-query</c> skill.
/// </summary>
public sealed partial class ElasticsearchLogSource : IElasticLogSource
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ElasticsearchLogSource> _logger;

    public ElasticsearchLogSource(
        ElasticsearchClient client,
        IOptions<ElasticOptions> options,
        ILogger<ElasticsearchLogSource> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.Value;
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(250),
                ShouldHandle = new PredicateBuilder().Handle<TransportException>(),
                OnRetry = args =>
                {
                    LogRetry(_logger, args.AttemptNumber, args.RetryDelay, args.Outcome.Exception);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public async IAsyncEnumerable<LogEntry> SearchAsync(
        LogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var allowedLevels = query.Severities
            .Select(s => FieldValue.String(s.ToString()))
            .ToArray();

        var emitted = 0;
        ICollection<FieldValue>? cursor = null;

        while (emitted < _options.MaxDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturedCursor = cursor;
            var pageSize = Math.Min(_options.PageSize, _options.MaxDocuments - emitted);

            var response = await _pipeline.ExecuteAsync(
                async ct => await _client.SearchAsync<JsonElement>(s =>
                {
                    s.Indices(_options.IndexPattern)
                        .Size(pageSize)
                        .Sort(sort => sort.Field("@timestamp", c => c.Order(SortOrder.Asc)))
                        .Query(q => q.Bool(b => b
                            .Filter(
                                f => f.Term(t => t.Field("fields.Application.keyword").Value(query.ApiName)),
                                f => f.Terms(t => t
                                    .Field("level.keyword")
                                    .Terms(new TermsQueryField(allowedLevels))),
                                f => f.Range(r => r.Date(dr => dr
                                    .Field("@timestamp")
                                    .Gte(query.From.UtcDateTime)
                                    .Lte(query.To.UtcDateTime))))));

                    if (capturedCursor is not null)
                    {
                        s.SearchAfter(capturedCursor);
                    }
                }, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsValidResponse)
            {
                throw new InvalidOperationException(
                    $"Elasticsearch search failed: {response.DebugInformation}");
            }

            var hits = response.Hits.ToArray();
            if (hits.Length == 0)
            {
                yield break;
            }

            foreach (var hit in hits)
            {
                if (hit.Source.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entry = SerilogDocumentMapper.TryMap(hit.Source);
                if (entry is null)
                {
                    continue;
                }

                yield return entry;
                emitted++;

                if (emitted >= _options.MaxDocuments)
                {
                    yield break;
                }
            }

            var lastSort = hits[^1].Sort;
            cursor = lastSort is null ? null : [.. lastSort];
            if (cursor is null || cursor.Count == 0)
            {
                yield break;
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Elasticsearch transient failure (attempt {Attempt}). Retrying in {Delay}.")]
    private static partial void LogRetry(ILogger logger, int attempt, TimeSpan delay, Exception? exception);
}
