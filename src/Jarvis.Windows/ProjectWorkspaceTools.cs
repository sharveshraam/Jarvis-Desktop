using System.Text;
using Jarvis.Core;

namespace Jarvis.Windows;

/// <summary>Bounded, workspace-only source access. Every path is resolved beneath the chosen root.</summary>
public sealed class ProjectWorkspaceTools(IUserPreferencesStore preferences, IActionApproval? approval)
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".sln", ".xaml", ".json", ".md", ".txt", ".xml", ".yml", ".yaml", ".js", ".ts", ".tsx", ".jsx", ".py", ".html", ".css", ".sql", ".sh", ".ps1", ".toml", ".ini", ".props", ".targets" };
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", "bin", "obj", "node_modules", "packages", "dist", "build" };
    private const int MaxFileBytes = 1_000_000;

    public async Task<string> ListFilesAsync(CancellationToken ct = default)
    {
        var root = await GetRootAsync(ct);
        if (!await ConfirmReadAsync(root, "List project files", "Project file names will be sent to Gemini.", ct)) return "Canceled; no project files were listed.";
        var output = new List<string>();
        foreach (var path in Enumerate(root))
        {
            output.Add(Path.GetRelativePath(root, path));
            if (output.Count == 200) break;
        }
        return output.Count == 0 ? "No supported source files found." : string.Join("\n", output);
    }

    public async Task<string> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 200) throw new ArgumentException("Search text must be between 1 and 200 characters.");
        var root = await GetRootAsync(ct);
        if (!await ConfirmReadAsync(root, "Search project code", "Matching code snippets will be sent to Gemini.", ct)) return "Canceled; project code was not searched.";
        var matches = new List<string>();
        foreach (var file in Enumerate(root))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                var lines = await File.ReadAllLinesAsync(file, ct);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
                        if (matches.Count == 50) return string.Join("\n", matches);
                    }
            }
            catch (IOException) { }
        }
        return matches.Count == 0 ? "No matches." : string.Join("\n", matches);
    }

    public async Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var (root, path) = await ResolveAsync(relativePath, ct);
        if (!await ConfirmReadAsync(path, "Send project file to Gemini", "This source file's contents will be sent to Google Gemini.", ct)) return "Canceled; the file was not read or shared.";
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Project file was not found.");
        if (info.Length > MaxFileBytes) throw new ArgumentException("Project file is larger than the 1 MB read limit.");
        var text = await File.ReadAllTextAsync(path, ct);
        return $"{Path.GetRelativePath(root, path)}\n{text}";
    }

    public async Task<string> WriteAsync(string relativePath, string content, CancellationToken ct = default)
    {
        var (root, path) = await ResolveAsync(relativePath, ct);
        if (content.Length > 500_000) throw new ArgumentException("New file content is larger than the 500 KB limit.");
        var exists = File.Exists(path);
        if (exists && new FileInfo(path).Length > MaxFileBytes) throw new ArgumentException("Existing project file exceeds the edit preview limit.");
        var before = exists ? await File.ReadAllTextAsync(path, ct) : "(new file)";
        var approved = approval is not null && await approval.ConfirmAsync(
            exists ? "Replace project file?" : "Create project file?",
            $"{Path.GetRelativePath(root, path)}\n\nBefore:\n{Preview(before)}\n\nAfter:\n{Preview(content)}\n\nApprove this local file change?", ct);
        if (!approved) return "Canceled; the project file was not changed.";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".jarvis-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
            return $"Saved {Path.GetRelativePath(root, path)}.";
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<bool> ConfirmReadAsync(string item, string title, string explanation, CancellationToken ct) =>
        approval is not null && await approval.ConfirmAsync(title, $"{explanation}\n{item}", ct);

    private async Task<string> GetRootAsync(CancellationToken ct)
    {
        var preferencesNow = await preferences.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(preferencesNow.ProjectWorkspacePath)) throw new InvalidOperationException("Choose a project workspace in Settings first.");
        var root = Path.GetFullPath(preferencesNow.ProjectWorkspacePath);
        if (!Directory.Exists(root)) throw new InvalidOperationException("The configured project workspace no longer exists.");
        return root;
    }

    private async Task<(string Root, string Path)> ResolveAsync(string relative, CancellationToken ct)
    {
        var root = await GetRootAsync(ct);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new ArgumentException("Use a relative path inside the project workspace.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path must stay inside the selected project workspace.");
        if (!Extensions.Contains(Path.GetExtension(path))) throw new ArgumentException("That file extension is not allowed for project source access.");
        var current = new DirectoryInfo(root);
        foreach (var part in Path.GetDirectoryName(Path.GetRelativePath(root, path))?.Split(Path.DirectorySeparatorChar) ?? [])
        {
            if (part == ".") continue;
            if (Ignored.Contains(part)) throw new ArgumentException("Build output and dependency folders are excluded.");
            current = new DirectoryInfo(Path.Combine(current.FullName, part));
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Links and reparse points are excluded from project access.");
        }
        var fi = new FileInfo(path);
        if (fi.Exists && (fi.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked files are excluded from project access.");
        return (root, path);
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var dir))
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToArray(); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                var info = new FileInfo(entry);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!Ignored.Contains(info.Name)) pending.Push(entry);
                }
                else if (Extensions.Contains(Path.GetExtension(entry))) yield return entry;
            }
        }
    }

    private static string Preview(string text) => text.Length <= 1800 ? text : text[..1800] + "\n… preview truncated …";
}
