using System.Runtime.InteropServices;
using FullWidget.Core;
using Microsoft.Web.WebView2.Core;

namespace FullWidget.Shell;

/// Hands memory that FullWidget and its WebView2 processes no longer touch back to Windows.
///
/// Starting up (loading pages, building the first frames, JIT) touches far more memory than the
/// widgets need afterwards. Measured with 13 widgets: about 240 MB in Task Manager after start,
/// of which the widgets keep using about 125 MB; the rest just sits there until Windows runs short
/// and trims it anyway. So once things have settled, the working sets are emptied: pages still
/// in use come straight back (from the standby list, not the disk), the rest stays out.
///
/// It runs once after start, and again after something big came and went (the control center
/// or the guide closed, widgets added or removed). Not on a timer: what is in use would only be
/// taken away and fetched back again.
sealed class MemoryTrim(Func<CoreWebView2Environment?> env)
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    bool hooked;

    /// Trims in 30 seconds, or 30 seconds after the last call if more follow.
    public void Soon()
    {
        if (!hooked) { hooked = true; timer.Tick += (_, _) => { timer.Stop(); Now(); }; }
        timer.Stop();
        timer.Start();
    }

    public void Now()
    {
        var ids = new List<int> { Environment.ProcessId };
        try
        {
            if (env() is { } e) ids.AddRange(e.GetProcessInfos().Select(p => p.ProcessId));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { /* the browser is gone */ }
        int done = 0;
        foreach (int id in ids.Distinct())
        {
            IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, id);
            if (h == IntPtr.Zero) continue;
            if (EmptyWorkingSet(h)) done++;
            CloseHandle(h);
        }
        Log.Info($"Memory: working sets of {done}/{ids.Count} processes handed back");
    }

    const int PROCESS_SET_QUOTA = 0x0100, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
