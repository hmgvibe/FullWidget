using System.Net.NetworkInformation;
using FullWidget.Bridge;
using FullWidget.Windows;
using static FullWidget.Native.Win32;

namespace FullWidget.Providers;

/// Samples system stats once and fans them out to every subscribed widget.
/// Runs only while at least one widget is subscribed. CPU, memory and network are read together,
/// at most once a second, and the readings are also kept for 15 minutes, so a graph added later
/// starts with what happened before it.
sealed class SystemMonitor : IDisposable
{
    public static readonly string[] Topics = { "cpu", "memory", "network", "disk", "battery" };

    sealed class Sub
    {
        public HashSet<string> Topics = new();
        public int Interval;
        public long Next;
    }

    readonly Dictionary<WidgetWindow, Sub> subs = new();
    readonly System.Windows.Forms.Timer timer = new();

    // the shared once-a-second reading of cpu, memory and network, and its history
    static readonly HashSet<string> CoreTopics = new() { "cpu", "memory", "network" };
    const int HistorySeconds = 15 * 60;
    readonly Queue<double[]> history = new(); // [unix ms, cpu %, memory %, down B/s, up B/s]
    Dictionary<string, object?>? core;
    long coreAt = long.MinValue;

    ulong lastIdle, lastTotal;
    Dictionary<string, (long rx, long tx)> lastNet = new();
    long lastNetTick;
    object? disks;
    long disksNext;

    public SystemMonitor()
    {
        timer.Tick += (_, _) => Tick();
        SampleCpu(); // baseline, so the first real sample has something to compare with
        SampleNetwork();
    }

    public Dictionary<string, object?> Subscribe(WidgetWindow w, IEnumerable<string> topics, int intervalMs)
    {
        var set = topics.Where(t => Topics.Contains(t)).ToHashSet();
        if (set.Count == 0) throw new BridgeException($"no valid topics; use: {string.Join(", ", Topics)}");
        int interval = Math.Clamp(intervalMs, 500, 60_000);
        subs[w] = new Sub { Topics = set, Interval = interval, Next = Environment.TickCount64 + interval };
        Retime();
        return Sample(set);
    }

    public void Unsubscribe(WidgetWindow w)
    {
        if (subs.Remove(w)) Retime();
    }

    void Retime()
    {
        if (subs.Count == 0) { timer.Stop(); return; }
        timer.Interval = subs.Values.Min(s => s.Interval) < 1000 ? 250 : 500;
        timer.Start();
    }

