using System.Diagnostics.CodeAnalysis;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HotFixAmbulance.Elastic;

public static class ElasticServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ElasticOptions"/>, the v8 <see cref="ElasticsearchClient"/>, and the
    /// <see cref="ElasticLogIngestor"/> backed by <see cref="ElasticsearchLogSource"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Standard DI extension; null-check guards added.")]
    public static IServiceCollection AddHotFixElastic(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ElasticOptions>()
            .Bind(configuration.GetSection(ElasticOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
            var settings = new ElasticsearchClientSettings(opts.Uri)
                .DefaultIndex(opts.IndexPattern);

            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                settings = settings.Authentication(new ApiKey(opts.ApiKey));
            }
            else if (!string.IsNullOrWhiteSpace(opts.Username))
            {
                settings = settings.Authentication(new BasicAuthentication(opts.Username!, opts.Password ?? string.Empty));
            }

            return new ElasticsearchClient(settings);
        });

        services.AddSingleton<IElasticLogSource, ElasticsearchLogSource>();
        services.AddSingleton<ElasticLogIngestor>();
        return services;
    }
}
