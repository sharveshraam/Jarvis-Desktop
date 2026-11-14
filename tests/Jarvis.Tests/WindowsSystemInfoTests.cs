using Jarvis.Windows;
using Xunit;

namespace Jarvis.Tests;

public sealed class WindowsSystemInfoTests
{
    [Fact]
    public async Task ReadsWindowsSystemSnapshotAndHandlesUnavailableGpuCounters()
    {
        var provider = new WindowsSystemInfo();

        var memory = provider.GetMemoryStatus();
        var snapshot = await provider.GetSystemSnapshotAsync();

        Assert.True(memory.TotalBytes > 0);
        Assert.True(snapshot.ProcessorCount > 0);
        Assert.True(snapshot.CpuPercent is null or >= 0 and <= 100);
        Assert.True(snapshot.GpuBusiestEnginePercent is null or >= 0 and <= 100);
    }
}
