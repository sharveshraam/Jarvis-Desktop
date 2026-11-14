using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class LocalAgentToolExecutor(LocalCommandRouter router, IActionApproval? approval = null, DesktopFileSystem? files = null, ProjectWorkspaceTools? workspace = null, SafeUserFiles? userFiles = null) : IAgentToolExecutor
{
    public async Task<JsonElement> ExecuteAsync(AiFunctionCall call, CancellationToken cancellationToken = default)
    {
        try
        {
            if (call.Arguments.ValueKind != JsonValueKind.Object)
                return Result(false, "Tool arguments must be a JSON object.");

            if (call.Name is "list_project_files" or "search_project_code" or "read_project_file" or "write_project_file")
            {
                if (workspace is null) return Result(false, "Choose a project workspace in Settings first.");
                var response = call.Name switch
                {
                    "list_project_files" => await workspace.ListFilesAsync(cancellationToken),
                    "search_project_code" => await workspace.SearchAsync(RequiredString(call.Arguments, "query"), cancellationToken),
                    "read_project_file" => await workspace.ReadAsync(RequiredString(call.Arguments, "path"), cancellationToken),
                    "write_project_file" => await workspace.WriteAsync(RequiredString(call.Arguments, "path"), RequiredString(call.Arguments, "content"), cancellationToken),
                    _ => "Unsupported project tool."
                };
                return Result(!response.StartsWith("Canceled", StringComparison.Ordinal), response);
            }

            var result = call.Name switch
            {
                "get_current_time" => await router.TryRunAsync("what time is it?", cancellationToken),
                "get_current_date" => await router.TryRunAsync("what is the date?", cancellationToken),
                "get_system_memory" => await router.TryRunAsync("show RAM usage", cancellationToken),
                "find_latest_pdf" => await router.TryRunAsync("find my latest PDF", cancellationToken),
                "open_application" => await router.TryRunAsync($"open {RequiredString(call.Arguments, "application")}", cancellationToken),
                "create_desktop_folder" => await router.CreateDesktopFolderAsync(RequiredString(call.Arguments, "name"), cancellationToken),
                "create_desktop_text_file" => await router.CreateDesktopTextFileAsync(
                    RequiredString(call.Arguments, "name"), RequiredString(call.Arguments, "content"), cancellationToken),
                "read_desktop_text_file" => await ReadDesktopFileForCloudAsync(RequiredString(call.Arguments, "name"), cancellationToken),
                "search_user_files" => await SearchUserFilesForCloudAsync(RequiredString(call.Arguments, "query"), cancellationToken),
                "read_user_file" => await ReadUserFileForCloudAsync(RequiredString(call.Arguments, "path"), cancellationToken),
                "open_user_file" => await router.TryRunAsync($"open file containing {RequiredString(call.Arguments, "query")}", cancellationToken),
                "create_reminder" => await CreateReminderAsync(call.Arguments, cancellationToken),
                "list_reminders" => await router.TryRunAsync("show reminders", cancellationToken),
                _ => new LocalCommandResult(false, $"The tool '{call.Name}' is not available.")
            };
            return Result(result.Handled && result.Succeeded, result.Response ?? "The local tool returned no result.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            return Result(false, ex.Message);
        }
    }

    private async Task<LocalCommandResult> ReadDesktopFileForCloudAsync(string name, CancellationToken cancellationToken)
    {
        if (approval is null || files is null)
            return new(true, "File sharing with Gemini is unavailable in this session.", false);
        string target;
        try { target = files.GetDesktopFilePath(name); }
        catch (ArgumentException ex) { return new(true, ex.Message, false); }
        var approved = await approval.ConfirmAsync(
            "Send file contents to Gemini?",
            $"The contents of this file will be sent to Google Gemini to answer your request:\n{target}",
            cancellationToken);
        if (!approved) return new(true, "Canceled. The file was not read or shared.", false);
        return await router.ReadDesktopTextFileAsync(name, cancellationToken);
    }

    private async Task<LocalCommandResult> SearchUserFilesForCloudAsync(string query, CancellationToken cancellationToken)
    {
        if (approval is null) return new(true, "Sharing local file names with Gemini is unavailable in this session.", false);
        var search = await router.TryRunAsync($"find files containing {query}", cancellationToken);
        if (!search.Handled || !search.Succeeded) return search;
        var preview = search.Response ?? "No matching files.";
        if (preview.Length > 2_000) preview = preview[..2_000] + "\n… preview truncated …";
        var approved = await approval.ConfirmAsync("Share matching file names with Gemini?",
            $"The search happens on this PC. If you approve, matching file names and paths will be sent to Google Gemini.\n\n{preview}", cancellationToken);
        return approved ? search : new(true, "Canceled. File names were not shared with Gemini.", false);
    }

    private async Task<LocalCommandResult> ReadUserFileForCloudAsync(string path, CancellationToken cancellationToken)
    {
        if (approval is null || userFiles is null)
            return new(true, "Sharing local file contents with Gemini is unavailable in this session.", false);
        UserFileSummary summary;
        try { summary = userFiles.Inspect(path); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException)
        { return new(true, ex.Message, false); }

        var approved = await approval.ConfirmAsync("Send local file contents to Gemini?",
            $"The contents of this file will be sent to Google Gemini.\nPath: {summary.FullPath}\nSize: {summary.Length:N0} bytes\nLast modified: {summary.LastModifiedUtc.ToLocalTime():g}\n\nContinue?", cancellationToken);
        if (!approved) return new(true, "Canceled. The file was not read or shared.", false);
        try
        {
            var content = await userFiles.ReadIfUnchangedAsync(summary, cancellationToken);
            return new(true, $"{summary.FullPath}\n{content}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException)
        { return new(true, ex.Message, false); }
    }

    private async Task<LocalCommandResult> CreateReminderAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("amount", out var amountValue) || !amountValue.TryGetInt32(out var amount) || amount <= 0)
            return new(true, "Reminder amount must be a positive integer.");
        var unit = RequiredString(arguments, "unit").ToLowerInvariant();
        if (unit is not ("minutes" or "hours" or "days")) return new(true, "Reminder unit must be minutes, hours, or days.");
        var title = RequiredString(arguments, "title");
        if (title.Length > 160 || title.Contains('\n') || title.Contains('\r')) return new(true, "Reminder text must be one line and at most 160 characters.");
        return await router.TryRunAsync($"remind me in {amount} {unit} to {title}", cancellationToken);
    }

    private static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"The '{name}' argument must be a string.");
        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException($"The '{name}' argument cannot be empty.");
        return text;
    }

    private static JsonElement Result(bool success, string message) => JsonSerializer.SerializeToElement(new { success, result = message });
}
