using FluentAssertions;
using HotFixAmbulance.Api;
using HotFixAmbulance.GitInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotFixAmbulance.UnitTests.Api;

public sealed class GroupEnrichmentServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(string? strategy)
    {
        var settings = new Dictionary<string, string?>();
        if (strategy is not null)
        {
            settings["Analysis:Strategy"] = strategy;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        // The enrichers depend on the git hint source; the LLM path also needs ILlmClient + prompt
        // builder, both registered by AddHotFixGroupEnrichment via AddHotFixLlm.
        services.AddSingleton(Substitute.For<IFixHintSource>());
        services.AddHotFixGroupEnrichment(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Llm_strategy_resolves_the_LLM_enricher()
    {
        using var sp = BuildProvider("Llm");
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGroupEnricher>().Should().BeOfType<LlmGroupEnricher>();
    }

    [Fact]
    public void Llm_strategy_is_case_insensitive()
    {
        using var sp = BuildProvider("llm");
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGroupEnricher>().Should().BeOfType<LlmGroupEnricher>();
    }

    [Fact]
    public void Default_and_heuristic_resolve_the_git_enricher()
    {
        using (var sp = BuildProvider(strategy: null))
        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IGroupEnricher>().Should().BeOfType<GitFixHintEnricher>();
        }

        using (var sp = BuildProvider("Heuristic"))
        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IGroupEnricher>().Should().BeOfType<GitFixHintEnricher>();
        }
    }
}
