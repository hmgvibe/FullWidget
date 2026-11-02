using System.Diagnostics;
using System.Runtime.InteropServices;
using FullWidget.Bridge;
using FullWidget.Core;

namespace FullWidget.Shell;

/// Lock, sleep, restart, shut down and sign out, and the Recycle Bin. Only built-in widgets reach
/// these (see WidgetBridge). With FULLWIDGET_DRYRUN=1 the actions are logged instead of done, for
/// testing on a PC that should stay on and keep its deleted files.
static class PowerActions
{
    public static bool DryRun => Environment.GetEnvironmentVariable("FULLWIDGET_DRYRUN") == "1";

    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvents);
    [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level, IntPtr input, int inputLength, out SystemPowerCapabilities output, int outputLength);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    // SYSTEM_POWER_CAPABILITIES is 76 bytes; only AoAc ("always on, always connected" =
    // modern standby), the 21st byte, is read.
    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerCapabilities
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 76)] public byte[] Bytes;
    }

    public static readonly string[] Actions = { "lock", "sleep", "restart", "shutdown", "signout" };

    public static void Do(string action)
    {
        if (!Actions.Contains(action)) throw new BridgeException($"unknown power action '{action}'");
        Log.Info($"Power: {action}{(DryRun ? " (dry run, not done)" : "")}");
        if (DryRun) return;
        switch (action)
        {
            case "lock": LockWorkStation(); break;
            case "sleep": Sleep(); break;
            case "restart": Run("shutdown.exe", "/r /t 0"); break;
            case "shutdown": Run("shutdown.exe", "/s /hybrid /t 0", fallback: "/s /t 0"); break;
            case "signout": ExitWindowsEx(0 /* EWX_LOGOFF */, 0); break;
        }
    }

    /// PCs with modern standby sleep when the screen turns off; on others SetSuspendState
    /// would hibernate them instead, so they get the screen-off route.
    static void Sleep()
    {
        bool modernStandby = false;
        try
        {
            if (CallNtPowerInformation(4 /* SystemPowerCapabilities */, IntPtr.Zero, 0, out var caps, Marshal.SizeOf<SystemPowerCapabilities>()) == 0)
                modernStandby = caps.Bytes[20] != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        if (modernStandby) SendMessage(new IntPtr(0xffff) /* HWND_BROADCAST */, 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF170) /* SC_MONITORPOWER */, new IntPtr(2));
        else SetSuspendState(false, false, false);
    }

    static void Run(string exe, string args, string? fallback = null)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
        if (fallback == null || p == null) return;
        // /hybrid (fast startup) is refused when fast startup is off; then shut down normally.
        Task.Run(() =>
        {
            if (p.WaitForExit(5000) && p.ExitCode != 0)
                Process.Start(new ProcessStartInfo(exe, fallback) { CreateNoWindow = true, UseShellExecute = false });
        });
    }

    // ── Recycle Bin ──

    [StructLayout(LayoutKind.Sequential)]
    struct ShQueryRbInfo { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string? root, ref ShQueryRbInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    /// Items and bytes in the Recycle Bins of all drives.
    public static object RecycleBin()
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        int hr = SHQueryRecycleBin(null, ref info);
        if (hr != 0) throw new BridgeException($"could not read the Recycle Bin (0x{hr:x8})");
        return new { items = info.i64NumItems, size = info.i64Size };
    }

    /// Deletes everything in the Recycle Bin for good; the widget asks the user twice first.
    public static async Task<object> EmptyRecycleBinAsync()
    {
        Log.Info($"Recycle Bin: empty{(DryRun ? " (dry run, not done)" : "")}");
        if (!DryRun)
        {
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND; can take a while for big bins
            int hr = await Task.Run(() => SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4));
            if (hr != 0 && hr != unchecked((int)0x8000FFFF)) throw new BridgeException($"could not empty the Recycle Bin (0x{hr:x8})");
        }
        return RecycleBin();
    }

    public static void OpenRecycleBin() =>
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = false });
}
