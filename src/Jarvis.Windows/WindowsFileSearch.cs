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

    public Task<LocalFileSearchResults> FindFilesByNameAsync(string nameQuery, int limit = 10, CancellationToken cancellationToken = default)
    {
        var query = nameQuery.Trim();
        if (query.Length is 0 or > 120) throw new ArgumentException("File search text must be between 1 and 120 characters.", nameof(nameQuery));
        if (query.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || query.Contains(Path.DirectorySeparatorChar) || query.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Search for a filename, not a path or wildcard pattern.", nameof(nameQuery));
        var resultLimit = Math.Clamp(limit, 1, 25);
        return Task.Run(() =>
        {
            var pending = new Queue<string>(_roots);
            var visited = 0;
            var found = new List<LocalFileMatch>();
            var truncated = false;
            while (pending.Count > 0 && visited < MaxDirectories && !truncated)
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
                            if (!Path.GetFileName(entry).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                            found.Add(new LocalFileMatch(Path.GetFullPath(entry), File.GetLastWriteTime(entry)));
                            if (found.Count > resultLimit) { truncated = true; break; }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new LocalFileSearchResults(found.OrderByDescending(match => match.LastModified).Take(resultLimit).ToArray(),
                truncated || pending.Count > 0 || visited >= MaxDirectories);
        }, cancellationToken);
    }

    private static IEnumerable<string> GetDefaultRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
