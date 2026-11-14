using System.Net;
using System.Text;
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

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamAsync(request)) chunks.Add(chunk);

        Assert.Equal(new[] { "Hello ", "Ram" }, chunks);
        Assert.Equal("v1beta/models/test-model:streamGenerateContent?alt=sse", handler.RequestUri?.PathAndQuery.TrimStart('/'));
        Assert.Equal(apiKey, handler.ApiKey);
        Assert.DoesNotContain(apiKey, handler.RequestBody);
        Assert.Contains("ECE student; prefers derivations", handler.RequestBody);
        Assert.Contains("Explain sampling", handler.RequestBody);
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
