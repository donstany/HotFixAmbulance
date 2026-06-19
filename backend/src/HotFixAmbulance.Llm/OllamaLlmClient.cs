using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HotFixAmbulance.Llm;

/// <summary>
/// <see cref="ILlmClient"/> backed by a local Ollama daemon's <c>POST /api/chat</c> endpoint
/// (non-streaming, <c>format: "json"</c>). Honours <see cref="LlmOptions.TimeoutSeconds"/> via a
/// linked cancellation token and converts every transport/timeout/parse failure into a <c>null</c>
/// result so the triage pipeline degrades to its heuristic columns instead of failing.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public OllamaLlmClient(HttpClient http, IOptions<LlmOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
    }

    public async Task<string?> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(new Uri(_options.Endpoint, UriKind.Absolute), "/api/chat");
        var payload = new ChatRequest(
            _options.Model,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
            Stream: false,
            Format: "json",
            Options: new RequestOptions(Temperature: 0.2));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var response = await _http
                .PostAsJsonAsync(requestUri, payload, SerializerOptions, cts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var parsed = await response.Content
                .ReadFromJsonAsync<ChatResponse>(SerializerOptions, cts.Token)
                .ConfigureAwait(false);
            var content = parsed?.Message?.Content;
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired (not the caller's token) — degrade gracefully.
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("options")] RequestOptions Options);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record RequestOptions(
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("done")] bool Done);
}
