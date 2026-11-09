using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FullWidget.Core;
using FullWidget.Shell;

namespace FullWidget;

static class Program
{
    /// Command line:
    ///   --data-dir=PATH   use another data folder (portable mode, testing)
    ///   --cmd="..."       send a command to the running instance (see App.Execute)
    ///   --exit            same as --cmd=exit
    ///   --wait-pid=N      wait for process N to exit first (used when restarting)
    ///   --apply-update=DIR   (the new version, unpacked) replace the copy in DIR with this one, then start it
    ///   --cleanup         remove autostart and the .fwz file type, then quit (before deleting the app)
    ///   FILE.fwz          install a widget package
    [STAThread]
    static int Main(string[] args)
    {
        string? dataDir = null, command = null;
        bool cleanup = false;
        int? waitPid = null;
        string? applyTo = null;
        foreach (var arg in args)
        {
            // A package path, e.g. from double-clicking a .fwz file.
            if (!arg.StartsWith('-') && (arg.EndsWith(".fwz", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".dwz", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                command = "install " + Path.GetFullPath(arg);
                continue;
            }
            var (name, value) = arg.Split('=', 2) is [var k, var v] ? (k, v) : (arg, "");
            switch (name.TrimStart('-').ToLowerInvariant())
            {
                case "data-dir": dataDir = value.Trim('"'); break;
                // only a pair of quotes around the whole command; a command may end with a quoted value
                case "cmd": command = value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value; break;
                case "exit": command = "exit"; break;
                case "cleanup": cleanup = true; break;
                case "apply-update": applyTo = value.Trim('"'); break;
                case "wait-pid": waitPid = int.TryParse(value, out var p) ? p : null; break;
            }
        }

        if (waitPid is int pid)
        {
            try { Process.GetProcessById(pid).WaitForExit(15_000); } catch (ArgumentException) { /* already gone */ }
        }

        Paths.Init(dataDir);
        if (applyTo != null) return Updater.Apply(applyTo, dataDir);
        if (cleanup)
        {
            // Before deleting a portable copy: take FullWidget out of Windows' startup list and file types.
            AutoStart.DataDirArg = dataDir;
            if (AutoStart.Enabled) AutoStart.Enabled = false;
            FileAssociation.Unregister();
            return 0;
        }
        string key = "FullWidget_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Paths.DataDir.ToLowerInvariant())))[..16];

        using var mutex = new Mutex(true, @"Local\" + key, out bool firstInstance);
        if (!firstInstance)
        {
            // This launch came from the user (e.g. a double-click), so it may bring a window to
            // the front; pass that right on to the running instance.
            AllowSetForegroundWindow(-1);
            CommandChannel.Send(key, command ?? "activate");
            return 0;
        }
        if (command == "exit") return 0; // nothing running

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); // must precede any control
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        Log.Info($"Starting FullWidget {App.Version} (pid {Environment.ProcessId}), data: {Paths.DataDir}");
        if (Paths.MovedFromOldName) Log.Info("Moved the data folders over from DeskWidgets");
        using var app = new App(key, dataDir, command);
        Application.Run(app);
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
