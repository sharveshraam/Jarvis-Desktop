using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Windows;
using Xunit;

namespace Jarvis.Tests;

public sealed class MemoryStoreTests
{
    [Fact]
    public async Task MemoryPersistsSearchesAndDeletesOnlyMatchingDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-memory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteMemoryStore(Path.Combine(root, "jarvis.db")); await store.InitializeAsync();
            await store.AddAsync("Prefer concise answers for quick commands");
            await store.AddAsync("Current project uses C# and WPF");
            await store.AddAsync("Current project uses C# and WPF");
            Assert.Equal(2, (await store.SearchAsync()).Count);
            Assert.Single(await store.SearchAsync("concise"));
            Assert.Equal(1, await store.DeleteMatchingAsync("concise"));
            Assert.Single(await store.SearchAsync());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RouterKeepsMemoryLocalAndRequiresConfirmationBeforeForget()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-memory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteMemoryStore(Path.Combine(root, "jarvis.db")); await store.InitializeAsync();
            var approval = new FakeApproval(false);
            var router = new LocalCommandRouter(approval: approval, memory: store);
            var saved = await router.TryRunAsync("remember I prefer concise explanations");
            Assert.Contains("on this computer", saved.Response);
            var listed = await router.TryRunAsync("what do you remember about me?");
            Assert.Contains("I prefer concise explanations", listed.Response);
            var forget = await router.TryRunAsync("forget concise explanations");
            Assert.False(forget.Succeeded);
            Assert.Single(await store.SearchAsync());
            Assert.Contains("I prefer concise explanations", approval.Details);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private sealed class FakeApproval(bool approved) : IActionApproval
    {
        public string Details { get; private set; } = "";
        public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default) { Details = details; return Task.FromResult(approved); }
    }
}
