using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.Elastic;

public class ElasticLogIngestorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    private static LogEntry Log(
        Severity severity = Severity.Error,
        DateTimeOffset? timestamp = null,
        string apiName = "demo-api")
        => new()
        {
            TimestampUtc = timestamp ?? FixedNow.AddMinutes(-30),
            Severity = severity,
            ApiName = apiName,
        };

    private static (ElasticLogIngestor ingestor, IElasticLogSource source) BuildSut(params LogEntry[] sourceReturns)
    {
        var source = Substitute.For<IElasticLogSource>();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
              .Returns(_ => ToAsync(sourceReturns));

        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(FixedNow);

        return (new ElasticLogIngestor(source, clock), source);
    }

    private static async IAsyncEnumerable<LogEntry> ToAsync(IEnumerable<LogEntry> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FetchAsync_BuildsQueryWithApiNameAndLookbackWindowEndingNow()
    {
        var (sut, source) = BuildSut();

        await sut.FetchAsync("demo-api", TimeSpan.FromHours(24), default);

        source.Received(1).SearchAsync(
            Arg.Is<LogQuery>(q =>
                q.ApiName == "demo-api" &&
                q.To == FixedNow &&
                q.From == FixedNow.AddHours(-24)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_RequestsFatalErrorWarningOnly()
    {
        var (sut, source) = BuildSut();

        await sut.FetchAsync("demo-api", TimeSpan.FromHours(1), default);

        source.Received(1).SearchAsync(
            Arg.Is<LogQuery>(q =>
                q.Severities.Count == 3 &&
                q.Severities.Contains(Severity.Fatal) &&
                q.Severities.Contains(Severity.Error) &&
                q.Severities.Contains(Severity.Warning)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ReturnsAllSourceEntries()
    {
        var (sut, _) = BuildSut(
            Log(severity: Severity.Fatal),
            Log(severity: Severity.Error),
            Log(severity: Severity.Warning));

        var result = await sut.FetchAsync("demo-api", TimeSpan.FromHours(24), default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task FetchAsync_FiltersDefensivelyToConfiguredSeverities()
    {
        // Source disregarded the filter and returned an Information-equivalent. The ingestor must drop it.
        // Severity is an enum so we use an out-of-range cast to simulate.
        var noisy = Log() with { Severity = (Severity)99 };
        var (sut, _) = BuildSut(Log(severity: Severity.Error), noisy);

        var result = await sut.FetchAsync("demo-api", TimeSpan.FromHours(24), default);

        result.Should().ContainSingle();
        result.Single().Severity.Should().Be(Severity.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task FetchAsync_ThrowsOnMissingApiName(string? apiName)
    {
        var (sut, _) = BuildSut();

        var act = () => sut.FetchAsync(apiName!, TimeSpan.FromHours(24), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FetchAsync_ThrowsOnNonPositiveLookback(int seconds)
    {
        var (sut, _) = BuildSut();

        var act = () => sut.FetchAsync("demo-api", TimeSpan.FromSeconds(seconds), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task FetchAsync_PropagatesCancellation()
    {
        var (sut, _) = BuildSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.FetchAsync("demo-api", TimeSpan.FromHours(1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchAsync_TimeWindow_PassesAbsoluteFromAndTo_VerbatimToSource()
    {
        var (sut, source) = BuildSut();
        var window = TimeWindow.Absolute(
            new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero));

        await sut.FetchAsync("demo-api", window, default);

        source.Received(1).SearchAsync(
            Arg.Is<LogQuery>(q =>
                q.ApiName == "demo-api" &&
                q.From == window.FromUtc &&
                q.To == window.ToUtc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_TimeWindow_FlagsTruncatedWhenLogsHitMaxDocuments()
    {
        var source = Substitute.For<IElasticLogSource>();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
              .Returns(_ => ToAsync(new[] { Log(), Log() }));

        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(FixedNow);

        var options = Microsoft.Extensions.Options.Options.Create(new ElasticOptions
        {
            Uri = new Uri("http://localhost:9200"),
            MaxDocuments = 2,
        });

        var sut = new ElasticLogIngestor(source, clock, options);
        var window = TimeWindow.Relative(FixedNow, TimeSpan.FromHours(1));

        var result = await sut.FetchAsync("demo-api", window, default);

        result.Logs.Should().HaveCount(2);
        result.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_TimeWindow_DoesNotFlagTruncatedWhenBelowCap()
    {
        var source = Substitute.For<IElasticLogSource>();
        source.SearchAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>())
              .Returns(_ => ToAsync(new[] { Log() }));

        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(FixedNow);

        var options = Microsoft.Extensions.Options.Options.Create(new ElasticOptions
        {
            Uri = new Uri("http://localhost:9200"),
            MaxDocuments = 10,
        });

        var sut = new ElasticLogIngestor(source, clock, options);
        var window = TimeWindow.Relative(FixedNow, TimeSpan.FromHours(1));

        var result = await sut.FetchAsync("demo-api", window, default);

        result.IsTruncated.Should().BeFalse();
    }
}
