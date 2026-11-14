using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.Infrastructure;

public sealed class JsonUserPreferencesStore(string filePath) : IUserPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = Path.GetFullPath(filePath);

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return UserPreferences.Default;
        await using var stream = File.OpenRead(_filePath);
        var preferences = await JsonSerializer.DeserializeAsync<UserPreferences>(stream, Options, cancellationToken);
        return preferences ?? UserPreferences.Default;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (string.IsNullOrWhiteSpace(preferences.GlobalShortcut) || preferences.GlobalShortcut.Length > 50)
            throw new ArgumentException("Enter a global shortcut between 1 and 50 characters.", nameof(preferences));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, preferences with { GlobalShortcut = preferences.GlobalShortcut.Trim() }, Options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
