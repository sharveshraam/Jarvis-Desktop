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
}
