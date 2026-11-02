using FullWidget.Core;
using Microsoft.Win32;

namespace FullWidget.Shell;

/// "Start with Windows" via the per-user Run key (no admin rights needed).
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FullWidget";
    /// The entry's name before the app was renamed from DeskWidgets.
    const string LegacyValueName = "DeskWidgets";

    /// The --data-dir this copy runs with (test profiles), so autostart opens the same data.
    public static string? DataDirArg { get; set; }

    static string Command =>
        $"\"{Environment.ProcessPath}\"" + (DataDirArg != null ? $" \"--data-dir={DataDirArg}\"" : "");

    static string? Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static bool Enabled
    {
        get => Current() is string s && s.Equals(Command, StringComparison.OrdinalIgnoreCase);
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// The folder was moved (portable copy): an entry pointing to an exe that no longer exists is
    /// moved over to this one, so "start with Windows" keeps working.
    public static void Repair()
    {
        // Renamed from DeskWidgets: an old entry becomes ours (pointing to this exe).
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            if (key?.GetValue(LegacyValueName) is string)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                if (Current() == null) Enabled = true;
                Log.Info("Autostart entry renamed from DeskWidgets");
                return;
            }
        if (Current() is not string s || Enabled) return;
        var exe = s.StartsWith('"') ? s[1..s.IndexOf('"', 1)] : s.Split(' ')[0];
        if (File.Exists(exe)) return; // another copy that still exists: leave it alone
        Enabled = true;
        Log.Info($"Autostart moved from {exe} to {Environment.ProcessPath}");
    }
}
