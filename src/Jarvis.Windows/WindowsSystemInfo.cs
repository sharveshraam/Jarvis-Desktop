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
        using var gpuQuery = GpuCounterQuery.TryCreate();
        var before = GetCpuTimes();
        gpuQuery?.Collect();
        await Task.Delay(CpuSampleInterval, cancellationToken).ConfigureAwait(false);
        var after = GetCpuTimes();
        gpuQuery?.Collect();
        var gpuPercent = gpuQuery?.GetBusiestEnginePercent();
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

        return new(cpu, received, sent, Environment.ProcessorCount, gpuPercent);
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

    private sealed class GpuCounterQuery : IDisposable
    {
        private const uint PdhMoreData = 0x800007D2;
        private const uint PdhFormatDouble = 0x00000200;
        private IntPtr _query;
        private IntPtr _counter;

        private GpuCounterQuery(IntPtr query, IntPtr counter) => (_query, _counter) = (query, counter);

        public static GpuCounterQuery? TryCreate()
        {
            if (PdhOpenQueryW(IntPtr.Zero, UIntPtr.Zero, out var query) != 0) return null;
            if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out var counter) != 0)
            {
                PdhCloseQuery(query);
                return null;
            }
            return new(query, counter);
        }

        public void Collect() => PdhCollectQueryData(_query);

        public double? GetBusiestEnginePercent()
        {
            uint bufferSize = 0;
            uint itemCount = 0;
            var status = PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
            if (status != PdhMoreData || bufferSize == 0 || itemCount == 0) return null;
            var buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
            try
            {
                status = PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble, ref bufferSize, ref itemCount, buffer);
                if (status != 0) return null;
                var itemSize = Marshal.SizeOf<PdhCounterValueItem>();
                double? max = null;
                for (var index = 0; index < itemCount; index++)
                {
                    var item = Marshal.PtrToStructure<PdhCounterValueItem>(IntPtr.Add(buffer, checked((int)index * itemSize)));
                    if (item.Value.CStatus is not (0 or 1) || !double.IsFinite(item.Value.DoubleValue)) continue;
                    var value = Math.Clamp(item.Value.DoubleValue, 0, 100);
                    max = max is null ? value : Math.Max(max.Value, value);
                }
                return max;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            if (_query == IntPtr.Zero) return;
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhCounterValueItem { public IntPtr Name; public PdhCounterValue Value; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhCounterValue { public uint CStatus; public double DoubleValue; }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhOpenQueryW(IntPtr dataSource, UIntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, UIntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);
    }
}
