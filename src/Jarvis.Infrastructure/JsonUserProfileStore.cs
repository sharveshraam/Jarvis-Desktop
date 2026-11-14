using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.Infrastructure;

public sealed class JsonUserProfileStore(string filePath) : IUserProfileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = Path.GetFullPath(filePath);

    public async Task<UserProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return UserProfile.Default;
        await using var stream = File.OpenRead(_filePath);
        var profile = await JsonSerializer.DeserializeAsync<UserProfile>(stream, Options, cancellationToken);
        return profile is null ? UserProfile.Default : Normalize(profile);
    }

    public async Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalized = Normalize(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, normalized, Options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static UserProfile Normalize(UserProfile profile)
    {
        var name = profile.DisplayName?.Trim() ?? string.Empty;
        var context = profile.PersonalContext?.Trim() ?? string.Empty;
        if (name.Length > 100) throw new ArgumentException("The profile name must be 100 characters or fewer.", nameof(profile));
        if (context.Length > 4_000) throw new ArgumentException("Personal context must be 4,000 characters or fewer.", nameof(profile));
        return new UserProfile(name, context);
    }
}
