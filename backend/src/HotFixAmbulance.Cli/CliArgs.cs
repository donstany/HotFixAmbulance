using System.Globalization;
using HotFixAmbulance.Core;

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
public sealed record CliArgs(
    string ApiName,
    TimeSpan Lookback,
    CliOutputFormat Format,
    bool OpenBrowser,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null)
{
    private const string UsageLine =
        "Usage: hot-fix-ambulance <apiName> [--lookback=24h | --from=<iso> --to=<iso>] [--format=json|table] [--no-open]";

    /// <summary>
    /// Builds the <see cref="TimeWindow"/> for this run. Returns an absolute window when both
    /// <see cref="FromUtc"/> and <see cref="ToUtc"/> are set; otherwise a relative window
    /// anchored at <paramref name="now"/> with <see cref="Lookback"/> as the duration.
    /// </summary>
    public TimeWindow ToWindow(DateTimeOffset now)
    {
        if (FromUtc is { } from && ToUtc is { } to)
        {
            return TimeWindow.Absolute(from, to);
        }
        return TimeWindow.Relative(now, Lookback);
    }

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
        var lookbackExplicit = false;
        var format = CliOutputFormat.Json;
        var openBrowser = true;
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

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
                        lookbackExplicit = true;
                        break;
                    }
                case "--from":
                    {
                        var value = inlineValue ?? Next(args, ref i, flag);
                        if (value is null)
                        {
                            return CliArgsResult.Fail("--from requires an ISO-8601 timestamp (e.g. 2026-06-18T08:00:00Z).");
                        }
                        if (!TryParseIso(value, out var parsed))
                        {
                            return CliArgsResult.Fail($"Invalid --from value '{value}'. Expected ISO-8601 (e.g. 2026-06-18T08:00:00Z).");
                        }
                        fromUtc = parsed;
                        break;
                    }
                case "--to":
                    {
                        var value = inlineValue ?? Next(args, ref i, flag);
                        if (value is null)
                        {
                            return CliArgsResult.Fail("--to requires an ISO-8601 timestamp (e.g. 2026-06-18T10:00:00Z).");
                        }
                        if (!TryParseIso(value, out var parsed))
                        {
                            return CliArgsResult.Fail($"Invalid --to value '{value}'. Expected ISO-8601 (e.g. 2026-06-18T10:00:00Z).");
                        }
                        toUtc = parsed;
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

        // Mutex + completeness validation for absolute window.
        var hasFrom = fromUtc is not null;
        var hasTo = toUtc is not null;
        if (hasFrom != hasTo)
        {
            return CliArgsResult.Fail("--from and --to must be supplied together.");
        }
        if (hasFrom && lookbackExplicit)
        {
            return CliArgsResult.Fail("--lookback is mutually exclusive with --from/--to.");
        }
        if (hasFrom && fromUtc >= toUtc)
        {
            return CliArgsResult.Fail("--from must be earlier than --to.");
        }

        // When an absolute window is supplied, expose its duration as Lookback for back-compat.
        if (hasFrom)
        {
            lookback = toUtc!.Value - fromUtc!.Value;
        }

        return CliArgsResult.Ok(new CliArgs(apiName, lookback, format, openBrowser, fromUtc, toUtc));
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

    private static bool TryParseIso(string raw, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
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
