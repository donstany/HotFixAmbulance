using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotFixAmbulance.GitInsights;

public static class GitInsightsServiceCollectionExtensions
{
    public static IServiceCollection AddHotFixGitInsights(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GitInsightsOptions>()
            .Bind(configuration.GetSection(GitInsightsOptions.SectionName));

        services.AddSingleton<IGitRepoCache, LibGit2SharpRepoCache>();
        services.AddSingleton<IGitHistoryReader, LibGit2SharpHistoryReader>();
        services.AddSingleton<FixHintBuilder>();
        services.AddSingleton<IFixHintSource>(sp => sp.GetRequiredService<FixHintBuilder>());
        return services;
    }
}
