using System.Text;

namespace Jarvis.Windows;

public sealed record UserFileSummary(string FullPath, long Length, DateTime LastModifiedUtc);

/// <summary>Bounded text-file access limited to the current user's Desktop, Documents, and Downloads.</summary>
public sealed class SafeUserFiles(IEnumerable<string>? roots = null)
{
    private const long MaxFileBytes = 1_000_000;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".md", ".csv", ".json", ".xml", ".cs", ".c", ".h", ".cpp", ".py", ".js", ".ts", ".html", ".css", ".sql", ".java" };

    private readonly string[] _roots = (roots ?? GetDefaultRoots())
        .Where(Directory.Exists)
        .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public UserFileSummary Inspect(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Use the full path returned by JARVIS file search.", nameof(fullPath));
        var path = Path.GetFullPath(fullPath);
        var root = _roots.FirstOrDefault(candidate => path.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (root is null) throw new ArgumentException("JARVIS can read files only from Desktop, Documents, or Downloads.", nameof(fullPath));
        if (!TextExtensions.Contains(Path.GetExtension(path)))
            throw new ArgumentException("JARVIS can send only supported text files (.txt, .md, .csv, .json, .xml, or source-code text files).", nameof(fullPath));

        var relative = Path.GetRelativePath(root, path);
        var current = new DirectoryInfo(root);
        foreach (var segment in Path.GetDirectoryName(relative)?.Split(Path.DirectorySeparatorChar) ?? [])
        {
            if (segment == ".") continue;
            current = new DirectoryInfo(Path.Combine(current.FullName, segment));
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("JARVIS cannot follow linked or reparse-point folders.", nameof(fullPath));
        }

        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("That file was not found.", path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("JARVIS cannot read linked or reparse-point files.", nameof(fullPath));
        if (info.Length > MaxFileBytes) throw new InvalidOperationException("The selected file exceeds the 1 MB text-file limit.");
        return new UserFileSummary(path, info.Length, info.LastWriteTimeUtc);
    }

    public async Task<string> ReadIfUnchangedAsync(UserFileSummary approvedFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedFile);
        var current = Inspect(approvedFile.FullPath);
        if (current.Length != approvedFile.Length || current.LastModifiedUtc != approvedFile.LastModifiedUtc)
            throw new InvalidOperationException("The file changed after confirmation. Review it again before sharing.");
        var content = await File.ReadAllTextAsync(current.FullPath, new UTF8Encoding(false, true), cancellationToken);
        var afterRead = Inspect(current.FullPath);
        if (afterRead.Length != approvedFile.Length || afterRead.LastModifiedUtc != approvedFile.LastModifiedUtc)
            throw new InvalidOperationException("The file changed while it was being read. Review it again before sharing.");
        return content;
    }

    private static IEnumerable<string> GetDefaultRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
