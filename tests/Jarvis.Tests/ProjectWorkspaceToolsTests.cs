using Jarvis.Core;
using Jarvis.Windows;
using Xunit;

namespace Jarvis.Tests;

public sealed class ProjectWorkspaceToolsTests
{
    [Fact]
    public async Task ReadRequiresConsentBeforeReturningSource()
    {
        var root = NewRoot(); var file = Path.Combine(root, "Program.cs"); await File.WriteAllTextAsync(file, "private sentinel");
        try
        {
            var approval = new Approval(false); var tools = new ProjectWorkspaceTools(new Preferences(root), approval);
            var result = await tools.ReadAsync("Program.cs");
            Assert.Contains("sent to Google Gemini", approval.Details);
            Assert.DoesNotContain("private sentinel", result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WriteShowsPreviewAndChangesOnlyAfterApproval()
    {
        var root = NewRoot(); var path = Path.Combine(root, "App.cs"); await File.WriteAllTextAsync(path, "old");
        try
        {
            var approval = new Approval(false); var tools = new ProjectWorkspaceTools(new Preferences(root), approval);
            await tools.WriteAsync("App.cs", "new");
            Assert.Equal("old", await File.ReadAllTextAsync(path));
            Assert.Contains("new", approval.Details); Assert.Contains("old", approval.Details);
            approval.Approved = true;
            await tools.WriteAsync("App.cs", "new");
            Assert.Equal("new", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("..\\escape.cs")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("obj\\generated.cs")]
    [InlineData("notes.exe")]
    public async Task RejectsOutsideOrExcludedPaths(string path)
    {
        var root = NewRoot();
        try
        {
            var tools = new ProjectWorkspaceTools(new Preferences(root), new Approval(true));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => tools.ReadAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() { var path = Path.Combine(Path.GetTempPath(), "jarvis-project-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private sealed class Preferences(string path) : IUserPreferencesStore
    {
        public Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserPreferences("Ctrl+Shift+F12", false, ProjectWorkspacePath: path));
        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Approval(bool approved) : IActionApproval
    {
        public bool Approved { get; set; } = approved;
        public string Details { get; private set; } = "";
        public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default) { Details = details; return Task.FromResult(Approved); }
    }
}
