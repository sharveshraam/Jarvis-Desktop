using System.Text.RegularExpressions;

namespace Jarvis.Windows;

public sealed partial class DesktopFileSystem(string? desktopRoot = null)
{
    private readonly string _desktopRoot = Path.GetFullPath(desktopRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string GetDesktopFolderPath(string folderName)
    {
        var name = folderName.Trim();
        if (name.Length is 0 or > 80 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || ReservedDeviceNamePattern().IsMatch(name))
            throw new ArgumentException("Use a folder name up to 80 characters without path separators or reserved Windows names.", nameof(folderName));

        var target = Path.GetFullPath(Path.Combine(_desktopRoot, name));
        var expectedPrefix = _desktopRoot.EndsWith(Path.DirectorySeparatorChar) ? _desktopRoot : _desktopRoot + Path.DirectorySeparatorChar;
        if (!target.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The folder must stay directly inside the Desktop folder.", nameof(folderName));
        return target;
    }

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

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceNamePattern();
}
