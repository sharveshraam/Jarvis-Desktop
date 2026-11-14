using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsSystemInfo : ISystemInfoProvider
{
    private static readonly TimeSpan CpuSampleInterval = TimeSpan.FromMilliseconds(300);

    public MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException($"Windows could not read memory status (error {Marshal.GetLastWin32Error()}).");
        return new(status.TotalPhys, status.AvailPhys);
    }

    public async Task<SystemSnapshot> GetSystemSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var before = GetCpuTimes();
        await Task.Delay(CpuSampleInterval, cancellationToken).ConfigureAwait(false);
        var after = GetCpuTimes();
        var total = (after.Kernel - before.Kernel) + (after.User - before.User);
        var idle = after.Idle - before.Idle;
        var cpu = total <= 0 ? (double?)null : Math.Clamp((total - idle) * 100d / total, 0, 100);

        ulong received = 0;
        ulong sent = 0;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback || network.OperationalStatus != OperationalStatus.Up)
                continue;
            try
            {
                var stats = network.GetIPv4Statistics();
                received = SaturatingAdd(received, (ulong)Math.Max(0, stats.BytesReceived));
                sent = SaturatingAdd(sent, (ulong)Math.Max(0, stats.BytesSent));
            }
            catch (NetworkInformationException) { }
        }

        return new(cpu, received, sent, Environment.ProcessorCount);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static (ulong Idle, ulong Kernel, ulong User) GetCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            throw new InvalidOperationException($"Windows could not read CPU status (error {Marshal.GetLastWin32Error()}).");
        return (ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
    }

    private static ulong ToUInt64(SystemFileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemFileTime { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out SystemFileTime idle, out SystemFileTime kernel, out SystemFileTime user);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
