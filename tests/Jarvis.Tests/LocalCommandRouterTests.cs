using Jarvis.Windows;
using Jarvis.Core;
using Xunit;

namespace Jarvis.Tests;

public sealed class LocalCommandRouterTests
{
    private readonly LocalCommandRouter _router = new();

    [Theory]
    [InlineData("calculate 17 * 23", "391")]
    [InlineData("what is 20 / 4?", "5")]
    [InlineData("calculate 2 + 2", "4")]
    public async Task CalculatesSimpleArithmeticWithoutAi(string input, string expected)
    {
        var result = await _router.TryRunAsync(input);
        Assert.True(result.Handled);
        Assert.Equal(expected, result.Response);
    }

    [Fact]
    public async Task DoesNotTreatUnrecognizedTextAsLocalCommand()
    {
        var result = await _router.TryRunAsync("Explain eigenvectors in simple terms");
        Assert.False(result.Handled);
    }

    [Fact]
    public async Task DoesNotEvaluateDivisionByZero()
    {
        var result = await _router.TryRunAsync("calculate 1 / 0");
        Assert.False(result.Handled);
    }

    [Theory]
    [InlineData("build project", JarvisBackgroundTaskKind.BuildProject)]
    [InlineData("test my project", JarvisBackgroundTaskKind.TestProject)]
    public async Task QueuesAllowlistedProjectOperations(string prompt, JarvisBackgroundTaskKind expected)
    {
        var coordinator = new FakeProjectTaskCoordinator();
        var router = new LocalCommandRouter(projectTasks: coordinator);
        var result = await router.TryRunAsync(prompt);
        Assert.True(result.Handled);
        Assert.Equal(expected, coordinator.QueuedKind);
        Assert.Contains("background", result.Response, StringComparison.OrdinalIgnoreCase);
        var details = await router.TryRunAsync("show task 1234abcd");
        Assert.Contains("Queued", details.Response);
    }

