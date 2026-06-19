using HotFixAmbulance.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotFixAmbulance.Api;

public static class GroupEnrichmentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the group-enrichment pipeline and selects the active <see cref="IGroupEnricher"/>
    /// from <c>Analysis:Strategy</c>: <c>Llm</c> wires <see cref="LlmGroupEnricher"/>, anything else
    /// (including absent) uses the deterministic <see cref="GitFixHintEnricher"/>. The git enricher
    /// is always registered because the LLM enricher falls back to it.
    /// </summary>
    public static IServiceCollection AddHotFixGroupEnrichment(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHotFixLlm(configuration);
        services.AddScoped<GitFixHintEnricher>();

        if (string.Equals(configuration["Analysis:Strategy"], AnalysisStrategyNames.Llm, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IGroupEnricher, LlmGroupEnricher>();
        }
        else
        {
            services.AddScoped<IGroupEnricher>(sp => sp.GetRequiredService<GitFixHintEnricher>());
        }

        return services;
    }
}
