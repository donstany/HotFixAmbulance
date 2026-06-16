using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Composes the "Suggestion for Error" column from the concrete facts of an <see cref="ErrorGroup"/>
/// — exception type, stack symbol, parsed message and endpoint — instead of a static per-rule label.
/// The intent is to make the column read like a short AI triage note such as
/// "NullReferenceException dereferencing `Customer.Email` in OrderProcessor.GetCustomerEmail
///  on POST /orders (9 occurrences). The customer lookup returned null."
/// </summary>
internal static class SuggestionBuilder
{
    public static string Build(ErrorGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var exception = ShortExceptionName(group.ExceptionType);
        var location = FormatLocation(group);
        var rootCause = InferRootCause(group);
        var occurrences = group.Count == 1
            ? "1 occurrence"
            : string.Create(CultureInfo.InvariantCulture, $"{group.Count} occurrences");

        var sb = new StringBuilder();
        sb.Append(exception);
        if (!string.IsNullOrEmpty(rootCause))
        {
            sb.Append(' ').Append(rootCause);
        }
        if (!string.IsNullOrEmpty(location))
        {
            sb.Append(' ').Append(location);
        }
        sb.Append(" (").Append(occurrences).Append(").");
        return sb.ToString();
    }

    private static string ShortExceptionName(string? exceptionType)
    {
        if (string.IsNullOrWhiteSpace(exceptionType))
        {
            return "Unclassified failure";
        }
        var parts = exceptionType.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? exceptionType : parts[^1];
    }

    private static string FormatLocation(ErrorGroup g)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(g.StackSymbol))
        {
            sb.Append("in `").Append(g.StackSymbol).Append('`');
        }
        if (!string.IsNullOrWhiteSpace(g.Endpoint))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append("on ").Append(g.Endpoint);
            if (g.HttpStatus is int status)
            {
                sb.Append(" → ").Append(status.ToString(CultureInfo.InvariantCulture));
            }
        }
        else if (g.HttpStatus is int status)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append("→ HTTP ").Append(status.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    // Pulls the most informative noun out of the exception message:
    //   * For NRE: the property/field/variable mentioned in the message, e.g. `Customer.Email`.
    //   * For timeouts: the upstream id from "for id=..." or just "after Nms".
    //   * For validation: the field name mentioned in the message.
    //   * Otherwise: a one-sentence echo of the message, trimmed.
    private static string InferRootCause(ErrorGroup g)
    {
        var exception = g.ExceptionType ?? string.Empty;
        var message = g.Message ?? string.Empty;

        if (exception.Contains("NullReference", StringComparison.OrdinalIgnoreCase))
        {
            var field = ExtractFirstSymbol(message);
            return field is not null
                ? $"dereferenced `{field}` after a lookup returned null."
                : "dereferenced a null value before checking it.";
        }

        if (exception.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            || exception.Contains("Canceled", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            var subject = ExtractAfter(message, "for ") ?? ExtractAfter(message, "talking to ");
            return subject is not null
                ? $"timeout — deadline exceeded {subject}."
                : "timeout — downstream call exceeded its deadline.";
        }

        if (exception.Contains("ArgumentOutOfRange", StringComparison.OrdinalIgnoreCase)
            || exception.Contains("Argument", StringComparison.OrdinalIgnoreCase))
        {
            var arg = ExtractAfter(message, "Parameter '") ?? ExtractAfter(message, "parameter '");
            return arg is not null
                ? $"caller passed an invalid value for `{arg.TrimEnd('\'')}`."
                : "caller passed an invalid argument.";
        }

        if (message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
        {
            return "deadlock — two transactions blocked on opposite lock orders.";
        }

        if (exception.Contains("Validation", StringComparison.OrdinalIgnoreCase)
            || g.HttpStatus is 400 or 422)
        {
            var field = ExtractFirstSymbol(message);
            return field is not null
                ? $"validation — request payload field `{field}` did not satisfy the contract."
                : "validation — request payload did not satisfy the contract.";
        }

        if (g.HttpStatus is >= 500 and <= 599)
        {
            return "server-side 5xx — unhandled exception bubbled out of the endpoint.";
        }

        return string.Empty;
    }

    private static readonly Regex SymbolRegex = new(
        @"`(?<sym>[A-Za-z_][\w\.]+)`|(?<sym>[A-Z][A-Za-z0-9]+(?:\.[A-Z][A-Za-z0-9]+)+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? ExtractFirstSymbol(string message)
    {
        var m = SymbolRegex.Match(message);
        return m.Success ? m.Groups["sym"].Value : null;
    }

    private static readonly char[] SymbolStopChars = ['.', ',', ';', '\n', '\r'];

    private static string? ExtractAfter(string haystack, string marker)
    {
        var idx = haystack.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }
        var start = idx + marker.Length;
        var end = haystack.IndexOfAny(SymbolStopChars, start);
        var slice = end < 0 ? haystack[start..] : haystack[start..end];
        return string.IsNullOrWhiteSpace(slice) ? null : slice.Trim();
    }
}
