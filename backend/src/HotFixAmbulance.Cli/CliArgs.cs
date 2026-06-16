using System.Globalization;

namespace HotFixAmbulance.Cli;

/// <summary>
/// Output format for the CLI. <see cref="Json"/> is the contract for the
/// <c>log-analyzer</c> subagent; <see cref="Table"/> is for humans.
/// </summary>
public enum CliOutputFormat
{
    Json,
    Table,
}

/// <summary>
/// Parsed, validated CLI arguments for <c>/hot-fix-ambulance</c>.
/// </summary>
public sealed record CliArgs(string ApiName, TimeSpan Lookback, CliOutputFormat Format, bool OpenBrowser)
{
    private const string UsageLine =
        "Usage: hot-fix-ambulance <apiName> [--lookback=24h] [--format=json|table] [--no-open]";

    /// <summary>
    /// Pure parser — no I/O. Returns either a populated <see cref="CliArgs"/>
    /// or a human-readable error.
    /// </summary>
    public static CliArgsResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || args[0].StartsWith('-'))
        {
            return CliArgsResult.Fail(UsageLine);
        }

        var apiName = args[0];
        var lookback = TimeSpan.FromHours(24);
        var format = CliOutputFormat.Json;
        var openBrowser = true;

        for (var i = 1; i < args.Count; i++)
        {
            var (flag, inlineValue) = SplitFlag(args[i]);

            switch (flag)
            {
                case "--lookback":
                    {
                        var value = inlineValue ?? Next(args, ref i, flag);
                        if (value is null)
                        {
                            return CliArgsResult.Fail($"--lookback requires a value (e.g. 24h, 60m, 2d).");
                        }
                        if (!TryParseLookback(value, out var parsed))
                        {
                            return CliArgsResult.Fail($"Invalid --lookback value '{value}'. Expected forms: 24h, 60m, 2d, or positive integer hours.");
                        }
                        lookback = parsed;
                        break;
                    }
                case "--format":
                    {
                        var value = inlineValue ?? Next(args, ref i, flag);
                        if (value is null)
                        {
                            return CliArgsResult.Fail("--format requires a value (json or table).");
                        }
                        if (!Enum.TryParse<CliOutputFormat>(value, ignoreCase: true, out var parsedFormat))
                        {
                            return CliArgsResult.Fail($"Unknown --format value '{value}'. Expected: json or table.");
                        }
                        format = parsedFormat;
                        break;
                    }
                case "--no-open":
                    openBrowser = false;
                    break;
                default:
                    return CliArgsResult.Fail($"Unknown argument '{args[i]}'. {UsageLine}");
            }
        }

        return CliArgsResult.Ok(new CliArgs(apiName, lookback, format, openBrowser));
    }

    private static (string flag, string? inlineValue) SplitFlag(string token)
    {
        var eq = token.IndexOf('=', StringComparison.Ordinal);
        return eq < 0 ? (token, null) : (token[..eq], token[(eq + 1)..]);
    }

    private static string? Next(IReadOnlyList<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Count)
        {
            return null;
        }
        i++;
        return args[i];
    }

    private static bool TryParseLookback(string raw, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        var lastChar = trimmed[^1];
        var (numericPart, multiplier) = char.ToLowerInvariant(lastChar) switch
        {
            'h' => (trimmed[..^1], TimeSpan.FromHours(1)),
            'm' => (trimmed[..^1], TimeSpan.FromMinutes(1)),
            'd' => (trimmed[..^1], TimeSpan.FromDays(1)),
            _ => (trimmed, TimeSpan.FromHours(1)),
        };

        if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            return false;
        }

        result = multiplier * n;
        return true;
    }
}

/// <summary>
/// Discriminated result returned by <see cref="CliArgs.Parse"/>.
/// </summary>
public sealed record CliArgsResult(bool IsValid, CliArgs? Args, string? Error)
{
    public static CliArgsResult Ok(CliArgs args) => new(true, args, null);
    public static CliArgsResult Fail(string error) => new(false, null, error);
}
