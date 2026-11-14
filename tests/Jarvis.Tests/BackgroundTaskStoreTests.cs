using Jarvis.Core;
using Jarvis.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.Tests;

public sealed class BackgroundTaskStoreTests
{
    [Fact]
    public async Task PersistsTaskProgressAndMarksInterruptedWorkWithoutRequeueingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteBackgroundTaskStore(Path.Combine(root, "tasks.db"));
            await store.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            var task = new JarvisBackgroundTask("a1b2c3d4", JarvisBackgroundTaskKind.BuildProject,
                JarvisBackgroundTaskState.Queued, 0, "Waiting", null, now, now);
            await store.AddAsync(task);
            await store.UpdateAsync(task with { State = JarvisBackgroundTaskState.Running, ProgressPercent = 30, Stage = "Compiling" });
            Assert.Equal("Compiling", (await store.GetAsync(task.Id))!.Stage);
            Assert.Equal(1, await store.MarkRunningInterruptedAsync());
            var interrupted = await store.GetAsync(task.Id);
            Assert.Equal(JarvisBackgroundTaskState.Interrupted, interrupted!.State);
            Assert.Empty(await store.GetQueuedAsync());
            Assert.Contains("not rerun automatically", interrupted.Stage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
