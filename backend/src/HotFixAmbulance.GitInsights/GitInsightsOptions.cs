namespace HotFixAmbulance.GitInsights;

public sealed class GitInsightsOptions
{
    public const string SectionName = "Git";

    /// <summary>Directory under which per-API working copies live. Defaults to <c>%LOCALAPPDATA%/HotFixAmbulance/repos</c>.</summary>
    public string? CacheRoot { get; set; }

    /// <summary>Optional bearer token forwarded as HTTP Basic password for git clone/fetch over HTTPS.</summary>
    public string? AuthToken { get; set; }

    public string ResolvedCacheRoot() =>
        CacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotFixAmbulance",
            "repos");
}
