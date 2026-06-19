using System.ComponentModel.DataAnnotations;

namespace HotFixAmbulance.Llm;

/// <summary>
/// Configuration for the LLM column-enrichment feature, bound from the <c>Llm</c> configuration
/// section (env overrides use the <c>HFA_Llm__*</c> prefix). Defaults target a local Ollama daemon
/// so the demo is self-contained and needs no secrets.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Adapter to use. Currently only <c>Ollama</c> is wired; reserved for OpenAI/Anthropic.</summary>
    [Required]
    public string Provider { get; set; } = "Ollama";

    /// <summary>Base address of the model server, e.g. <c>http://localhost:11434</c> for Ollama.</summary>
    [Required]
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Model tag to request, e.g. <c>llama3.1</c>.</summary>
    [Required]
    public string Model { get; set; } = "llama3.1";

    /// <summary>Per-call deadline in seconds. On expiry the call returns null and triage falls back.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Optional API key for hosted providers. Unused by Ollama; reserved for future adapters.</summary>
    public string? ApiKey { get; set; }
}
