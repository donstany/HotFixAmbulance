using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotFixAmbulance.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LlmOptions"/> (bound from the <c>Llm</c> section), the
    /// <see cref="LlmPromptBuilder"/>, and a typed <see cref="System.Net.Http.HttpClient"/>-backed
    /// <see cref="OllamaLlmClient"/> as <see cref="ILlmClient"/>. Safe to call regardless of the
    /// active analysis strategy — the client is only resolved when the LLM strategy is selected.
    /// </summary>
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Standard DI extension; null-check guards added.")]
    public static IServiceCollection AddHotFixLlm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<LlmPromptBuilder>();
        services.AddHttpClient<ILlmClient, OllamaLlmClient>();

        return services;
    }
}
