using System;
using System.Collections.Generic;
using FluentAssertions;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Core;

public class ErrorGroupTests
{
    private static LogEntry Log(
        Severity severity = Severity.Error,
        string exceptionType = "System.NullReferenceException",
        string message = "Object reference not set",
        string endpoint = "POST /orders",
        DateTimeOffset? timestamp = null,
        string? correlationId = "abc")
        => new()
        {
            TimestampUtc = timestamp ?? DateTimeOffset.UtcNow,
            Severity = severity,
            ApiName = "demo-api",
            ExceptionType = exceptionType,
            Message = message,
            Endpoint = endpoint,
            CorrelationId = correlationId,
        };

    [Fact]
    public void FromLogs_ThrowsWhenEmpty()
    {
        var act = () => ErrorGroup.FromLogs(Array.Empty<LogEntry>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromLogs_AggregatesCountAndTimestamps()
    {
        var t1 = DateTimeOffset.Parse("2026-06-16T10:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-06-16T10:30:00Z");
        var t3 = DateTimeOffset.Parse("2026-06-16T11:00:00Z");
        var group = ErrorGroup.FromLogs(new[] { Log(timestamp: t2), Log(timestamp: t1), Log(timestamp: t3) });

        group.Count.Should().Be(3);
        group.FirstSeenUtc.Should().Be(t1);
        group.LastSeenUtc.Should().Be(t3);
    }

    [Fact]
    public void FromLogs_TakesSeverityFromHighestEntry()
    {
        var group = ErrorGroup.FromLogs(new[]
        {
            Log(severity: Severity.Warning),
            Log(severity: Severity.Error),
            Log(severity: Severity.Fatal),
        });

        group.Severity.Should().Be(Severity.Fatal);
    }

    [Fact]
    public void FromLogs_CountsDistinctCorrelationIds()
    {
        var group = ErrorGroup.FromLogs(new[]
        {
            Log(correlationId: "a"),
            Log(correlationId: "a"),
            Log(correlationId: "b"),
            Log(correlationId: null),
        });

        group.CorrelationIdCount.Should().Be(2);
    }

    [Fact]
    public void RankBySeverity_OrdersFatalBeforeErrorBeforeWarning_ThenCountDesc_ThenLastSeenDesc()
    {
        var older = DateTimeOffset.Parse("2026-06-16T08:00:00Z");
        var newer = DateTimeOffset.Parse("2026-06-16T09:00:00Z");

        var warn = ErrorGroup.FromLogs(new[] { Log(severity: Severity.Warning, timestamp: newer) });
        var err5 = ErrorGroup.FromLogs(new[] { Log(severity: Severity.Error,  timestamp: newer),
                                                  Log(severity: Severity.Error, timestamp: newer),
                                                  Log(severity: Severity.Error, timestamp: newer),
                                                  Log(severity: Severity.Error, timestamp: newer),
                                                  Log(severity: Severity.Error, timestamp: newer) });
        var err2Old = ErrorGroup.FromLogs(new[] { Log(severity: Severity.Error, timestamp: older),
                                                   Log(severity: Severity.Error, timestamp: older) });
        var err2New = ErrorGroup.FromLogs(new[] { Log(severity: Severity.Error, timestamp: newer),
                                                   Log(severity: Severity.Error, timestamp: newer) });
        var fatal = ErrorGroup.FromLogs(new[] { Log(severity: Severity.Fatal, timestamp: older) });

        var ranked = ErrorGroup.RankBySeverity(new[] { warn, err2Old, fatal, err5, err2New });

        ranked.Should().Equal(fatal, err5, err2New, err2Old, warn);
    }
}
