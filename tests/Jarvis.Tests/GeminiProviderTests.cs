using System.Net;
using System.Text;
using System.Text.Json;
using Jarvis.AI;
using Jarvis.Core;
using Xunit;

namespace Jarvis.Tests;

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task StreamsTextAndSendsProfileWithoutPuttingKeyInRequestBody()
    {
        const string apiKey = "synthetic-test-secret";
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hello \"}]}}]}\n\n" +
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Ram\"}]}}]}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var provider = new GeminiProvider(http, new FakeSecretStore(apiKey), "test-model");
        var request = new ChatRequest([], "Explain sampling", JarvisMode.Study, new UserProfile("Ram", "ECE student; prefers derivations."));

        var events = new List<AiStreamEvent>();
        await foreach (var item in provider.StreamAsync(request)) events.Add(item);

        Assert.Equal(new[] { "Hello ", "Ram" }, events.Select(item => item.Text));
        Assert.Equal("v1beta/models/test-model:streamGenerateContent?alt=sse", handler.RequestUri?.PathAndQuery.TrimStart('/'));
        Assert.Equal(apiKey, handler.ApiKey);
        Assert.DoesNotContain(apiKey, handler.RequestBody);
        Assert.Contains("ECE student; prefers derivations", handler.RequestBody);
        Assert.Contains("Explain sampling", handler.RequestBody);
        Assert.Contains("functionDeclarations", handler.RequestBody);
    }

    [Fact]
    public async Task PreservesFunctionCallAndReturnsToolResponseInNextRequest()
    {
        const string callId = "call-7";
        var callHandler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"functionCall\":{\"name\":\"get_system_memory\",\"args\":{},\"id\":\"call-7\"},\"thoughtSignature\":\"sig-123\"}]}}]}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        using var callHttp = new HttpClient(callHandler) { BaseAddress = new Uri("https://example.test/") };
        var request = new ChatRequest([], "How much RAM is free?", JarvisMode.System);
        var callEvents = new List<AiStreamEvent>();
        await foreach (var item in new GeminiProvider(callHttp, new FakeSecretStore("key"), "test-model").StreamAsync(request)) callEvents.Add(item);
        var call = Assert.IsType<AiFunctionCall>(Assert.Single(callEvents).FunctionCall);
        Assert.Equal("get_system_memory", call.Name);
        Assert.Equal(callId, call.Id);
        Assert.Equal("sig-123", call.ModelContent.GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());

        var responseHandler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"There are 8 GB available.\"}]}}]}\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });
        using var responseHttp = new HttpClient(responseHandler) { BaseAddress = new Uri("https://example.test/") };
        var toolResponse = JsonSerializer.SerializeToElement(new { success = true, result = "8 GB available" });
        var followup = request with { ToolHistory = [new AiToolRound(call.ModelContent, [new AiToolResult(call.Name, toolResponse, call.Id)])] };
        var finalEvents = new List<AiStreamEvent>();
        await foreach (var item in new GeminiProvider(responseHttp, new FakeSecretStore("key"), "test-model").StreamAsync(followup)) finalEvents.Add(item);

        using var body = JsonDocument.Parse(responseHandler.RequestBody);
        var contents = body.RootElement.GetProperty("contents");
        Assert.Equal("sig-123", contents[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        var functionResponse = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal(callId, functionResponse.GetProperty("id").GetString());
        Assert.Equal("get_system_memory", functionResponse.GetProperty("name").GetString());
        Assert.Equal("There are 8 GB available.", string.Concat(finalEvents.Select(item => item.Text)));
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            return response;
        }
    }

    private sealed class FakeSecretStore(string value) : ISecretStore
    {
        public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<string?>(value);
        public Task SetAsync(string name, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
