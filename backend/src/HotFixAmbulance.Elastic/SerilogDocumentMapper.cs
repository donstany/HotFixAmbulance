using System.Globalization;
using System.Text.Json;
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
        };
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
