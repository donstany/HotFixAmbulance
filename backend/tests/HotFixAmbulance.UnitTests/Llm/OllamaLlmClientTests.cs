using System.Net;
using System.Text;
using FluentAssertions;
using HotFixAmbulance.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace HotFixAmbulance.UnitTests.Llm;

public sealed class OllamaLlmClientTests
{
    private static OllamaLlmClient BuildClient(StubHandler handler, LlmOptions? options = null)
    {
        var http = new HttpClient(handler);
        var opts = options ?? new LlmOptions
        {
            Provider = "Ollama",
            Endpoint = "http://localhost:11434",
            Model = "llama3.1",
            TimeoutSeconds = 30,
        };
        return new OllamaLlmClient(http, Options.Create(opts));
    }

    [Fact]
    public async Task CompleteJsonAsync_posts_chat_request_and_returns_message_content()
    {
        const string content = "{\"suggestion\":\"a null lookup\",\"howToFix\":\"guard the lookup\"}";
        var responseBody = "{\"model\":\"llama3.1\",\"message\":{\"role\":\"assistant\",\"content\":"
            + System.Text.Json.JsonSerializer.Serialize(content) + "},\"done\":true}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        });
        var sut = BuildClient(handler);

        var result = await sut.CompleteJsonAsync("SYS-PROMPT", "USER-PROMPT", CancellationToken.None);

        result.Should().Be(content, "the assistant message content is the model's JSON answer");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should().Be("http://localhost:11434/api/chat");
        handler.LastBody.Should().Contain("llama3.1");
        handler.LastBody.Should().Contain("SYS-PROMPT");
        handler.LastBody.Should().Contain("USER-PROMPT");
        handler.LastBody.Should().Contain("\"stream\":false", "non-streaming responses are easier to parse");
        handler.LastBody.Should().Contain("\"format\":\"json\"", "Ollama is asked to constrain output to JSON");
    }

    [Fact]
    public async Task CompleteJsonAsync_returns_null_on_non_success_status()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = BuildClient(handler);

        var result = await sut.CompleteJsonAsync("s", "u", CancellationToken.None);

        result.Should().BeNull("a 5xx from the model server must degrade gracefully, not throw");
    }

    [Fact]
    public async Task CompleteJsonAsync_returns_null_when_transport_throws()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = BuildClient(handler);

        var result = await sut.CompleteJsonAsync("s", "u", CancellationToken.None);

        result.Should().BeNull("an unreachable Ollama endpoint must fall back, not crash the triage run");
    }

    [Fact]
    public async Task CompleteJsonAsync_returns_null_on_malformed_response_body()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        });
        var sut = BuildClient(handler);

        var result = await sut.CompleteJsonAsync("s", "u", CancellationToken.None);

        result.Should().BeNull("an unparseable body is treated as a failure");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return responder(request);
        }
    }
}
