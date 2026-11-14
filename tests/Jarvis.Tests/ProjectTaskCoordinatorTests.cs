using Jarvis.Agent;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.Tests;

public sealed class ProjectTaskCoordinatorTests
{
    [Fact]
    public async Task PersistsBuildOnlyAfterUserApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-queue-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteBackgroundTaskStore(Path.Combine(root, "tasks.db")); await store.InitializeAsync();
            var prefs = new Preferences(root); var approval = new Approval(false);
            var coordinator = new BackgroundTaskCoordinator(store, prefs, approval);
            await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.QueueAsync(JarvisBackgroundTaskKind.BuildProject));
            Assert.Empty(await store.GetQueuedAsync());
            Assert.Contains("dotnet build --no-restore", approval.Details);

            approval.Approved = true;
            var task = await coordinator.QueueAsync(JarvisBackgroundTaskKind.BuildProject);
            Assert.Equal(JarvisBackgroundTaskState.Queued, task.State);
            Assert.Equal(task.Id, Assert.Single(await store.GetQueuedAsync()).Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private sealed class Preferences(string path) : IUserPreferencesStore
    {
        public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserPreferences("Ctrl+Shift+F12", false, ProjectWorkspacePath: path));
        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Approval(bool approved) : IActionApproval
    {
        public bool Approved { get; set; } = approved;
        public string Details { get; private set; } = "";
        public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default)
        { Details = details; return Task.FromResult(Approved); }
    }
}
