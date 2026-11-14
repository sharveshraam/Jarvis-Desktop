using System.Text;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SqliteConversationStoreKeepsMessagesInOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteConversationStore(Path.Combine(directory, "history.db"));
            await store.InitializeAsync();
            var first = new ChatMessage("one", ChatRole.User, "hello", DateTimeOffset.UtcNow.AddSeconds(-1));
            var second = new ChatMessage("two", ChatRole.Assistant, "hi", DateTimeOffset.UtcNow);
            await store.AddAsync(first);
            await store.AddAsync(second);

            var messages = await store.GetRecentAsync(10);
            Assert.Collection(messages,
                item => Assert.Equal(first, item),
                item => Assert.Equal(second, item));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task DpapiSecretStoreProtectsAndRestoresASecret()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-secret-test-{Guid.NewGuid():N}");
        const string testSecret = "synthetic-test-key-123";
        try
        {
            var store = new DpapiSecretStore(directory);
            await store.SetAsync("gemini-api-key", testSecret);

            var storedBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "gemini-api-key.dpapi"));
            Assert.Equal(-1, storedBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(testSecret)));
            Assert.Equal(testSecret, await store.GetAsync("gemini-api-key"));

            await store.DeleteAsync("gemini-api-key");
            Assert.Null(await store.GetAsync("gemini-api-key"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task UserProfileDefaultsCanBeEditedAndPersistedLocally()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-profile-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "profile.json");
            var store = new JsonUserProfileStore(path);
            Assert.Equal(UserProfile.Default, await store.GetAsync());

            var edited = new UserProfile("  Ram  ", "  Explain DSP with a derivation and one practical example.  ");
            await store.SaveAsync(edited);
            Assert.Equal(new UserProfile("Ram", "Explain DSP with a derivation and one practical example."), await new JsonUserProfileStore(path).GetAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task UserProfileStoreRejectsOversizedContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-profile-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonUserProfileStore(Path.Combine(directory, "profile.json"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UserProfile("Ram", new string('x', 4_001))));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task UserPreferencesPersistShortcutAndStartupChoice()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-prefs-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var store = new JsonUserPreferencesStore(path);
            Assert.Equal(UserPreferences.Default, await store.GetAsync());
            await store.SaveAsync(new UserPreferences("  Ctrl+Shift+Space  ", true, true));
            Assert.Equal(new UserPreferences("Ctrl+Shift+Space", true, true), await new JsonUserPreferencesStore(path).GetAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public async Task UserPreferencesRejectEmptyShortcut()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-prefs-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonUserPreferencesStore(Path.Combine(directory, "preferences.json"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UserPreferences(" ", false)));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
