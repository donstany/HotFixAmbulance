using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Clones the repo into <c>%LOCALAPPDATA%/HotFixAmbulance/repos/{apiName}</c> on first use, then
/// fetches and hard-resets the working copy to <c>origin/{branch}</c> on subsequent calls.
/// </summary>
public sealed partial class LibGit2SharpRepoCache : IGitRepoCache
{
    private readonly GitInsightsOptions _options;
    private readonly ILogger<LibGit2SharpRepoCache> _logger;

    public LibGit2SharpRepoCache(IOptions<GitInsightsOptions> options, ILogger<LibGit2SharpRepoCache> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> EnsureUpToDateAsync(ApiRepoEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var root = _options.ResolvedCacheRoot();
        Directory.CreateDirectory(root);
        var repoPath = Path.Combine(root, entry.Name);

        if (!Repository.IsValid(repoPath))
        {
            LogClone(_logger, entry.Url, repoPath);
            Repository.Clone(entry.Url.ToString(), repoPath, new CloneOptions
            {
                BranchName = entry.Branch,
                FetchOptions =
                {
                    CredentialsProvider = BuildCredentials(),
                },
            });
        }
        else
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(r => r.Specification).ToArray();
            Commands.Fetch(repo, "origin", refSpecs, new FetchOptions
            {
                CredentialsProvider = BuildCredentials(),
            }, logMessage: $"hfa fetch {entry.Name}");

            var remoteBranch = repo.Branches[$"origin/{entry.Branch}"]
                ?? throw new InvalidOperationException($"Remote branch origin/{entry.Branch} not found.");
            repo.Reset(ResetMode.Hard, remoteBranch.Tip);
            LogFetch(_logger, entry.Name, remoteBranch.Tip.Sha[..7]);
        }

        return Task.FromResult(repoPath);
    }

    private CredentialsHandler BuildCredentials() =>
        (_, _, _) => string.IsNullOrWhiteSpace(_options.AuthToken)
            ? new DefaultCredentials()
            : new UsernamePasswordCredentials { Username = "x-access-token", Password = _options.AuthToken };

    [LoggerMessage(EventId = 1, Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cloning {Url} into {Path}")]
    private static partial void LogClone(ILogger logger, Uri url, string path);

    [LoggerMessage(EventId = 2, Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Fetched {Api}, now at {Sha}")]
    private static partial void LogFetch(ILogger logger, string api, string sha);
}
