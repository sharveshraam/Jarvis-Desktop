using System.Text.RegularExpressions;
using System.Text;

namespace Jarvis.Windows;

public sealed partial class DesktopFileSystem(string? desktopRoot = null)
{
    private readonly string _desktopRoot = Path.GetFullPath(desktopRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string GetDesktopFolderPath(string folderName)
    {
        var name = ValidateName(folderName, 80);

        return GetDesktopEntryPath(name, nameof(folderName));
    }

    public string GetDesktopFilePath(string fileName) => GetDesktopEntryPath(ValidateName(fileName, 160), nameof(fileName));

    public Task<string> CreateDesktopFolderAsync(string folderName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GetDesktopFolderPath(folderName);
        if (!Directory.Exists(_desktopRoot))
            throw new DirectoryNotFoundException("The Desktop folder could not be found.");
        if (Directory.Exists(target) || File.Exists(target))
            return Task.FromResult($"A file or folder named '{Path.GetFileName(target)}' already exists on the Desktop. Nothing was overwritten.");

        Directory.CreateDirectory(target);
        if (!Directory.Exists(target))
            throw new IOException("Windows did not confirm the new folder was created.");
        return Task.FromResult($"Created and verified the folder on your Desktop: {Path.GetFileName(target)}");
    }

    public async Task<string> CreateDesktopTextFileAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        if (content.Length > 1_000_000) throw new ArgumentException("Text file content is limited to 1,000,000 characters.", nameof(content));
        cancellationToken.ThrowIfCancellationRequested();
        var target = GetDesktopFilePath(fileName);
        if (!Directory.Exists(_desktopRoot)) throw new DirectoryNotFoundException("The Desktop folder could not be found.");
        if (Directory.Exists(target) || File.Exists(target))
            return $"A file or folder named '{Path.GetFileName(target)}' already exists on the Desktop. Nothing was overwritten.";

        var temporary = Path.Combine(_desktopRoot, $".jarvis-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target);
            return $"Created the text file on your Desktop: {Path.GetFileName(target)}";
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<string> ReadDesktopTextFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var target = GetDesktopFilePath(fileName);
        var extension = Path.GetExtension(target);
        if (!TextExtensions().Contains(extension))
            throw new ArgumentException("JARVIS can read .txt, .md, .csv, .json, .xml, and source-code text files from the Desktop.", nameof(fileName));
        var info = new FileInfo(target);
        if (!info.Exists) throw new FileNotFoundException("That Desktop file was not found.", target);
        if (info.Length > 1_000_000) throw new InvalidOperationException("This file is over the 1 MB preview limit.");
        return await File.ReadAllTextAsync(target, new UTF8Encoding(false, true), cancellationToken);
    }

    private string GetDesktopEntryPath(string name, string parameterName)
    {
        var target = Path.GetFullPath(Path.Combine(_desktopRoot, name));
        var expectedPrefix = _desktopRoot.EndsWith(Path.DirectorySeparatorChar) ? _desktopRoot : _desktopRoot + Path.DirectorySeparatorChar;
        if (!target.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The item must stay directly inside the Desktop folder.", parameterName);
        return target;
    }

    private static string ValidateName(string value, int maxLength)
    {
        var name = value.Trim();
        if (name.Length is 0 || name.Length > maxLength || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || ReservedDeviceNamePattern().IsMatch(name))
            throw new ArgumentException($"Use a name up to {maxLength} characters without path separators or reserved Windows names.", nameof(value));
        return name;
    }

    private static HashSet<string> TextExtensions() => new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".md", ".csv", ".json", ".xml", ".cs", ".c", ".h", ".cpp", ".py", ".js", ".ts", ".html", ".css", ".sql", ".java" };

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceNamePattern();
}
