using System.IO;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core;

namespace Jarvis.AI;

public sealed class GeminiProvider(HttpClient http, ISecretStore secrets, string model = "gemini-3.8-flash", IUserPreferencesStore? preferences = null) : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var key = await secrets.GetAsync("gemini-api-key", cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Add a Gemini API key in Settings to use cloud chat. Local commands still work without it.");
        var selectedModel = preferences is null ? model : (await preferences.GetAsync(cancellationToken)).GeminiModel;

        var contents = request.History
            .TakeLast(12)
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => (object)new GeminiContent(
                message.Role == ChatRole.User ? "user" : "model",
                [new GeminiPart(message.Content)]))
            .ToList();
        contents.Add(new GeminiContent("user", [new GeminiPart(request.UserText)]));
        foreach (var round in request.ToolHistory ?? [])
        {
            contents.Add(round.ModelContent);
            contents.Add(new GeminiContent("user", round.Results
                .Select(result => new GeminiPart(FunctionResponse: new GeminiFunctionResponse(result.Name, result.Response, result.Id)))
                .ToArray()));
        }

        var payload = new GeminiRequest(
            contents,
            new GeminiSystemInstruction([new GeminiPart(SystemPrompt.For(request.Mode, request.UserProfile))]),
            new GeminiGenerationConfig(Temperature: 0.4, MaxOutputTokens: 2048),
            [new GeminiTool(FunctionDeclarations())]);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(selectedModel)}:streamGenerateContent?alt=sse");
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
        var anyOutput = false;
        var functionCalls = new Dictionary<string, AiFunctionCall>(StringComparer.Ordinal);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]") continue;
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)) continue;
            foreach (var candidate in candidates.EnumerateArray().Take(1))
            {
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) continue;
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textValue))
                    {
                        var chunk = textValue.GetString();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            anyOutput = true;
                            yield return new AiStreamEvent(Text: chunk);
                        }
                    }
                    if (part.TryGetProperty("functionCall", out var functionCall) && functionCall.TryGetProperty("name", out var nameValue))
                    {
                        var name = nameValue.GetString();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var arguments = functionCall.TryGetProperty("args", out var args) ? args.Clone() : EmptyObject();
                        anyOutput = true;
                        var id = functionCall.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                        functionCalls[id ?? name] = new AiFunctionCall(name, arguments, content.Clone(), id);
                    }
                }
            }
        }
        foreach (var functionCall in functionCalls.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AiStreamEvent(FunctionCall: functionCall);
        }
        if (!anyOutput)
            throw new InvalidDataException("Gemini returned no text or tool call. Try a shorter request or check the selected model.");
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<object> Contents,
        [property: JsonPropertyName("systemInstruction")] GeminiSystemInstruction SystemInstruction,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig,
        [property: JsonPropertyName("tools")] IReadOnlyList<GeminiTool> Tools);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("functionResponse")] GeminiFunctionResponse? FunctionResponse = null);
    private sealed record GeminiFunctionResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("response")] JsonElement Response,
        [property: JsonPropertyName("id")] string? Id = null);
    private sealed record GeminiTool([property: JsonPropertyName("functionDeclarations")] JsonElement FunctionDeclarations);
    private sealed record GeminiSystemInstruction([property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    private static JsonElement EmptyObject() => ParseJsonElement("{}");

    private static JsonElement FunctionDeclarations() => ParseJsonElement("""
        [
          {"name":"get_current_time","description":"Get the current local Windows time.","parameters":{"type":"OBJECT","properties":{}}},
          {"name":"get_current_date","description":"Get today's local date.","parameters":{"type":"OBJECT","properties":{}}},
          {"name":"get_system_memory","description":"Read total and currently available physical memory.","parameters":{"type":"OBJECT","properties":{}}},
          {"name":"find_latest_pdf","description":"Find the most recently modified PDF in Desktop, Documents, or Downloads.","parameters":{"type":"OBJECT","properties":{}}},
          {"name":"open_application","description":"Open an application from JARVIS's limited allowlist.","parameters":{"type":"OBJECT","properties":{"application":{"type":"STRING","description":"Application name such as Notepad, Calculator, VS Code, or Visual Studio."}},"required":["application"]}},
          {"name":"create_desktop_folder","description":"Create a new folder directly on the Desktop after asking the user to confirm.","parameters":{"type":"OBJECT","properties":{"name":{"type":"STRING","description":"Single folder name, without a path."}},"required":["name"]}},
          {"name":"create_desktop_text_file","description":"Create a new .txt or .md file directly on the Desktop after user confirmation.","parameters":{"type":"OBJECT","properties":{"name":{"type":"STRING","description":"File name ending in .txt or .md."},"content":{"type":"STRING","description":"Full text content to write."}},"required":["name","content"]}},
          {"name":"read_desktop_text_file","description":"Read a supported text file up to 1 MB directly on the Desktop.","parameters":{"type":"OBJECT","properties":{"name":{"type":"STRING","description":"File name, not a path."}},"required":["name"]}},
          {"name":"create_reminder","description":"Create a local reminder in the next year.","parameters":{"type":"OBJECT","properties":{"amount":{"type":"INTEGER","description":"Positive delay amount."},"unit":{"type":"STRING","enum":["minutes","hours","days"]},"title":{"type":"STRING","description":"Reminder text up to 160 characters."}},"required":["amount","unit","title"]}},
          {"name":"list_reminders","description":"List upcoming local reminders.","parameters":{"type":"OBJECT","properties":{}}}
          ,{"name":"search_user_files","description":"Search filenames in Desktop, Documents, and Downloads. Ask the user before sharing matching names and paths with Gemini.","parameters":{"type":"OBJECT","properties":{"query":{"type":"STRING","description":"Filename fragment; not a path or wildcard."}},"required":["query"]}}
          ,{"name":"read_user_file","description":"Read a supported text file returned by local search, up to 1 MB. Always ask the user before sharing file contents with Gemini.","parameters":{"type":"OBJECT","properties":{"path":{"type":"STRING","description":"Full path returned by search_user_files."}},"required":["path"]}}
          ,{"name":"list_project_files","description":"List source files in the configured project workspace. Ask before sharing names with Gemini.","parameters":{"type":"OBJECT","properties":{}}}
          ,{"name":"search_project_code","description":"Search source code in the configured project workspace. Ask before sharing matching snippets with Gemini.","parameters":{"type":"OBJECT","properties":{"query":{"type":"STRING"}},"required":["query"]}}
          ,{"name":"read_project_file","description":"Read a supported source file from the configured workspace after explicit user approval to send its contents to Gemini.","parameters":{"type":"OBJECT","properties":{"path":{"type":"STRING"}},"required":["path"]}}
          ,{"name":"write_project_file","description":"Create or replace a source file inside the configured workspace. Always show the change preview and ask for approval.","parameters":{"type":"OBJECT","properties":{"path":{"type":"STRING"},"content":{"type":"STRING"}},"required":["path","content"]}}
        ]
        """);

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

}

internal static class SystemPrompt
{
    public static string For(JarvisMode mode, UserProfile? profile = null)
    {
        var modeGuidance = mode switch
        {
            JarvisMode.Personal => "Personal mode: act as a practical day-to-day assistant. Help organize the user's plans and preferences, and ask a short clarifying question when a personal detail is uncertain.",
            JarvisMode.Study => "Study mode: teach for understanding. Build from fundamentals, explain terminology and assumptions, show derivations or step-by-step reasoning where useful, and finish with an example or a check for understanding.",
            JarvisMode.Development => "Development mode: work like a careful software engineering partner. Inspect the available project context before proposing changes, explain tradeoffs, preserve existing behavior, and recommend reproducible tests and verification.",
            JarvisMode.Research => "Research mode: distinguish verified facts from inference, state uncertainty and date sensitivity, and never invent sources or claim live research when no browsing tool has been used.",
            JarvisMode.Focus => "Focus mode: keep replies brief and actionable. Prioritize one next action, avoid tangents, and break larger work into small steps only when needed.",
            JarvisMode.System => "System mode: prioritize accurate local Windows status and troubleshooting. Use available local tools for measurements, explain unavailable readings, and obtain approval before changing files or settings.",
            JarvisMode.Creator => "Creator mode: help explore multiple creative directions, then shape the selected idea into a practical outline or draft while preserving the user's intent.",
            _ => "Personal mode: be a practical, clear, and honest assistant."
        };
        var personalContext = profile is null || (string.IsNullOrWhiteSpace(profile.DisplayName) && string.IsNullOrWhiteSpace(profile.PersonalContext))
            ? string.Empty
            : $"The user prefers to be called {profile.DisplayName}. Personal context and preferences (editable by the user; do not assume other facts): {profile.PersonalContext}";
        return $"""
            You are JARVIS, a concise and capable Windows assistant. The user's current mode is {mode}.
            Be direct and honest. Never claim that you performed a computer action unless the local application confirms it.
            You can use only the declared JARVIS tools for local actions. The application validates every argument and asks before changing files. Never claim an action succeeded until a tool result confirms it. Never invent tools or attempt shell commands.
            Keep answers focused; use clear technical explanations when the user asks to learn.
            {modeGuidance}
            {personalContext}
            """;
    }
}
