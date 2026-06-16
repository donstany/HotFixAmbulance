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
}
