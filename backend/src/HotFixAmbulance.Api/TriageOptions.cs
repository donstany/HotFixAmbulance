using System.ComponentModel.DataAnnotations;

namespace HotFixAmbulance.Api;

/// <summary>
/// Bound from <c>Triage:*</c> configuration. Controls the default lookback window applied
/// when the API client (or UI) does not specify one, and the hard cap on any user-supplied
/// absolute window (passed as <c>maxSpan</c> to <c>TimeWindow.Relative/Absolute</c>).
/// </summary>
public sealed class TriageOptions
{
    /// <summary>Configuration section. Combined with the host's <c>HFA_</c> prefix this matches <c>HFA_TRIAGE__*</c>.</summary>
    public const string SectionName = "Triage";

    /// <summary>Lookback (in hours) used when no <c>?lookbackHours</c> or <c>?fromUtc/toUtc</c> is supplied.</summary>
    [Range(1, 24 * 30)]
    public int DefaultLookbackHours { get; set; } = 24;

    /// <summary>Hard cap on the analysis window span (in days). Requests above this return 400.</summary>
    [Range(1, 365)]
    public int MaxRangeDays { get; set; } = 30;
}
