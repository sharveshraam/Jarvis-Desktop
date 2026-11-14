using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed record LocalCommandResult(bool Handled, string? Response = null, bool Succeeded = true);

public sealed partial class LocalCommandRouter
{
    private readonly IActionApproval? _approval;
    private readonly DesktopFileSystem? _files;
    private readonly ISystemInfoProvider? _systemInfo;
    private readonly ILocalFileSearch? _fileSearch;
    private readonly IApplicationLauncher? _applicationLauncher;
    private readonly IReminderStore? _reminders;
    private readonly IMemoryStore? _memory;
    private readonly IProjectTaskCoordinator? _projectTasks;
    private readonly IPerformanceTelemetry? _performance;

    public LocalCommandRouter(IActionApproval? approval = null, DesktopFileSystem? files = null, ISystemInfoProvider? systemInfo = null, ILocalFileSearch? fileSearch = null, IApplicationLauncher? applicationLauncher = null, IReminderStore? reminders = null, IMemoryStore? memory = null, IProjectTaskCoordinator? projectTasks = null, IPerformanceTelemetry? performance = null)
    {
        _approval = approval;
        _files = files;
        _systemInfo = systemInfo;
        _fileSearch = fileSearch;
        _applicationLauncher = applicationLauncher;
        _reminders = reminders;
        _memory = memory;
        _projectTasks = projectTasks;
        _performance = performance;
    }

