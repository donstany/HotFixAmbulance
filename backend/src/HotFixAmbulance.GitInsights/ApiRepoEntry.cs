namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Configuration for a single myPos API: where to clone, which branch to track.
/// </summary>
public sealed record ApiRepoEntry(string Name, Uri Url, string Branch);
