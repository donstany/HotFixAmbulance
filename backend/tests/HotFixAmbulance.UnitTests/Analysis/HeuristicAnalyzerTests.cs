using FluentAssertions;
using HotFixAmbulance.Analysis;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Analysis;

public sealed class HeuristicAnalyzerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    private static LogEntry Make(
        string api = "checkout",
        Severity severity = Severity.Error,
        string? exceptionType = "System.NullReferenceException",
        string? message = "Object reference not set to an instance of an object",
        string? endpoint = "/checkout",
        int offsetSeconds = 0,
        string? correlationId = null,
        int? status = 500) =>
        new()
        {
            TimestampUtc = BaseTime.AddSeconds(offsetSeconds),
            Severity = severity,
            ApiName = api,
            ExceptionType = exceptionType,
            Message = message,
            Endpoint = endpoint,
            HttpStatus = status,
            CorrelationId = correlationId,
        };

    [Fact]
    public void Analyze_returns_empty_for_empty_input()
    {
        var sut = new HeuristicAnalyzer();
        sut.Analyze([]).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_groups_logs_with_same_exception_normalized_message_and_endpoint()
    {
        var sut = new HeuristicAnalyzer();
        var logs = new[]
        {
            Make(message: "User 1234567 failed", correlationId: "c-1"),
            Make(message: "User 7654321 failed", correlationId: "c-2", offsetSeconds: 10),
            Make(message: "User 1111111 failed", correlationId: "c-3", offsetSeconds: 20),
        };

        var result = sut.Analyze(logs);

        result.Should().HaveCount(1);
        result[0].Count.Should().Be(3);
        result[0].CorrelationIdCount.Should().Be(3);
    }

    [Fact]
    public void Analyze_separates_different_exception_types()
    {
        var sut = new HeuristicAnalyzer();
        var logs = new[]
        {
            Make(exceptionType: "System.NullReferenceException", message: "Object ref"),
            Make(exceptionType: "System.TimeoutException", message: "Timed out", offsetSeconds: 5),
        };

        sut.Analyze(logs).Should().HaveCount(2);
    }

    [Fact]
    public void Analyze_separates_different_endpoints()
    {
        var sut = new HeuristicAnalyzer();
        var logs = new[]
        {
            Make(endpoint: "/checkout"),
            Make(endpoint: "/orders", offsetSeconds: 1),
        };

        sut.Analyze(logs).Should().HaveCount(2);
    }

    [Fact]
    public void Analyze_ranks_groups_by_severity_then_count()
    {
        var sut = new HeuristicAnalyzer();
        var logs = new[]
        {
            Make(severity: Severity.Warning, endpoint: "/a"),
            Make(severity: Severity.Warning, endpoint: "/a", offsetSeconds: 1),
            Make(severity: Severity.Fatal, endpoint: "/b", offsetSeconds: 2),
        };

        var result = sut.Analyze(logs);

        result[0].Severity.Should().Be(Severity.Fatal);
        result[1].Severity.Should().Be(Severity.Warning);
    }

    [Fact]
    public void Analyze_fills_purpose_for_null_reference()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze([Make(exceptionType: "System.NullReferenceException")]);

        result[0].Purpose.Should().NotBeNullOrWhiteSpace();
        result[0].Purpose!.ToLowerInvariant().Should().Contain("null");
    }

    [Fact]
    public void Analyze_fills_purpose_for_timeout()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze(
            [Make(exceptionType: "System.TimeoutException", message: "The operation has timed out")]);

        result[0].Purpose!.ToLowerInvariant().Should().Contain("timeout");
    }

    [Fact]
    public void Analyze_fills_purpose_for_database_deadlock()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze(
            [Make(exceptionType: "Microsoft.Data.SqlClient.SqlException",
                message: "Transaction was deadlocked on lock resources")]);

        result[0].Purpose!.ToLowerInvariant().Should().Contain("deadlock");
    }

    [Fact]
    public void Analyze_fills_purpose_for_validation_failure()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze(
            [Make(exceptionType: "FluentValidation.ValidationException",
                message: "Field X is required", status: 400, severity: Severity.Warning)]);

        result[0].Purpose!.ToLowerInvariant().Should().Contain("validation");
    }

    [Fact]
    public void Analyze_fills_generic_5xx_purpose_when_status_500_without_known_exception()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze(
            [Make(exceptionType: "App.Boom", message: "Boom", status: 503)]);

        result[0].Purpose!.ToLowerInvariant().Should().Contain("5xx");
    }

    [Fact]
    public void Analyze_leaves_purpose_null_when_no_rule_matches()
    {
        var sut = new HeuristicAnalyzer();
        var result = sut.Analyze(
            [Make(exceptionType: "App.Custom", message: "something unusual", status: 200, severity: Severity.Warning)]);

        result[0].Purpose.Should().BeNull();
    }

    [Fact]
    public void Analyze_throws_for_null_logs()
    {
        var sut = new HeuristicAnalyzer();
        var act = () => sut.Analyze(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
