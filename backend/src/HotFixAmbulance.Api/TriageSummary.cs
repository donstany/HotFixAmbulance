namespace HotFixAmbulance.Api;

/// <summary>
/// Whole-run aggregates computed across ALL error groups. Lets the UI show metrics and a
/// total group count without downloading every group (which is now paginated).
/// </summary>
public sealed record TriageSummary(
    int TotalGroups,
    int TotalOccurrences,
    int Fatal,
    int Error,
    int Warning,
    int WithSuggestions,
    int WithFixes);
