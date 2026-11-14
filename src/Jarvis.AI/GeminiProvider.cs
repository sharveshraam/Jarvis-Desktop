using System.IO;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core;

namespace Jarvis.AI;

public sealed class GeminiProvider(HttpClient http, ISecretStore secrets, string model = "gemini-3.8-flash") : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var key = await secrets.GetAsync("gemini-api-key", cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Add a Gemini API key in Settings to use cloud chat. Local commands still work without it.");

        var contents = request.History
            .TakeLast(12)
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new GeminiContent(
                message.Role == ChatRole.User ? "user" : "model",
                [new GeminiPart(message.Content)]))
            .Append(new GeminiContent("user", [new GeminiPart(request.UserText)]))
            .ToArray();

        var payload = new GeminiRequest(
            contents,
            new GeminiSystemInstruction([new GeminiPart(SystemPrompt.For(request.Mode))]),
            new GeminiGenerationConfig(Temperature: 0.4, MaxOutputTokens: 2048));

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        message.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var friendly = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Gemini rejected the API key. Check it in Settings.",
                HttpStatusCode.TooManyRequests => "Gemini is rate-limiting requests. Wait a moment and try again.",
                _ => $"Gemini request failed ({(int)response.StatusCode}). Check the connection and model settings."
            };
            throw new HttpRequestException(friendly, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var anyText = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]") continue;
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)) continue;
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) continue;
                foreach (var part in parts.EnumerateArray())
                {
                    if (!part.TryGetProperty("text", out var textValue)) continue;
                    var chunk = textValue.GetString();
                    if (string.IsNullOrEmpty(chunk)) continue;
                    anyText = true;
                    yield return chunk;
                }
            }
        }
        if (!anyText)
            throw new InvalidDataException("Gemini returned no text. Try a shorter request or check the selected model.");
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("systemInstruction")] GeminiSystemInstruction SystemInstruction,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);
    private sealed record GeminiSystemInstruction([property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

}

internal static class SystemPrompt
{
    public static string For(JarvisMode mode) => $"""
        You are JARVIS, a concise and capable Windows assistant. The user's current mode is {mode}.
        Be direct and honest. Never claim that you performed a computer action unless the local application confirms it.
        You cannot run arbitrary commands, browse private files, or take actions yourself. For any such request, explain the local capability or ask for the needed approval.
        Keep answers focused; use clear technical explanations when the user asks to learn.
        """;
}
