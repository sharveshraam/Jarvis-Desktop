using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsFileSearch : ILocalFileSearch
{
    private const int MaxDirectories = 4_000;
    private readonly string[] _roots;

    public WindowsFileSearch(IEnumerable<string>? roots = null)
    {
        _roots = (roots ?? GetDefaultRoots())
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<LocalFileSearchResult> FindLatestPdfAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var pending = new Queue<string>(_roots);
        var visited = 0;
        LocalFileMatch? newest = null;
        while (pending.Count > 0 && visited < MaxDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();
            visited++;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (visited + pending.Count < MaxDirectories) pending.Enqueue(entry);
                            continue;
                        }
                        if (!string.Equals(Path.GetExtension(entry), ".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                        var modified = File.GetLastWriteTime(entry);
                        if (newest is null || modified > newest.LastModified)
                            newest = new LocalFileMatch(Path.GetFullPath(entry), modified);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new LocalFileSearchResult(newest, pending.Count > 0);
    }, cancellationToken);

    private static IEnumerable<string> GetDefaultRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
