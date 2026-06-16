using System.Globalization;
using HotFixAmbulance.Api;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.Cli;

/// <summary>
/// Human-readable rendering of a <see cref="TriageResult"/> for the CLI
/// <c>--format table</c> mode. JSON remains the contract for downstream agents.
/// </summary>
public static class CliRenderer
{
    public static async Task RenderTableAsync(TextWriter writer, TriageResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        await writer.WriteLineAsync(
            $"Triage for {result.ApiName} — {result.TotalLogs} log(s), {result.Groups.Count} group(s), lookback {Format(result.Lookback)}")
            .ConfigureAwait(false);
        await writer.WriteLineAsync(new string('-', 80)).ConfigureAwait(false);

        var rank = 1;
        foreach (var g in result.Groups)
        {
            await writer.WriteLineAsync(
                $"#{rank,-2} [{g.Severity}] x{g.Count} {g.ExceptionType ?? "(no type)"} @ {g.Endpoint ?? "(no endpoint)"}")
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(g.Suggestion))
            {
                await writer.WriteLineAsync($"    Suggestion: {g.Suggestion}").ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(g.HowToFix))
            {
                await writer.WriteLineAsync($"    How to fix:").ConfigureAwait(false);
                foreach (var line in g.HowToFix.Split('\n'))
                {
                    await writer.WriteLineAsync($"      {line}").ConfigureAwait(false);
                }
            }
            rank++;
        }
    }

    private static string Format(TimeSpan span)
    {
        if (span.TotalDays >= 1 && span.TotalDays % 1 == 0)
        {
            return span.TotalDays.ToString("0", CultureInfo.InvariantCulture) + "d";
        }
        if (span.TotalHours >= 1 && span.TotalHours % 1 == 0)
        {
            return span.TotalHours.ToString("0", CultureInfo.InvariantCulture) + "h";
        }
        return span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + "m";
    }
}
