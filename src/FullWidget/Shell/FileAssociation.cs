using System.Runtime.InteropServices;
using FullWidget.Core;
using Microsoft.Win32;

namespace FullWidget.Shell;

/// Double-clicking a .fwz file opens it with FullWidget. Per-user (HKCU), no admin rights needed.
/// .dwz files (from before the rename) open the same way.
static class FileAssociation
{
    const string ProgId = "FullWidget.Package";
    const string LegacyProgId = "DeskWidgets.Package";
    static readonly string[] Extensions = { Packages.Extension, Packages.LegacyExtension };
    const string Classes = @"Software\Classes\";

    static string Command(string? dataDirArg) =>
        $"\"{Environment.ProcessPath}\"" + (dataDirArg != null ? $" \"--data-dir={dataDirArg}\"" : "") + " \"%1\"";

    /// Whether .fwz files currently open with this copy of FullWidget (and this data folder).
    public static bool IsRegistered(string? dataDirArg)
    {
        using var ext = Registry.CurrentUser.OpenSubKey(Classes + Packages.Extension);
        using var cmd = Registry.CurrentUser.OpenSubKey(Classes + ProgId + @"\shell\open\command");
        return ext?.GetValue(null) as string == ProgId && cmd?.GetValue(null) as string == Command(dataDirArg);
    }

    public static void Register(string? dataDirArg)
    {
        if (IsRegistered(dataDirArg)) return;
        try
        {
            foreach (var e in Extensions)
                using (var ext = Registry.CurrentUser.CreateSubKey(Classes + e))
                {
                    // .dwz only if nothing else has it, or the old DeskWidgets did
                    if (e == Packages.LegacyExtension && ext.GetValue(null) is string other && other != LegacyProgId && other != ProgId) continue;
                    ext.SetValue(null, ProgId);
                    ext.SetValue("Content Type", "application/zip");
                }
            Registry.CurrentUser.DeleteSubKeyTree(Classes + LegacyProgId, throwOnMissingSubKey: false);
            using (var prog = Registry.CurrentUser.CreateSubKey(Classes + ProgId))
            {
                prog.SetValue(null, Strings.FwzTypeName);
                using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{Environment.ProcessPath}\",0");
                using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, Command(dataDirArg));
            }
            Changed();
            Log.Info("Registered .fwz file type");
        }
        catch (Exception ex) { Log.Warn($"Could not register .fwz: {ex.Message}"); }
    }

    /// Removes the association, but only if it still points to FullWidget.
    public static void Unregister()
    {
        try
        {
            bool any = false;
            foreach (var e in Extensions)
            {
                using (var ext = Registry.CurrentUser.OpenSubKey(Classes + e))
                    if (ext?.GetValue(null) as string is not (ProgId or LegacyProgId)) continue;
                Registry.CurrentUser.DeleteSubKeyTree(Classes + e, throwOnMissingSubKey: false);
                any = true;
            }
            if (!any) return;
            Registry.CurrentUser.DeleteSubKeyTree(Classes + ProgId, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(Classes + LegacyProgId, throwOnMissingSubKey: false);
            Changed();
            Log.Info("Removed .fwz file type");
        }
        catch (Exception ex) { Log.Warn($"Could not remove .fwz association: {ex.Message}"); }
    }

    const int SHCNE_ASSOCCHANGED = 0x08000000;

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    static void Changed() => SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