    void Tick()
    {
        long now = Environment.TickCount64;
        // hidden widgets (hide all, full-screen pause) are skipped and cost nothing
        var due = subs.Where(kv => now >= kv.Value.Next && !kv.Key.IsHidden).ToList();
        if (due.Count == 0) return;
        var snap = Sample(due.SelectMany(d => d.Value.Topics).ToHashSet());
        foreach (var (w, s) in due)
        {
            s.Next = now + s.Interval;
            w.Send("system", snap.Where(kv => s.Topics.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        }
    }

    Dictionary<string, object?> Sample(HashSet<string> topics)
    {
        var d = new Dictionary<string, object?>();
        if (topics.Overlaps(CoreTopics))
            foreach (var (k, v) in Core()) if (topics.Contains(k)) d[k] = v;
        if (topics.Contains("disk")) d["disk"] = SampleDisks();
        if (topics.Contains("battery")) d["battery"] = SampleBattery();
        return d;
    }

    /// CPU, memory and network, read afresh at most about once a second and recorded.
    Dictionary<string, object?> Core()
    {
        long now = Environment.TickCount64;
        if (core != null && now - coreAt < 900) return core;
        double cpu = SampleCpu();
        var (memory, memUsage) = SampleMemory();
        var (network, down, up) = SampleNetwork();
        core = new() { ["cpu"] = new { usage = cpu, cores = Environment.ProcessorCount }, ["memory"] = memory, ["network"] = network };
        coreAt = now;
        long unix = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        history.Enqueue(new[] { unix, cpu, memUsage, down, up });
        while (history.Count > 0 && unix - history.Peek()[0] > HistorySeconds * 1000) history.Dequeue();
        return core;
    }

    /// The last 15 minutes of readings (only while some widget was showing system figures).
    public object History() => new { now = DateTimeOffset.Now.ToUnixTimeMilliseconds(), points = history.ToArray() };

    double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong total = kernel.Value + user.Value; // kernel time includes idle time
        ulong dTotal = total - lastTotal, dIdle = idle.Value - lastIdle;
        lastTotal = total;
        lastIdle = idle.Value;
        return dTotal == 0 ? 0 : Math.Round(Math.Clamp(100.0 * (1 - (double)dIdle / dTotal), 0, 100), 1);
    }

    static (object info, double usage) SampleMemory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double usage = m.ullTotalPhys == 0 ? 0 : Math.Round(100.0 * (m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys, 1);
        return (new
        {
            usage,
            total = m.ullTotalPhys,
            used = m.ullTotalPhys - m.ullAvailPhys,
            available = m.ullAvailPhys,
        }, usage);
    }

    /// Download and upload speed of the real adapters. Each adapter is measured on its own: one
    /// that just appeared (Wi-Fi joining, a VPN connecting) only counts from its second reading,
    /// so its bytes-since-boot never show up as one second's traffic.
    (object info, double down, double up) SampleNetwork()
    {
        long dRx = 0, dTx = 0, rx = 0, tx = 0;
        var seen = new Dictionary<string, (long rx, long tx)>();
        var same = new HashSet<(string mac, long rx, long tx)>();
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || SystemInfo.IsFilter(n) || SystemInfo.IsVirtual(n)) continue;
                var s = n.GetIPStatistics();
                // last line of defence against one adapter seen twice: same hardware address, same counts
                if (s.BytesReceived > 0 && !same.Add((n.GetPhysicalAddress().ToString(), s.BytesReceived, s.BytesSent))) continue;
                seen[n.Id] = (s.BytesReceived, s.BytesSent);
                rx += s.BytesReceived;
                tx += s.BytesSent;
                if (lastNet.TryGetValue(n.Id, out var was))
                {
                    dRx += Math.Max(0, s.BytesReceived - was.rx); // counters can reset
                    dTx += Math.Max(0, s.BytesSent - was.tx);
                }
            }
        }
        catch { /* adapters can vanish mid-enumeration */ }

        long now = Environment.TickCount64;
        double secs = (now - lastNetTick) / 1000.0;
        bool first = lastNetTick == 0 || secs <= 0;
        double down = first ? 0 : Math.Round(dRx / secs), up = first ? 0 : Math.Round(dTx / secs);
        lastNet = seen; lastNetTick = now;
        return (new { down, up, totalDown = rx, totalUp = tx }, down, up);
    }

    object SampleDisks()
    {
        long now = Environment.TickCount64;
        if (disks != null && now < disksNext) return disks;
        disks = DriveInfo.GetDrives()
            .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)
            .Select(d => new
            {
                name = d.Name.TrimEnd('\\'),
                label = d.VolumeLabel,
                type = d.DriveType == DriveType.Removable ? "removable" : "fixed",
                total = d.TotalSize,
                free = d.AvailableFreeSpace,
                usage = d.TotalSize == 0 ? 0 : Math.Round(100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize, 1),
            })
            .ToList();
        disksNext = now + 15_000;
        return disks;
    }

    static object SampleBattery()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryFlag == 255)
            return new { present = false, percent = (int?)null, charging = false, pluggedIn = s.ACLineStatus == 1, secondsLeft = (int?)null, saver = s.SystemStatusFlag == 1 };
        return new
        {
            present = true,
            percent = s.BatteryLifePercent == 255 ? (int?)null : s.BatteryLifePercent,
            charging = (s.BatteryFlag & 8) != 0,
            pluggedIn = s.ACLineStatus == 1,
            secondsLeft = s.BatteryLifeTime < 0 ? (int?)null : s.BatteryLifeTime,
            saver = s.SystemStatusFlag == 1, // Windows' battery saver is on
        };
    }

    public void Dispose() => timer.Dispose();
}
