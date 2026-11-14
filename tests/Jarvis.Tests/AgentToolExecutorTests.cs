using System.Text.Json;
using Jarvis.Core;
using Jarvis.Windows;
using Xunit;

namespace Jarvis.Tests;

public sealed class AgentToolExecutorTests
{
    [Fact]
    public async Task ToolCreatedFolderUsesValidatedNameAndAsksForApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var approval = new FakeApproval(true);
            var router = new LocalCommandRouter(approval, new DesktopFileSystem(root));
            var tools = new LocalAgentToolExecutor(router);
            var response = await tools.ExecuteAsync(Call("create_desktop_folder", new { name = "on my desktop" }));

            Assert.True(response.GetProperty("success").GetBoolean());
            Assert.True(Directory.Exists(Path.Combine(root, "on my desktop")));
            Assert.Contains(Path.Combine(root, "on my desktop"), approval.Details);
        }
        finally
        {
            var folder = Path.Combine(root, "on my desktop");
            if (Directory.Exists(folder)) Directory.Delete(folder);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task ToolRejectsTraversalBeforeAskingOrChangingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var approval = new FakeApproval(true);
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(approval, new DesktopFileSystem(root)));
            var response = await tools.ExecuteAsync(Call("create_desktop_folder", new { name = ".." }));
            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.False(approval.Called);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task ToolCannotLaunchAnApplicationOutsideTheAllowlist()
    {
        var launcher = new FakeLauncher();
        var tools = new LocalAgentToolExecutor(new LocalCommandRouter(applicationLauncher: launcher));
        var response = await tools.ExecuteAsync(Call("open_application", new { application = "arbitrary command" }));
        Assert.False(response.GetProperty("success").GetBoolean());
        Assert.Null(launcher.LastCommand);
    }

    [Fact]
    public async Task CloudRequestedFileReadRequiresDisclosureApprovalBeforeContentIsReturned()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "private.txt");
        try
        {
            await File.WriteAllTextAsync(file, "synthetic private note");
            var approval = new FakeApproval(false);
            var tools = new LocalAgentToolExecutor(
                new LocalCommandRouter(files: new DesktopFileSystem(root)), approval, new DesktopFileSystem(root));
            var response = await tools.ExecuteAsync(Call("read_desktop_text_file", new { name = "private.txt" }));
            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.Contains("sent to Google Gemini", approval.Details);
            Assert.DoesNotContain("synthetic private note", response.GetProperty("result").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task LocalUserFileContentIsNotReadOrReturnedBeforeApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "lab-notes.txt");
        try
        {
            await File.WriteAllTextAsync(file, "synthetic private local notes");
            var approval = new FakeApproval(false);
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(), approval, userFiles: new SafeUserFiles([root]));

            var response = await tools.ExecuteAsync(Call("read_user_file", new { path = file }));

            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.Contains("sent to Google Gemini", approval.Details);
            Assert.Contains(file, approval.Details);
            Assert.DoesNotContain("synthetic private local notes", response.GetProperty("result").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task ApprovedLocalUserFileCanBeSharedOnlyAfterItIsInspected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "lab-notes.txt");
        try
        {
            await File.WriteAllTextAsync(file, "synthetic approved content");
            var approval = new FakeApproval(true);
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(), approval, userFiles: new SafeUserFiles([root]));

            var response = await tools.ExecuteAsync(Call("read_user_file", new { path = file }));

            Assert.True(response.GetProperty("success").GetBoolean());
            Assert.Contains("synthetic approved content", response.GetProperty("result").GetString());
            Assert.Contains("Size:", approval.Details);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task CloudRequestedFilenameSearchRequiresApprovalBeforePathsAreShared()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "synthetic-secret-lab.txt");
        try
        {
            await File.WriteAllTextAsync(file, "not sent");
            var approval = new FakeApproval(false);
            var router = new LocalCommandRouter(fileSearch: new WindowsFileSearch([root]));
            var tools = new LocalAgentToolExecutor(router, approval);

            var response = await tools.ExecuteAsync(Call("search_user_files", new { query = "secret-lab" }));

            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.Contains("names and paths will be sent", approval.Details);
            Assert.Contains(file, approval.Details);
            Assert.DoesNotContain(file, response.GetProperty("result").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task ApprovedCloudRequestedFilenameSearchReturnsLocalMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "lecture-notes.txt");
        try
        {
            await File.WriteAllTextAsync(file, "not uploaded as part of a filename search");
            var approval = new FakeApproval(true);
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(fileSearch: new WindowsFileSearch([root])), approval);

            var response = await tools.ExecuteAsync(Call("search_user_files", new { query = "lecture-notes" }));

            Assert.True(response.GetProperty("success").GetBoolean());
            Assert.Contains(file, response.GetProperty("result").GetString());
            Assert.DoesNotContain("not uploaded as part of a filename search", response.GetProperty("result").GetString());
            Assert.Contains("names and paths will be sent", approval.Details);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task LocalFileOutsideAllowedFoldersIsRejectedBeforeApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "outside.txt");
        try
        {
            await File.WriteAllTextAsync(file, "private");
            var approval = new FakeApproval(true);
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(), approval, userFiles: new SafeUserFiles([Path.Combine(root, "allowed")]));

            var response = await tools.ExecuteAsync(Call("read_user_file", new { path = file }));

            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.False(approval.Called);
            Assert.DoesNotContain("private", response.GetProperty("result").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task ChangedLocalFileRequiresFreshApprovalAndIsNotShared()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-agent-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "notes.txt");
        try
        {
            await File.WriteAllTextAsync(file, "original approved content");
            var approval = new FakeApproval(true, () =>
            {
                File.WriteAllText(file, "replacement content");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
            });
            var tools = new LocalAgentToolExecutor(new LocalCommandRouter(), approval, userFiles: new SafeUserFiles([root]));

            var response = await tools.ExecuteAsync(Call("read_user_file", new { path = file }));

            Assert.False(response.GetProperty("success").GetBoolean());
            Assert.Contains("file changed after confirmation", response.GetProperty("result").GetString());
            Assert.DoesNotContain("replacement content", response.GetProperty("result").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(root);
        }
    }

    private static AiFunctionCall Call(string name, object args)
    {
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(args));
        using var content = JsonDocument.Parse("{\"role\":\"model\",\"parts\":[]}");
        return new AiFunctionCall(name, arguments.RootElement.Clone(), content.RootElement.Clone());
    }

    private sealed class FakeApproval(bool approved, Action? onConfirm = null) : IActionApproval
    {
        public bool Called { get; private set; }
        public string Details { get; private set; } = string.Empty;
        public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default)
        {
            Called = true;
            Details = details;
            onConfirm?.Invoke();
            return Task.FromResult(approved);
        }
    }

    private sealed class FakeLauncher : IApplicationLauncher
    {
        public string? LastCommand { get; private set; }
        public void Start(string applicationCommand) => LastCommand = applicationCommand;
    }
}
