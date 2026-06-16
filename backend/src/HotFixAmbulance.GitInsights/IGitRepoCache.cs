namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Ensures the local clone of a repository exists and is up-to-date with the configured branch on
/// <c>origin</c>. Returns the on-disk path of the working copy.
/// </summary>
public interface IGitRepoCache
{
    Task<string> EnsureUpToDateAsync(ApiRepoEntry entry, CancellationToken cancellationToken = default);
}
