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
}
