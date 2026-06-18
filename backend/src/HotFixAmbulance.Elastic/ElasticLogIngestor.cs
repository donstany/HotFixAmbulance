using HotFixAmbulance.Core;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// Orchestrates a single log fetch: validates inputs, builds a <see cref="LogQuery"/> for the given
/// API and lookback window, and applies a defensive severity filter on the way back.
/// </summary>
public sealed class ElasticLogIngestor
{
    private static readonly IReadOnlyCollection<Severity> AllowedSeverities =
    [
        Severity.Fatal,
        Severity.Error,
        Severity.Warning,
    ];

    private static readonly HashSet<Severity> AllowedSet = new(AllowedSeverities);

    private readonly IElasticLogSource _source;
    private readonly TimeProvider _clock;

    public ElasticLogIngestor(IElasticLogSource source, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        _source = source;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LogEntry>> FetchAsync(
        string apiName,
        TimeSpan lookback,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiName))
        {
            throw new ArgumentException("API name is required.", nameof(apiName));
        }

        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "Lookback must be positive.");
        }

        var window = TimeWindow.Relative(_clock.GetUtcNow(), lookback);
        return await FetchAsync(apiName, window, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches logs for the supplied API filtered by an absolute UTC <paramref name="window"/>.
    /// Prefer this overload over the <see cref="TimeSpan"/> one when the caller has explicit
    /// from/to timestamps (e.g. picked by the user in the UI).
    /// </summary>
    public async Task<IReadOnlyList<LogEntry>> FetchAsync(
        string apiName,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiName))
        {
            throw new ArgumentException("API name is required.", nameof(apiName));
        }

        ArgumentNullException.ThrowIfNull(window);

        cancellationToken.ThrowIfCancellationRequested();

        var query = new LogQuery
        {
            ApiName = apiName,
            From = window.FromUtc,
            To = window.ToUtc,
            Severities = AllowedSeverities,
        };

        var results = new List<LogEntry>();
        await foreach (var entry in _source.SearchAsync(query, cancellationToken).ConfigureAwait(false))
        {
            if (AllowedSet.Contains(entry.Severity))
            {
                results.Add(entry);
            }
        }

        return results;
    }
}
