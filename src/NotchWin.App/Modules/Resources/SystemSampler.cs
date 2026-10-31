using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;

namespace NotchWin.App.Modules.Resources;

/// <summary>One reading of the machine's resources. Null means "not available here" (no battery on a desktop PC, no GPU counters…).</summary>
public sealed record SystemSnapshot(
    double Cpu,
    double? Gpu,
    double Memory,
    double? BatteryPercent,
    bool? OnAc,
    double DownBytesPerSec,
    double UpBytesPerSec,
    double FreeDiskGb,
    double? PowerWatts,
    int? ConnectedDevices);

/// <summary>
/// Samples CPU, GPU, memory, battery, network, disk and connected Bluetooth devices. CPU and network are
/// rates, so each call reports the average since the previous call. Only call it while the tab is visible.
/// </summary>
public sealed class SystemSampler
{
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _haveCpu;

    private long _lastRx, _lastTx;
    private DateTime _lastNetUtc;

    private PerformanceCounterCategory? _gpuCategory;
    private bool _gpuUnavailable;
    private readonly Dictionary<string, CounterSample> _gpuPrev = [];

    private int? _devices;
    private DateTime _devicesAtUtc = DateTime.MinValue;
    private bool _devicesRefreshing;

    /// <summary>Forget the previous reading, so the first sample after the tab opens isn't an average over minutes.</summary>
    public void Reset()
    {
        _haveCpu = false;
        _lastNetUtc = default;
        _gpuPrev.Clear();
    }

    public SystemSnapshot Sample()
    {
        var (battery, onAc) = ReadBattery();
        var (down, up) = ReadNetwork();
        return new SystemSnapshot(
            ReadCpu(), ReadGpu(), ReadMemory(), battery, onAc, down, up, ReadFreeDiskGb(), ReadPowerWatts(), RefreshDevices());
    }

    // ---------- CPU / memory ----------

    private double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var i = ToUlong(idle);
        var k = ToUlong(kernel); // kernel time already includes idle time
        var u = ToUlong(user);

        double result = 0;
        if (_haveCpu)
        {
            var total = (k - _lastKernel) + (u - _lastUser);
            var idleDelta = i - _lastIdle;
            result = total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
        }

        (_lastIdle, _lastKernel, _lastUser, _haveCpu) = (i, k, u, true);
        return result;
    }

    private static double ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
    }

    // ---------- GPU ----------

    private double? ReadGpu()
    {
        if (_gpuUnavailable)
        {
            return null;
        }

        try
        {
            _gpuCategory ??= new PerformanceCounterCategory("GPU Engine");
            var data = _gpuCategory.ReadCategory();
            if (!data.Contains("Utilization Percentage"))
            {
                _gpuUnavailable = true;
                return null;
            }

            // Like Task Manager: sum the engines of one type (3D, copy, video…) and report the busiest type.
            var byType = new Dictionary<string, double>();
            var seen = new HashSet<string>();
            foreach (System.Collections.DictionaryEntry entry in data["Utilization Percentage"])
            {
                var name = (string)entry.Key;
                var current = ((InstanceData)entry.Value!).Sample;
                seen.Add(name);
                if (_gpuPrev.TryGetValue(name, out var previous))
                {
                    var value = CounterSample.Calculate(previous, current);
                    var type = name.Contains("engtype_", StringComparison.Ordinal)
                        ? name[(name.LastIndexOf("engtype_", StringComparison.Ordinal) + 8)..]
                        : "other";
                    byType[type] = byType.GetValueOrDefault(type) + value;
                }

                _gpuPrev[name] = current;
            }

            foreach (var gone in _gpuPrev.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _gpuPrev.Remove(gone);
            }

            return byType.Count == 0 ? 0 : Math.Clamp(byType.Values.Max(), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _gpuUnavailable = true;
            return null;
        }
    }

    // ---------- battery / power ----------

    private static (double? Percent, bool? OnAc) ReadBattery()
    {
        if (!GetSystemPowerStatus(out var status) || (status.BatteryFlag & 128) != 0 || status.BatteryLifePercent == 255)
        {
            return (null, null); // no battery (a desktop PC)
        }

        return (status.BatteryLifePercent, status.AcLineStatus == 1);
    }

    private static double? ReadPowerWatts()
    {
        try
        {
            var report = Battery.AggregateBattery.GetReport();
            return report.ChargeRateInMilliwatts is { } mw ? Math.Abs(mw) / 1000.0 : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---------- network / disk ----------

    private (double Down, double Up) ReadNetwork()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
            return (0, 0);
        }

        var now = DateTime.UtcNow;
        double down = 0, up = 0;
        if (_lastNetUtc != default && now > _lastNetUtc)
        {
            var seconds = (now - _lastNetUtc).TotalSeconds;
            down = Math.Max(0, (rx - _lastRx) / seconds);
            up = Math.Max(0, (tx - _lastTx) / seconds);
        }

        (_lastRx, _lastTx, _lastNetUtc) = (rx, tx, now);
        return (down, up);
    }

    private static double ReadFreeDiskGb()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024 / 1024;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    // ---------- connected Bluetooth devices (refreshed in the background every 15 s) ----------

    private int? RefreshDevices()
    {
        if (!_devicesRefreshing && DateTime.UtcNow - _devicesAtUtc > TimeSpan.FromSeconds(15))
        {
            _devicesRefreshing = true;
            _devicesAtUtc = DateTime.UtcNow;
            _ = CountDevicesAsync();
        }

        return _devices;
    }

    private async Task CountDevicesAsync()
    {
        try
        {
            // AQS: paired Bluetooth devices (classic and LE) that are connected right now.
            var selector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";
            var found = await DeviceInformation.FindAllAsync(selector, null, DeviceInformationKind.AssociationEndpoint);
            _devices = found.Count;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            _devices = null;
        }
        finally
        {
            _devicesRefreshing = false;
        }
    }

    // ---------- Win32 ----------

    private static ulong ToUlong(FileTime t) => ((ulong)t.High << 32) | t.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
