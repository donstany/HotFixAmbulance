using FluentAssertions;
using HotFixAmbulance.Cli;
using Xunit;

namespace HotFixAmbulance.UnitTests.Cli;

public sealed class CliArgsTests
{
    [Fact]
    public void Parse_returns_error_when_args_are_empty()
    {
        var result = CliArgs.Parse([]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Usage:");
    }

    [Fact]
    public void Parse_returns_error_when_first_token_is_a_flag()
    {
        var result = CliArgs.Parse(["--lookback", "24h"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Usage:");
    }

    [Fact]
    public void Parse_extracts_api_name_with_defaults()
    {
        var result = CliArgs.Parse(["checkout-api"]);

        result.IsValid.Should().BeTrue();
        result.Args!.ApiName.Should().Be("checkout-api");
        result.Args.Lookback.Should().Be(TimeSpan.FromHours(24));
        result.Args.Format.Should().Be(CliOutputFormat.Json);
        result.Args.OpenBrowser.Should().BeTrue();
    }

    [Theory]
    [InlineData("24h", 24 * 60)]
    [InlineData("60m", 60)]
    [InlineData("2d", 2 * 24 * 60)]
    [InlineData("12", 12 * 60)]
    public void Parse_supports_lookback_suffixes(string raw, int expectedMinutes)
    {
        var result = CliArgs.Parse(["checkout-api", "--lookback", raw]);

        result.IsValid.Should().BeTrue();
        result.Args!.Lookback.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void Parse_supports_equals_form_for_lookback()
    {
        var result = CliArgs.Parse(["checkout-api", "--lookback=6h"]);

        result.IsValid.Should().BeTrue();
        result.Args!.Lookback.Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Parse_rejects_invalid_lookback()
    {
        var result = CliArgs.Parse(["checkout-api", "--lookback", "soon"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("lookback");
    }

    [Fact]
    public void Parse_rejects_non_positive_lookback()
    {
        var result = CliArgs.Parse(["checkout-api", "--lookback", "0h"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("lookback");
    }

    [Fact]
    public void Parse_reads_no_open_flag()
    {
        var result = CliArgs.Parse(["checkout-api", "--no-open"]);

        result.IsValid.Should().BeTrue();
        result.Args!.OpenBrowser.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_format_table()
    {
        var result = CliArgs.Parse(["checkout-api", "--format", "table"]);

        result.IsValid.Should().BeTrue();
        result.Args!.Format.Should().Be(CliOutputFormat.Table);
    }

    [Fact]
    public void Parse_rejects_unknown_flag()
    {
        var result = CliArgs.Parse(["checkout-api", "--whatever"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Unknown");
    }

    [Fact]
    public void Parse_rejects_unknown_format()
    {
        var result = CliArgs.Parse(["checkout-api", "--format", "xml"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("format");
    }

    [Fact]
    public void Parse_accepts_absolute_from_and_to()
    {
        var result = CliArgs.Parse(["checkout-api", "--from", "2026-06-18T08:00:00Z", "--to", "2026-06-18T10:00:00Z"]);

        result.IsValid.Should().BeTrue();
        result.Args!.FromUtc.Should().Be(new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero));
        result.Args.ToUtc.Should().Be(new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero));
        result.Args.Lookback.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Parse_accepts_equals_form_for_from_and_to()
    {
        var result = CliArgs.Parse(["checkout-api", "--from=2026-06-18T08:00:00Z", "--to=2026-06-18T10:00:00Z"]);

        result.IsValid.Should().BeTrue();
        result.Args!.FromUtc.Should().NotBeNull();
        result.Args.ToUtc.Should().NotBeNull();
    }

    [Fact]
    public void Parse_rejects_from_without_to()
    {
        var result = CliArgs.Parse(["checkout-api", "--from", "2026-06-18T08:00:00Z"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("--from and --to");
    }

    [Fact]
    public void Parse_rejects_to_without_from()
    {
        var result = CliArgs.Parse(["checkout-api", "--to", "2026-06-18T10:00:00Z"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("--from and --to");
    }

    [Fact]
    public void Parse_rejects_lookback_combined_with_absolute_window()
    {
        var result = CliArgs.Parse(["checkout-api", "--lookback", "6h", "--from", "2026-06-18T08:00:00Z", "--to", "2026-06-18T10:00:00Z"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("mutually exclusive");
    }

    [Fact]
    public void Parse_rejects_inverted_absolute_range()
    {
        var result = CliArgs.Parse(["checkout-api", "--from", "2026-06-18T10:00:00Z", "--to", "2026-06-18T08:00:00Z"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("earlier than");
    }

    [Fact]
    public void Parse_rejects_invalid_iso_for_from()
    {
        var result = CliArgs.Parse(["checkout-api", "--from", "yesterday", "--to", "2026-06-18T10:00:00Z"]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("--from");
    }

    [Fact]
    public void ToWindow_returns_absolute_window_when_from_and_to_set()
    {
        var args = CliArgs.Parse(["checkout-api", "--from", "2026-06-18T08:00:00Z", "--to", "2026-06-18T10:00:00Z"]).Args!;

        var window = args.ToWindow(new DateTimeOffset(2026, 6, 18, 15, 0, 0, TimeSpan.Zero));

        window.FromUtc.Should().Be(new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero));
        window.ToUtc.Should().Be(new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToWindow_returns_relative_window_anchored_at_now_when_no_absolute_window()
    {
        var args = CliArgs.Parse(["checkout-api", "--lookback", "6h"]).Args!;
        var now = new DateTimeOffset(2026, 6, 18, 15, 0, 0, TimeSpan.Zero);

        var window = args.ToWindow(now);

        window.ToUtc.Should().Be(now);
        window.FromUtc.Should().Be(now - TimeSpan.FromHours(6));
    }
}
