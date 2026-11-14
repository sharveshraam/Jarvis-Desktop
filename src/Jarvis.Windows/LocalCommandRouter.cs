using System.Globalization;
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

    public LocalCommandRouter(IActionApproval? approval = null, DesktopFileSystem? files = null, ISystemInfoProvider? systemInfo = null, ILocalFileSearch? fileSearch = null, IApplicationLauncher? applicationLauncher = null, IReminderStore? reminders = null)
    {
        _approval = approval;
        _files = files;
        _systemInfo = systemInfo;
        _fileSearch = fileSearch;
        _applicationLauncher = applicationLauncher;
        _reminders = reminders;
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
                : string.Join(Environment.NewLine, upcoming.Select(item => $"{item.DueAt.ToLocalTime():g} — {item.Title}")));
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

    [GeneratedRegex(@"^(?:calculate|what is)\s+(.+?)\s*\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalculationPattern();

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*([+\-*/])\s*(-?\d+(?:\.\d+)?)$")]
    private static partial Regex ArithmeticPattern();
}
