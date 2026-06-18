using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// Maps a raw Serilog/Elasticsearch document (<see cref="JsonElement"/>) to a <see cref="LogEntry"/> per
/// the <c>serilog-mapping</c> skill. Returns <c>null</c> when required fields are missing or invalid.
/// </summary>
internal static class SerilogDocumentMapper
{
    public static LogEntry? TryMap(JsonElement doc)
    {
        if (doc.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetString(doc, "@timestamp", out var timestampRaw)
            || !DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            return null;
        }

        if (!TryGetString(doc, "level", out var levelRaw) || !Severities.TryParse(levelRaw, out var severity))
        {
            return null;
        }

        if (!TryGetNestedString(doc, "fields", "Application", out var apiName) || string.IsNullOrWhiteSpace(apiName))
        {
            return null;
        }

        var message = TryGetString(doc, "message", out var msg) ? msg : null;
        var exceptionType = TryGetNestedString(doc, "fields", "ExceptionType", out var exType) ? exType : null;
        var version = TryGetNestedString(doc, "fields", "Version", out var ver) ? ver : null;
        var method = TryGetNestedString(doc, "fields", "RequestMethod", out var rm) ? rm : null;
        var path = TryGetNestedString(doc, "fields", "RequestPath", out var rp) ? rp : null;
        var correlation = TryGetNestedString(doc, "fields", "CorrelationId", out var corr) ? corr : null;
        var httpStatus = TryGetNestedInt(doc, "fields", "StatusCode", out var sc) ? sc : (int?)null;

        // Stack frame can arrive in two shapes:
        //   1) Pre-extracted by the producer: fields.StackFile / fields.StackSymbol / fields.StackLine
        //   2) Raw ECS exception trace at the root: error.stack_trace -> parse the top user frame.
        var stackFile = TryGetNestedString(doc, "fields", "StackFile", out var sf) ? sf : null;
        var stackSymbol = TryGetNestedString(doc, "fields", "StackSymbol", out var ss) ? ss : null;
        var stackLine = TryGetNestedInt(doc, "fields", "StackLine", out var sl) ? sl : (int?)null;
        if (stackFile is null && stackSymbol is null && stackLine is null)
        {
            if (TryGetNestedString(doc, "error", "stack_trace", out var trace) && !string.IsNullOrEmpty(trace))
            {
                (stackFile, stackSymbol, stackLine) = ParseTopUserFrame(trace);
            }
        }

        return new LogEntry
        {
            TimestampUtc = timestamp,
            Severity = severity,
            ApiName = apiName!,
            ServiceVersion = version,
            ExceptionType = exceptionType,
            Message = message,
            RequestMethod = method,
            Endpoint = path,
            HttpStatus = httpStatus,
            CorrelationId = correlation,
            StackFile = stackFile,
            StackSymbol = stackSymbol,
            StackLine = stackLine,
        };
    }

    // Matches a single CLR stack frame line. Both legacy and modern formatter shapes are supported:
    //   legacy: "at DemoApi.OrderProcessor.GetCustomerEmail(String customerId) in C:\\repo\\demo-api\\BrokenServices.cs:line 53"
    //   modern: "at async Task<decimal> DemoApi.DatabaseFailureSimulator.CreatePricingRecordAsync(...) in .../DemoDatabase.cs:line 311"
    // The "async <ReturnType> " prefix is allowed via a non-greedy gap between `at` and the symbol.
    // The file path is captured non-greedily so Windows drive-letter paths ("C:\\repo\\...")
    // and POSIX paths both match.
    private static readonly Regex FrameRegex = new(
        @"^\s*at\s+.*?(?<sym>[\w]+(?:\.[\w<>`]+)+)\s*\([^)]*\)(?:\s+in\s+(?<file>.+?):line\s+(?<line>\d+))?",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static (string? File, string? Symbol, int? Line) ParseTopUserFrame(string trace)
    {
        foreach (Match m in FrameRegex.Matches(trace))
        {
            var sym = m.Groups["sym"].Value;
            // Skip framework frames so we surface user code.
            if (sym.StartsWith("System.", StringComparison.Ordinal)
                || sym.StartsWith("Microsoft.", StringComparison.Ordinal)
                || sym.StartsWith("Serilog.", StringComparison.Ordinal))
            {
                continue;
            }
            var file = m.Groups["file"].Success ? Path.GetFileName(m.Groups["file"].Value.Trim()) : null;
            var line = m.Groups["line"].Success && int.TryParse(m.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                ? l
                : (int?)null;
            // Collapse fully-qualified to "Type.Method" for the suggestion text.
            var lastTwo = string.Join('.', sym.Split('.').TakeLast(2));
            return (file, lastTwo, line);
        }
        return (null, null, null);
    }

    private static bool TryGetString(JsonElement doc, string property, out string? value)
    {
        value = null;
        if (doc.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return value is not null;
        }
        return false;
    }

    private static bool TryGetNestedString(JsonElement doc, string parent, string child, out string? value)
    {
        value = null;
        if (doc.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(child, out var c) && c.ValueKind == JsonValueKind.String)
        {
            value = c.GetString();
            return value is not null;
        }
        return false;
    }

    private static bool TryGetNestedInt(JsonElement doc, string parent, string child, out int value)
    {
        value = 0;
        if (doc.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(child, out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var v))
        {
            value = v;
            return true;
        }
        return false;
    }
}
