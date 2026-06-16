namespace HotFixAmbulance.Core;

/// <summary>
/// Severity levels supported by HotFixAmbulance. Ordering: <see cref="Warning"/> &lt; <see cref="Error"/> &lt; <see cref="Fatal"/>.
/// Any other Serilog level (Information, Debug, Verbose) is intentionally ignored at ingestion time.
/// </summary>
public enum Severity
{
    Warning = 1,
    Error = 2,
    Fatal = 3,
}

public static class Severities
{
    public static bool TryParse(string? value, out Severity severity)
    {
        severity = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "fatal":
            case "critical":
                severity = Severity.Fatal;
                return true;
            case "error":
            case "err":
                severity = Severity.Error;
                return true;
            case "warning":
            case "warn":
                severity = Severity.Warning;
                return true;
            default:
                return false;
        }
    }
}
