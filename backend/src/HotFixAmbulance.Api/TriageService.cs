using System.Text.Json;
using HotFixAmbulance.Analysis;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using HotFixAmbulance.GitInsights;
using HotFixAmbulance.Persistence;

namespace HotFixAmbulance.Api;

/// <summary>
/// End-to-end pipeline behind <c>POST /api/triage/{apiName}</c>:
/// Elastic → grouping/analysis → git-history hints → persist → response.
/// All cross-cutting policies (timeouts, retries) live in the collaborators.
/// </summary>
public sealed class TriageService
{
    private readonly ElasticLogIngestor _ingestor;
    private readonly IAnalysisStrategy _analyzer;
    private readonly FixHintBuilder _hinter;
    private readonly ITriageRunRepository _repository;
    private readonly TimeProvider _clock;

    public TriageService(
        ElasticLogIngestor ingestor,
        IAnalysisStrategy analyzer,
        FixHintBuilder hinter,
        ITriageRunRepository repository,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ingestor);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(hinter);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _ingestor = ingestor;
        _analyzer = analyzer;
        _hinter = hinter;
        _repository = repository;
        _clock = clock;
    }

    public async Task<TriageResult> RunAsync(string apiName, TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "Lookback must be positive.");
        }

        var requestedAt = _clock.GetUtcNow();

        var logs = await _ingestor.FetchAsync(apiName, lookback, cancellationToken).ConfigureAwait(false);

        var groups = _analyzer.Analyze(logs);

        var enriched = new List<ErrorGroup>(groups.Count);
        foreach (var g in groups)
        {
            var howToFix = await _hinter.BuildAsync(apiName, g, cancellationToken).ConfigureAwait(false);
            enriched.Add(howToFix is null ? g : g with { HowToFix = howToFix });
        }

        var run = new TriageRun
        {
            Id = Guid.NewGuid(),
            ApiName = apiName,
            RequestedAtUtc = requestedAt,
            Lookback = lookback,
            TotalLogs = logs.Count,
            GroupCount = enriched.Count,
            ErrorGroupsJson = JsonSerializer.Serialize(enriched),
        };

        await _repository.AddAsync(run, cancellationToken).ConfigureAwait(false);

        return new TriageResult(
            run.Id,
            run.ApiName,
            run.RequestedAtUtc,
            run.Lookback,
            run.TotalLogs,
            enriched);
    }
}
