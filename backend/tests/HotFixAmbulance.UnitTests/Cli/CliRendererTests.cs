using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.Cli;
using HotFixAmbulance.Core;
using Xunit;

namespace HotFixAmbulance.UnitTests.Cli;

public sealed class CliRendererTests
{
    [Fact]
    public async Task RenderTableAsync_writes_header_and_rows_with_suggestion_and_fix()
    {
        var when = new DateTimeOffset(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);
        var group = new ErrorGroup
        {
            Severity = Severity.Error,
            Count = 3,
            FirstSeenUtc = when,
            LastSeenUtc = when.AddMinutes(5),
            ExceptionType = "System.NullReferenceException",
            Message = "Object reference not set",
            Endpoint = "/checkout/confirm",
            HttpStatus = 500,
            ServiceVersion = "1.2.3",
            CorrelationIdCount = 2,
            Suggestion = "Null guard missing",
            HowToFix = "abcdef1 (2026-06-15) — guard cart.Items",
        };
        var result = new TriageResult(
            Guid.NewGuid(),
            "checkout-api",
            when,
            TimeSpan.FromHours(24),
            FromUtc: when.AddHours(-24),
            ToUtc: when,
            TotalLogs: 3,
            IsTruncated: false,
            Groups: [group]);

        await using var writer = new StringWriter();
        await CliRenderer.RenderTableAsync(writer, result);

        var output = writer.ToString();
        output.Should().Contain("checkout-api");
        output.Should().Contain("lookback 1d");
        output.Should().Contain("[Error]");
        output.Should().Contain("System.NullReferenceException");
        output.Should().Contain("/checkout/confirm");
        output.Should().Contain("Suggestion: Null guard missing");
        output.Should().Contain("abcdef1 (2026-06-15)");
    }
}
