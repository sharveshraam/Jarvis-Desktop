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
        var model = string.IsNullOrWhiteSpace(preferences.GeminiModel) ? UserPreferences.Default.GeminiModel : preferences.GeminiModel.Trim();
        if (model.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(model, "^[A-Za-z0-9][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException("Enter a valid Gemini model ID using letters, numbers, dots, underscores, or hyphens.", nameof(preferences));
        var workspace = string.IsNullOrWhiteSpace(preferences.ProjectWorkspacePath)
            ? null
            : Path.GetFullPath(preferences.ProjectWorkspacePath.Trim());
        if (workspace is not null && !Directory.Exists(workspace))
            throw new ArgumentException("Choose an existing project folder for the workspace.", nameof(preferences));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, preferences with
                {
                    GlobalShortcut = preferences.GlobalShortcut.Trim(),
                    ProjectWorkspacePath = workspace,
                    GeminiModel = model
                }, Options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
