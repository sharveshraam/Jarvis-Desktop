using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.Tests;

public sealed class ReminderTests
{
    [Fact]
    public async Task RelativeReminderPersistsAndIsMarkedDeliveredOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteReminderStore(Path.Combine(directory, "jarvis.db"));
            await store.InitializeAsync();
            var router = new LocalCommandRouter(reminders: store);

            var created = await router.TryRunAsync("remind me in 10 minutes to stretch");
            Assert.True(created.Handled);
            Assert.Contains("stretch", created.Response);
            var upcoming = await store.GetUpcomingAsync();
            var reminder = Assert.Single(upcoming);
            Assert.Equal("stretch", reminder.Title);
            Assert.InRange(reminder.DueAt, DateTimeOffset.UtcNow.AddMinutes(9), DateTimeOffset.UtcNow.AddMinutes(11));
            Assert.Empty(await store.GetDueAsync(DateTimeOffset.UtcNow));

            await store.MarkNotifiedAsync(reminder.Id);
            Assert.Empty(await store.GetUpcomingAsync());
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

    [Theory]
    [InlineData("remind me in 0 minutes to stretch")]
    [InlineData("remind me in 600000 minutes to stretch")]
    public async Task RejectsInvalidOrExcessiveReminderTimes(string text)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteReminderStore(Path.Combine(directory, "jarvis.db"));
            await store.InitializeAsync();
            var result = await new LocalCommandRouter(reminders: store).TryRunAsync(text);
            Assert.True(result.Handled);
            Assert.Empty(await store.GetUpcomingAsync());
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
    public async Task DueReminderIsReturnedAndThenSuppressedAfterDelivery()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteReminderStore(Path.Combine(directory, "jarvis.db"));
            await store.InitializeAsync();
            var reminder = new JarvisReminder("due-test", "Take a break", DateTimeOffset.UtcNow.AddMinutes(-1), false);
            await store.AddAsync(reminder);

            var due = await store.GetDueAsync(DateTimeOffset.UtcNow);
            Assert.Equal(reminder.Id, Assert.Single(due).Id);
            await store.MarkNotifiedAsync(reminder.Id);
            Assert.Empty(await store.GetDueAsync(DateTimeOffset.UtcNow));
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
    public async Task CancelsReminderAndKeepsItOutOfUpcomingAndDueLists()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteReminderStore(Path.Combine(directory, "jarvis.db"));
            await store.InitializeAsync();
            var reminder = new JarvisReminder("cancel-this", "Lab meeting", DateTimeOffset.UtcNow.AddMinutes(-1), false);
            await store.AddAsync(reminder);

            var canceled = await new LocalCommandRouter(reminders: store).TryRunAsync("cancel reminder Lab meeting");

            Assert.True(canceled.Succeeded);
            Assert.Contains("Lab meeting", canceled.Response);
            Assert.Empty(await store.GetUpcomingAsync());
            Assert.Empty(await store.GetDueAsync(DateTimeOffset.UtcNow));
            Assert.False(await store.CancelAsync(reminder.Id));
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
    public async Task ListsReminderIdsAndRequiresExactTitlesForCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-test-{Guid.NewGuid():N}");
        try
        {
            var store = new SqliteReminderStore(Path.Combine(directory, "jarvis.db"));
            await store.InitializeAsync();
            await store.AddAsync(new JarvisReminder("same-a", "Study", DateTimeOffset.UtcNow.AddHours(1), false));
            await store.AddAsync(new JarvisReminder("same-b", "Study", DateTimeOffset.UtcNow.AddHours(2), false));
            var router = new LocalCommandRouter(reminders: store);

            var listing = await router.TryRunAsync("show reminders");
            Assert.Contains("[same-a]", listing.Response);
            var ambiguous = await router.TryRunAsync("cancel reminder Study");
            Assert.False(ambiguous.Succeeded);
            Assert.Contains("Use its ID", ambiguous.Response);
            var canceled = await router.TryRunAsync("cancel reminder same-b");
            Assert.True(canceled.Succeeded);
            Assert.Single(await store.GetUpcomingAsync());
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
    public async Task AddsCancellationColumnToExistingReminderDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jarvis-reminder-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "jarvis.db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE reminders (id TEXT PRIMARY KEY, title TEXT NOT NULL, due_at_utc TEXT NOT NULL, notified INTEGER NOT NULL DEFAULT 0);" +
                    "INSERT INTO reminders (id, title, due_at_utc, notified) VALUES ('legacy', 'Existing reminder', $due, 0);";
                command.Parameters.AddWithValue("$due", DateTimeOffset.UtcNow.AddHours(1).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
            }

            var store = new SqliteReminderStore(databasePath);
            await store.InitializeAsync();
            Assert.Equal("Existing reminder", Assert.Single(await store.GetUpcomingAsync()).Title);
            Assert.True(await store.CancelAsync("legacy"));
            Assert.Empty(await store.GetUpcomingAsync());
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
}
