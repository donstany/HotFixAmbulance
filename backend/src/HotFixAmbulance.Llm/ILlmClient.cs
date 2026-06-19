namespace HotFixAmbulance.Llm;

/// <summary>
/// Minimal seam over a chat-style LLM. Implementations send the supplied prompts to a model and
/// return the assistant's raw text response — expected to be a JSON object because callers ask the
/// model to constrain its output. Implementations must NEVER throw for transport/timeout/parse
/// failures: they return <c>null</c> so the caller can fall back to the heuristic columns.
/// </summary>
public interface ILlmClient
{
    Task<string?> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
