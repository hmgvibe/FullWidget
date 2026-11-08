using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using FullWidget.Bridge;
using static FullWidget.Native.Win32;

namespace FullWidget.Providers;

/// Facts about this PC and its network that widgets ask for now and then (not a stream like
/// SystemMonitor): Windows version, processor, memory, graphics, uptime, network adapters, ping.
static class SystemInfo
{
    // ── this PC ──

    /// Mostly fixed facts, read once; the uptime part is computed on each call.
    public static object Info()
    {
        var fixedPart = Fixed.Value;
        long uptimeMs = Environment.TickCount64;
        return new
        {
            fixedPart.os,
            fixedPart.cpu,
            fixedPart.memory,
            fixedPart.gpus,
            computer = Environment.MachineName,
            screens = Screen.AllScreens.Select(s => new { width = s.Bounds.Width, height = s.Bounds.Height, primary = s.Primary }),
            bootTime = DateTimeOffset.Now.AddMilliseconds(-uptimeMs).ToUnixTimeMilliseconds(),
            uptime = uptimeMs,
        };
    }

    sealed record FixedInfo(object os, object cpu, object memory, string[] gpus);

    static readonly Lazy<FixedInfo> Fixed = new(() => new FixedInfo(Os(), Cpu(), Memory(), Gpus()));

    static object Os()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string product = k?.GetValue("ProductName") as string ?? "Windows";
        string build = k?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
        int ubr = k?.GetValue("UBR") is int u ? u : 0;
        // The registry still says "Windows 10" on Windows 11; the build number tells them apart.
        if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return new
        {
            name = product,
            edition = k?.GetValue("EditionID") as string ?? "",
            version = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string ?? "",
            build = ubr > 0 ? $"{build}.{ubr}" : build,
            arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        };
    }

    static object Cpu()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string name = (k?.GetValue("ProcessorNameString") as string ?? "").Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ");
        return new { name, cores = PhysicalCores(), threads = Environment.ProcessorCount };
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LogicalProcessorInfo { public UIntPtr Mask; public int Relationship; public long Reserved0, Reserved1; }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref int length);

    static int PhysicalCores()
    {
        int len = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref len);
        if (len <= 0) return Environment.ProcessorCount;
        var buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetLogicalProcessorInformation(buf, ref len)) return Environment.ProcessorCount;
            int size = Marshal.SizeOf<LogicalProcessorInfo>(), cores = 0;
            for (int off = 0; off + size <= len; off += size)
                if (Marshal.PtrToStructure<LogicalProcessorInfo>(buf + off).Relationship == 0) cores++; // RelationProcessorCore
            return cores > 0 ? cores : Environment.ProcessorCount;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [DllImport("kernel32.dll")]
    static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);

    static object Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        ulong installed = GetPhysicallyInstalledSystemMemory(out ulong kb) ? kb * 1024 : m.ullTotalPhys;
        return new { installed, usable = m.ullTotalPhys };
    }

    /// Every graphics adapter Windows knows, including a laptop's second GPU that drives no screen.
    static string[] Gpus()
    {
        var names = new List<string>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var sub in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!int.TryParse(sub, out _)) continue;
                try
                {
                    using var k = cls!.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is string d && !string.IsNullOrWhiteSpace(d)) names.Add(d.Trim());
                }
                catch (System.Security.SecurityException) { /* some entries are private */ }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return names
            .Where(n => !n.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Indirect Display", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // ── network ──

    static readonly string[] VirtualHints = { "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "vpn", "tap-", "wintun", "tunnel", "clash", "mihomo", "sing-box", "wireguard", "tailscale", "zerotier", "npcap", "loopback", "bluetooth" };

    /// Virtual adapters (VMs, VPNs, proxy TUN adapters…) carry traffic that also crosses a real
    /// adapter, so counting both would add the same bytes twice.
    public static bool IsVirtual(NetworkInterface n) =>
        n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
        VirtualHints.Any(h => n.Description.Contains(h, StringComparison.OrdinalIgnoreCase) || n.Name.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// Connected adapters, the one carrying internet traffic (has a gateway, not virtual) first.
    public static object Adapters()
    {
        var list = new List<(bool main, object info)>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up) continue;
            if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties p;
            try { p = n.GetIPProperties(); } catch (NetworkInformationException) { continue; }
            string desc = n.Description;
            bool isVirtual = IsVirtual(n);
            var gateways = p.GatewayAddresses.Select(g => g.Address).Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1) // the familiar 192.168.x.x first
                .Select(a => a.ToString()).ToArray();
            string type = n.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "wifi",
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "ethernet",
                NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "cellular",
                _ => "other",
            };
            var info = new
            {
                name = n.Name,
                description = desc,
                type,
                @virtual = isVirtual,
                speed = n.Speed > 0 ? n.Speed : 0,
                ipv4 = p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToArray(),
                ipv6 = p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString()).ToArray(),
                gateways,
                dns = p.DnsAddresses.Select(a => a.ToString()).Where(a => !a.StartsWith("fec0:", StringComparison.OrdinalIgnoreCase)).ToArray(),
            };
            list.Add((gateways.Length > 0 && !isVirtual, info));
        }
        return new
        {
            online = NetworkInterface.GetIsNetworkAvailable(),
            adapters = list.OrderByDescending(x => x.main).Select(x => x.info).ToArray(),
        };
    }

    // ── ping ──

    static readonly Dictionary<string, long> lastPing = new();
    static readonly System.Text.RegularExpressions.Regex HostPattern = new(@"^[A-Za-z0-9]([A-Za-z0-9.\-:]{0,251}[A-Za-z0-9])?$");

    /// One echo request. Each widget instance may ping at most once a second.
    public static async Task<object> PingAsync(string instance, string host, int timeoutMs)
    {
        host = host.Trim();
        if (!HostPattern.IsMatch(host)) throw new BridgeException($"invalid host '{host}'");
        long now = Environment.TickCount64;
        lock (lastPing)
        {
            if (lastPing.TryGetValue(instance, out long last) && now - last < 900) throw new BridgeException("ping at most once a second");
            lastPing[instance] = now;
        }
        try
        {
            using var ping = new Ping();
            var r = await ping.SendPingAsync(host, Math.Clamp(timeoutMs, 200, 5000));
            return new { ok = r.Status == IPStatus.Success, ms = r.Status == IPStatus.Success ? r.RoundtripTime : (long?)null, status = r.Status.ToString(), address = r.Address?.ToString() };
        }
        catch (PingException ex)
        {
            // usually the name could not be resolved: no network, or a typo
            return new { ok = false, ms = (long?)null, status = ex.InnerException is SocketException ? "NameNotResolved" : "Error", address = (string?)null };
        }
    }
}