    private static readonly IReadOnlyDictionary<string, string> Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = "notepad.exe",
        ["calculator"] = "calc.exe",
        ["calc"] = "calc.exe",
        ["paint"] = "mspaint.exe",
        ["file explorer"] = "explorer.exe",
        ["explorer"] = "explorer.exe",
        ["visual studio code"] = "code",
        ["vs code"] = "code",
        ["code"] = "code",
        ["visual studio"] = "devenv.exe"
    };

    public async Task<LocalCommandResult> TryRunAsync(string text, CancellationToken cancellationToken = default)
    {
        var normalized = text.Trim();
        var now = DateTimeOffset.Now;
        if (Regex.IsMatch(normalized, @"^(?:show|open|check)(?:\s+my)?\s+(?:jarvis )?(?:performance|diagnostics|diagnostic report)\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return await GetDiagnosticsAsync(cancellationToken);
        if (Regex.IsMatch(normalized, @"^(?:build|compile)(?:\s+my)?\s+project\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return await QueueProjectTaskAsync(JarvisBackgroundTaskKind.BuildProject, cancellationToken);
        if (Regex.IsMatch(normalized, @"^(?:test|run tests on|run the tests for)(?:\s+my)?\s+project\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return await QueueProjectTaskAsync(JarvisBackgroundTaskKind.TestProject, cancellationToken);
        if (Regex.IsMatch(normalized, @"^(?:show|list)(?:\s+my)?\s+(?:background )?project tasks\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_projectTasks is null) return new(true, "Background project tasks are unavailable.", false);
            var tasks = await _projectTasks.GetRecentAsync(10, cancellationToken);
            return new(true, tasks.Count == 0 ? "No project tasks have run yet." : string.Join(Environment.NewLine,
                tasks.Select(t => $"{t.Id} · {t.Kind} · {t.State} · {t.Stage}")));
        }
        var inspectTask = Regex.Match(normalized, @"^(?:show|inspect)(?:\s+the)?\s+task\s+([a-f0-9]{4,8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (inspectTask.Success)
        {
            if (_projectTasks is null) return new(true, "Background project tasks are unavailable.", false);
            var matches = (await _projectTasks.GetRecentAsync(100, cancellationToken))
                .Where(t => t.Id.StartsWith(inspectTask.Groups[1].Value, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1) return new(true, matches.Length == 0 ? "Task not found." : "That task ID is ambiguous; use more characters.", false);
            var task = matches[0];
            var details = $"{task.Id} · {task.Kind} · {task.State} · {task.Stage}\n{task.Result ?? "No output yet."}";
            return new(true, details.Length <= 5000 ? details : details[..5000]);
        }
        var cancelTask = Regex.Match(normalized, @"^cancel task\s+([a-f0-9]{4,8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (cancelTask.Success)
        {
            if (_projectTasks is null) return new(true, "Background project tasks are unavailable.", false);
            var canceled = await _projectTasks.CancelAsync(cancelTask.Groups[1].Value, cancellationToken);
            return new(true, canceled ? "Cancellation requested." : "That task is not active or could not be found.", canceled);
        }
        var remember = Regex.Match(normalized, @"^remember(?:\s+that)?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (remember.Success)
        {
            if (_memory is null) return new(true, "Local memory is unavailable.", false);
            var content = remember.Groups[1].Value.Trim().TrimEnd('.', '!');
            if (content.Length is 0 or > 2000) return new(true, "Memory text must be between 1 and 2,000 characters.", false);
            await _memory.AddAsync(content, cancellationToken);
            return new(true, "Saved to JARVIS memory on this computer.");
        }
        if (Regex.IsMatch(normalized, @"^(?:what do you remember(?: about me)?|show (?:my )?memories|list memories)\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_memory is null) return new(true, "Local memory is unavailable.", false);
            var memories = await _memory.SearchAsync(limit: 50, cancellationToken: cancellationToken);
            return new(true, memories.Count == 0 ? "I don't have any saved memories yet." : string.Join(Environment.NewLine, memories.Select(m => $"• {m.Content}")));
        }
        var forget = Regex.Match(normalized, @"^forget(?:\s+that)?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (forget.Success)
        {
            if (_memory is null) return new(true, "Local memory is unavailable.", false);
            var query = forget.Groups[1].Value.Trim().TrimEnd('.', '!');
            var matches = await _memory.SearchAsync(query, 20, cancellationToken);
            if (matches.Count == 0) return new(true, "No saved memory matched that detail.");
            if (_approval is null || !await _approval.ConfirmAsync("Forget saved memory?", string.Join(Environment.NewLine, matches.Select(m => m.Content)), cancellationToken))
                return new(true, "Canceled; saved memory was kept.", false);
            var removed = await _memory.DeleteMatchingAsync(query, cancellationToken);
            return new(true, $"Removed {removed} matching saved {(removed == 1 ? "memory" : "memories")} from this computer.");
        }
        var setReminder = ReminderPattern().Match(normalized);
        if (setReminder.Success)
        {
            if (_reminders is null) return new(true, "Reminders are unavailable in this session.", false);
            if (!int.TryParse(setReminder.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                return new(true, "Choose a reminder time greater than zero.", false);
            var duration = setReminder.Groups[2].Value.ToLowerInvariant() switch
            {
                "minute" or "minutes" => TimeSpan.FromMinutes(amount),
                "hour" or "hours" => TimeSpan.FromHours(amount),
                "day" or "days" => TimeSpan.FromDays(amount),
                _ => TimeSpan.Zero
            };
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(365))
                return new(true, "Choose a reminder within the next year.", false);
            var title = setReminder.Groups[3].Value.Trim().TrimEnd('.', '!');
            if (title.Length is 0 or > 160) return new(true, "Reminder text must be between 1 and 160 characters.", false);
            var reminder = new JarvisReminder(Guid.NewGuid().ToString("N"), title, DateTimeOffset.UtcNow.Add(duration), false);
            await _reminders.AddAsync(reminder, cancellationToken);
            return new(true, $"Reminder set for {reminder.DueAt.ToLocalTime():g}: {title}");
        }
        if (Regex.IsMatch(normalized, @"^(?:show|list)\s+(?:my\s+)?reminders\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_reminders is null) return new(true, "Reminders are unavailable in this session.", false);
            var upcoming = await _reminders.GetUpcomingAsync(cancellationToken: cancellationToken);
            return new(true, upcoming.Count == 0
                ? "You have no upcoming reminders."
                : string.Join(Environment.NewLine, upcoming.Select(item => $"[{item.Id[..Math.Min(8, item.Id.Length)]}] {item.DueAt.ToLocalTime():g} — {item.Title}")));
        }
        var cancelReminder = CancelReminderPattern().Match(normalized);
        if (cancelReminder.Success)
        {
            if (_reminders is null) return new(true, "Reminders are unavailable in this session.", false);
            var identifier = cancelReminder.Groups[1].Value.Trim().Trim('"', '\'');
            var upcoming = await _reminders.GetUpcomingAsync(cancellationToken: cancellationToken);
            var matches = upcoming.Where(item => item.Id.StartsWith(identifier, StringComparison.OrdinalIgnoreCase) ||
                item.Title.Equals(identifier, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length == 0) return new(true, "No upcoming reminder matched that ID or exact title.", false);
            if (matches.Length > 1) return new(true, "More than one reminder matched that title. Use its ID from ‘show reminders’.", false);
            var canceled = await _reminders.CancelAsync(matches[0].Id, cancellationToken);
            return new(true, canceled ? $"Canceled reminder: {matches[0].Title}." : "That reminder was already delivered or canceled.", canceled);
        }
        var folder = CreateFolderPattern().Match(normalized);
        if (folder.Success) return await CreateDesktopFolderAsync(folder.Groups[1].Value.Trim().Trim('"', '\''), cancellationToken);
        var createText = CreateTextFilePattern().Match(normalized);
        if (createText.Success) return await CreateDesktopTextFileAsync(
            createText.Groups[1].Value.Trim().Trim('"', '\''), createText.Groups[2].Value, cancellationToken);
        var readText = ReadTextFilePattern().Match(normalized);
        if (readText.Success) return await ReadDesktopTextFileAsync(readText.Groups[1].Value.Trim().Trim('"', '\''), cancellationToken);
        if (Regex.IsMatch(normalized, @"^(?:show|check|what(?:'s| is)?)\s+(?:my\s+)?(?:ram|memory)(?:\s+usage)?\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_systemInfo is null) return new(true, "System memory information is unavailable.", false);
            try
            {
                var memory = _systemInfo.GetMemoryStatus();
                if (memory.TotalBytes == 0) return new(true, "Windows returned an empty memory status.", false);
                var usedGiB = memory.UsedBytes / 1024d / 1024 / 1024;
                var totalGiB = memory.TotalBytes / 1024d / 1024 / 1024;
                var percent = memory.UsedBytes * 100d / memory.TotalBytes;
                return new(true, $"Memory in use: {usedGiB:0.0} GB of {totalGiB:0.0} GB ({percent:0}%).");
            }
            catch (InvalidOperationException ex) { return new(true, ex.Message, false); }
        }
        if (Regex.IsMatch(normalized, @"^(?:show|check|give me)\s+(?:my\s+)?(?:system status|system performance|computer status)\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_systemInfo is null) return new(true, "System information is unavailable.", false);
            try
            {
                var memory = _systemInfo.GetMemoryStatus();
                var snapshot = await _systemInfo.GetSystemSnapshotAsync(cancellationToken);
                var usedGiB = memory.UsedBytes / 1024d / 1024 / 1024;
                var totalGiB = memory.TotalBytes / 1024d / 1024 / 1024;
                var receivedGiB = snapshot.BytesReceived / 1024d / 1024 / 1024;
                var sentGiB = snapshot.BytesSent / 1024d / 1024 / 1024;
                var cpu = snapshot.CpuPercent is null ? "unavailable" : $"{snapshot.CpuPercent:0}%";
                var gpu = snapshot.GpuBusiestEnginePercent is null ? "unavailable" : $"{snapshot.GpuBusiestEnginePercent:0}% busiest engine";
                return new(true, $"System status (local): CPU {cpu} across {snapshot.ProcessorCount} logical processors; GPU {gpu}; RAM {usedGiB:0.0}/{totalGiB:0.0} GB; network totals received {receivedGiB:0.00} GB, sent {sentGiB:0.00} GB (active non-loopback adapters).");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or NetworkInformationException)
            { return new(true, $"Could not read system status: {ex.Message}", false); }
        }
        if (Regex.IsMatch(normalized, @"^(?:find|show)\s+(?:my\s+)?(?:latest|most recent)\s+pdf(?:\s+file)?\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (_fileSearch is null) return new(true, "Local file search is unavailable.", false);
            try
            {
                var search = await _fileSearch.FindLatestPdfAsync(cancellationToken);
                if (search.Match is null)
                    return new(true, search.SearchLimitReached
                        ? "No PDF was found before the local search limit was reached."
                        : "I couldn't find a PDF in Desktop, Documents, or Downloads.");
                var suffix = search.SearchLimitReached ? " Search limit reached; there may be a newer PDF." : string.Empty;
                return new(true, $"Your latest PDF is {Path.GetFileName(search.Match.FullPath)}\n{search.Match.FullPath}\nModified {search.Match.LastModified:g}.{suffix}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { return new(true, $"Local file search failed: {ex.Message}", false); }
        }
        if (Regex.IsMatch(normalized, @"^(what(?:'s| is) the )?time\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new(true, $"It is {now.ToString("h:mm tt", CultureInfo.CurrentCulture)}.");
        if (Regex.IsMatch(normalized, @"^(what(?:'s| is) the )?date\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new(true, $"Today is {now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture)}.");

        var calculation = CalculationPattern().Match(normalized);
        if (calculation.Success && TryCalculate(calculation.Groups[1].Value, out var answer))
            return new(true, answer.ToString("G", CultureInfo.InvariantCulture));

        var launch = LaunchPattern().Match(normalized);
        if (launch.Success)
        {
            var name = launch.Groups[1].Value.Trim().TrimEnd('.', '!');
            if (!Applications.TryGetValue(name, out var executable))
                return new(true, $"I can open a limited set of apps directly right now. '{name}' is not in the launch allowlist yet.", false);
            if (_applicationLauncher is null) return new(true, "Application launching is unavailable in this session.", false);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _applicationLauncher.Start(executable);
                await Task.Yield();
                return new(true, $"Opened {DisplayName(name)}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new(true, $"Windows could not open {DisplayName(name)}: {ex.Message}", false);
            }
        }

        return new(false);
    }

    private async Task<LocalCommandResult> QueueProjectTaskAsync(JarvisBackgroundTaskKind kind, CancellationToken cancellationToken)
    {
        if (_projectTasks is null) return new(true, "Background project tasks are unavailable.", false);
        var task = await _projectTasks.QueueAsync(kind, cancellationToken);
        return new(true, $"Queued {kind} as task {task.Id}. I’ll run it in the background; use ‘show project tasks’ to check progress.");
    }

    private async Task<LocalCommandResult> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_performance is null) return new(true, "Performance diagnostics are unavailable.", false);
        var d = await _performance.GetDiagnosticsAsync(cancellationToken);
        if (d.SampleCount == 0) return new(true, "No performance samples yet. Local and Gemini interaction timings will appear here after you use JARVIS.");
        static string Ms(long? value) => value is null ? "n/a" : $"{value.Value} ms";
        var text = $"JARVIS diagnostics (local-only measurements; {d.SampleCount} recent interactions)\n" +
            $"Local commands: {d.LocalSampleCount}; total p50/p90/p95 {Ms(d.LocalP50Ms)} / {Ms(d.LocalP90Ms)} / {Ms(d.LocalP95Ms)}\n" +
            $"Gemini requests: {d.GeminiSampleCount}; total p50/p90/p95 {Ms(d.GeminiP50Ms)} / {Ms(d.GeminiP90Ms)} / {Ms(d.GeminiP95Ms)}\n" +
            $"Gemini first output p50/p90 {Ms(d.GeminiFirstOutputP50Ms)} / {Ms(d.GeminiFirstOutputP90Ms)}; model time p50 {Ms(d.GeminiModelP50Ms)}; tool time p50 {Ms(d.GeminiToolP50Ms)}\n" +
            $"Speech recognition p50/p90 {Ms(d.SpeechInputP50Ms)} / {Ms(d.SpeechInputP90Ms)}; speech output p50/p90 {Ms(d.SpeechP50Ms)} / {Ms(d.SpeechP90Ms)}\n" +
            $"Model calls {d.ModelCallCount}; local tool calls {d.ToolCallCount}\n" +
            $"Failed interactions {d.FailedCount}; canceled {d.CanceledCount}. No prompts or transcripts are stored in these measurements.";
        return new(true, text);
    }

    public async Task<LocalCommandResult> CreateDesktopFolderAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_approval is null || _files is null) return new(true, "Desktop folder creation is unavailable in this session.", false);
        string target;
        try { target = _files.GetDesktopFolderPath(name); }
        catch (ArgumentException ex) { return new(true, ex.Message, false); }
        var approved = await _approval.ConfirmAsync("Create a Desktop folder?", target, cancellationToken);
        if (!approved) return new(true, "Canceled. No folder was created.", false);
        try { return new(true, await _files.CreateDesktopFolderAsync(name, cancellationToken)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(true, $"Could not create the folder: {ex.Message}", false); }
    }

    public async Task<LocalCommandResult> CreateDesktopTextFileAsync(string name, string content, CancellationToken cancellationToken = default)
    {
        if (_approval is null || _files is null) return new(true, "Desktop file creation is unavailable in this session.", false);
        if (content.Length > 1_000_000) return new(true, "Text file content is limited to 1,000,000 characters.", false);
        string target;
        try
        {
            target = _files.GetDesktopFilePath(name);
            if (!Path.GetExtension(target).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(target).Equals(".md", StringComparison.OrdinalIgnoreCase))
                return new(true, "For now, JARVIS can create only .txt or .md files on the Desktop.", false);
        }
        catch (ArgumentException ex) { return new(true, ex.Message, false); }
        var preview = content.Length > 500 ? content[..500] + "… (preview truncated)" : content;
        var approved = await _approval.ConfirmAsync("Create a text file?", $"Path: {target}\nCharacters: {content.Length}\n\nContent preview:\n{preview}", cancellationToken);
        if (!approved) return new(true, "Canceled. No file was created.", false);
        try { return new(true, await _files.CreateDesktopTextFileAsync(name, content, cancellationToken)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return new(true, $"Could not create the file: {ex.Message}", false); }
    }

    public async Task<LocalCommandResult> ReadDesktopTextFileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_files is null) return new(true, "Desktop file reading is unavailable in this session.", false);
        try
        {
            var content = await _files.ReadDesktopTextFileAsync(name, cancellationToken);
            return new(true, string.IsNullOrWhiteSpace(content) ? "The file is empty." : content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return new(true, $"Could not read the file: {ex.Message}", false); }
    }

    private static string DisplayName(string name) => name.ToLowerInvariant() switch
    {
        "calc" or "calculator" => "Calculator",
        "file explorer" or "explorer" => "File Explorer",
        "vs code" or "visual studio code" or "code" => "Visual Studio Code",
        "visual studio" => "Visual Studio",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name)
    };

    private static bool TryCalculate(string expression, out double result)
    {
        result = 0;
        var match = ArithmeticPattern().Match(expression.Replace("×", "*", StringComparison.Ordinal).Replace("÷", "/", StringComparison.Ordinal));
        if (!match.Success || !double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var left) ||
            !double.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out var right)) return false;
        result = match.Groups[2].Value switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" when right != 0 => left / right,
            _ => double.NaN
        };
        return double.IsFinite(result);
    }

    [GeneratedRegex(@"^(?:open|launch|start)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaunchPattern();

    [GeneratedRegex("^create\\s+(?:a\\s+)?folder\\s+(?:called|named)\\s+(.+?)\\s+(?:on|at)\\s+(?:my\\s+)?desktop[.!]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateFolderPattern();

    [GeneratedRegex("^create\\s+(?:a\\s+)?text\\s+file\\s+(?:called|named)\\s+(.+?)\\s+on\\s+(?:my\\s+)?desktop\\s+with\\s+content:\\s*([\\s\\S]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTextFilePattern();

    [GeneratedRegex("^read\\s+(?:the\\s+)?file\\s+(.+?)\\s+(?:on|from)\\s+(?:my\\s+)?desktop[.!]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadTextFilePattern();

    [GeneratedRegex("^(?:remind me|set a reminder) in (\\d{1,6})\\s+(minutes?|hours?|days?)\\s+to\\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReminderPattern();

    [GeneratedRegex("^(?:cancel|remove)\\s+(?:the\\s+)?reminder\\s+(.+?)\\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CancelReminderPattern();

    [GeneratedRegex(@"^(?:calculate|what is)\s+(.+?)\s*\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalculationPattern();

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*([+\-*/])\s*(-?\d+(?:\.\d+)?)$")]
    private static partial Regex ArithmeticPattern();
}