    [Fact]
    public async Task CreatesDesktopFolderOnlyAfterApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var approval = new FakeApproval(true);
            var router = new LocalCommandRouter(approval, new DesktopFileSystem(root));
            var result = await router.TryRunAsync("create a folder named Projects on my desktop");
            Assert.True(result.Handled);
            Assert.True(Directory.Exists(Path.Combine(root, "Projects")));
            Assert.Contains(Path.Combine(root, "Projects"), approval.Details);
        }
        finally
        {
            var folder = Path.Combine(root, "Projects");
            if (Directory.Exists(folder)) Directory.Delete(folder);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task DecliningFolderCreationLeavesDesktopUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var router = new LocalCommandRouter(new FakeApproval(false), new DesktopFileSystem(root));
            var result = await router.TryRunAsync("create a folder named Projects on my desktop");
            Assert.Equal("Canceled. No folder was created.", result.Response);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task RejectsFolderPathTraversalBeforeApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var approval = new FakeApproval(true);
            var router = new LocalCommandRouter(approval, new DesktopFileSystem(root));
            var result = await router.TryRunAsync("create a folder named .. on my desktop");
            Assert.True(result.Handled);
            Assert.False(approval.Called);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task ReportsRamUsageLocallyWithoutAi()
    {
        var router = new LocalCommandRouter(systemInfo: new FakeSystemInfo(new MemoryStatus(16UL * 1024 * 1024 * 1024, 6UL * 1024 * 1024 * 1024)));
        var result = await router.TryRunAsync("show my RAM usage");
        Assert.True(result.Handled);
        Assert.Equal("Memory in use: 10.0 GB of 16.0 GB (63%).", result.Response);
    }

    [Fact]
    public async Task ReportsCpuMemoryAndNetworkStatusLocally()
    {
        var router = new LocalCommandRouter(systemInfo: new FakeSystemInfo(
            new MemoryStatus(16UL * 1024 * 1024 * 1024, 6UL * 1024 * 1024 * 1024),
            new SystemSnapshot(27.4, 2UL * 1024 * 1024 * 1024, 512UL * 1024 * 1024, 20)));
        var result = await router.TryRunAsync("show system status");
        Assert.True(result.Handled);
        Assert.Contains("CPU 27%", result.Response);
        Assert.Contains("20 logical processors", result.Response);
        Assert.Contains("RAM 10.0/16.0 GB", result.Response);
        Assert.Contains("received 2.00 GB, sent 0.50 GB", result.Response);
        Assert.Contains("GPU utilization is not available", result.Response);
    }

    [Fact]
    public async Task FindsNewestPdfOnLocalFoldersWithoutAi()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-search-test-{Guid.NewGuid():N}");
        var nested = Directory.CreateDirectory(Path.Combine(root, "class notes")).FullName;
        try
        {
            var older = Path.Combine(root, "old.pdf");
            var newer = Path.Combine(nested, "latest.pdf");
            await File.WriteAllTextAsync(older, "older");
            await File.WriteAllTextAsync(newer, "newer");
            await File.WriteAllTextAsync(Path.Combine(root, "ignore.txt"), "not a pdf");
            File.SetLastWriteTime(older, DateTime.Now.AddDays(-2));
            File.SetLastWriteTime(newer, DateTime.Now);

            var router = new LocalCommandRouter(fileSearch: new WindowsFileSearch([root]));
            var result = await router.TryRunAsync("find my latest PDF");
            Assert.True(result.Handled);
            Assert.Contains(newer, result.Response);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
            Directory.Delete(nested);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task CreatesTextFileOnlyAfterApprovalAndReadsItLocally()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var approval = new FakeApproval(true);
            var router = new LocalCommandRouter(approval, new DesktopFileSystem(root));
            var created = await router.TryRunAsync("create a text file named Notes.txt on my desktop with content: Remember the lab meeting");
            Assert.Contains("Created the text file", created.Response);
            Assert.Contains("Remember the lab meeting", approval.Details);
            var read = await router.TryRunAsync("read file Notes.txt on my desktop");
            Assert.Equal("Remember the lab meeting", read.Response);
        }
        finally
        {
            var file = Path.Combine(root, "Notes.txt");
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task DeclinedTextFileCreationLeavesNoFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var router = new LocalCommandRouter(new FakeApproval(false), new DesktopFileSystem(root));
            var result = await router.TryRunAsync("create a text file named Notes.txt on my desktop with content: Do not create me");
            Assert.Equal("Canceled. No file was created.", result.Response);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }

    [Theory]
    [InlineData("open VS Code", "code", "Opened Visual Studio Code.")]
    [InlineData("open Visual Studio", "devenv.exe", "Opened Visual Studio.")]
    public async Task LaunchesOnlyAllowlistedDevelopmentTools(string text, string expectedCommand, string expectedResponse)
    {
        var launcher = new FakeApplicationLauncher();
        var router = new LocalCommandRouter(applicationLauncher: launcher);
        var result = await router.TryRunAsync(text);
        Assert.True(result.Handled);
        Assert.Equal(expectedCommand, launcher.LastCommand);
        Assert.Equal(expectedResponse, result.Response);
    }

    private sealed class FakeApproval(bool approved) : IActionApproval
    {
        public bool Called { get; private set; }
        public string Details { get; private set; } = string.Empty;
        public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default)
        {
            Called = true;
            Details = details;
            return Task.FromResult(approved);
        }
    }

    private sealed class FakeSystemInfo(MemoryStatus memory, SystemSnapshot? snapshot = null) : ISystemInfoProvider
    {
        public MemoryStatus GetMemoryStatus() => memory;
        public Task<SystemSnapshot> GetSystemSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot ?? new SystemSnapshot(null, 0, 0, 1));
    }

    private sealed class FakeApplicationLauncher : IApplicationLauncher
    {
        public string? LastCommand { get; private set; }
        public void Start(string applicationCommand) => LastCommand = applicationCommand;
    }

    private sealed class FakeProjectTaskCoordinator : IProjectTaskCoordinator
    {
        public JarvisBackgroundTaskKind? QueuedKind { get; private set; }
        private readonly List<JarvisBackgroundTask> _tasks = [];
        public Task<JarvisBackgroundTask> QueueAsync(JarvisBackgroundTaskKind kind, CancellationToken cancellationToken = default)
        {
            QueuedKind = kind;
            var now = DateTimeOffset.UtcNow;
            var task = new JarvisBackgroundTask("1234abcd", kind, JarvisBackgroundTaskState.Queued, 0, "Queued", null, now, now);
            _tasks.Add(task);
            return Task.FromResult(task);
        }
        public Task<IReadOnlyList<JarvisBackgroundTask>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<JarvisBackgroundTask>>(_tasks.Take(limit).ToArray());
        public Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
