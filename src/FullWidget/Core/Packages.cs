using FullWidget.Shell;
using System.IO.Compression;

namespace FullWidget.Core;

/// A package unpacked to the staging folder, waiting for the user to install it or cancel.
sealed class PendingPackage
{
    public string Token { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string Source { get; init; }
    /// The staging folder for this package (deleted on cancel).
    public required string StageDir { get; init; }
    /// The widget folder inside StageDir (the package root, or its single top folder).
    public required string WidgetDir { get; init; }
    public required WidgetManifest Manifest { get; init; }
}

/// .fwz packages: a zip of one widget folder (widget.json at the root, or inside one top folder).
static class Packages
{
    public const string Extension = ".fwz";
    /// Packages made before the rename (DeskWidgets) still open.
    public const string LegacyExtension = ".dwz";
    const long MaxPackageBytes = 64L << 20;
    const long MaxUnpackedBytes = 256L << 20;
    const int MaxEntries = 5000;

    /// Unpacks a package to the staging folder and reads its manifest. Nothing is installed yet.
    public static PendingPackage Open(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists) throw new InvalidDataException(Strings.FileNotFound);
        if (info.Length > MaxPackageBytes) throw new InvalidDataException(Strings.PackageTooLarge);

        var stage = Path.Combine(Paths.StagingDir, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(stage);
        try
        {
            Extract(file, stage);
            string root = File.Exists(Path.Combine(stage, "widget.json")) ? stage
                : Directory.GetDirectories(stage) is [var only] && File.Exists(Path.Combine(only, "widget.json")) && Directory.GetFiles(stage).Length == 0 ? only
                : throw new InvalidDataException(Strings.NotAWidget);
            var m = Catalog.Load(root, builtin: false);
            if (Catalog.IsReserved(m.Id))
                throw new InvalidDataException(string.Format(Strings.ReservedInstall, m.Id));
            return new PendingPackage { Source = Path.GetFullPath(file), StageDir = stage, WidgetDir = root, Manifest = m };
        }
        catch
        {
            TryDelete(stage);
            throw;
        }
    }

    static void Extract(string file, string dest)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(file); }
        catch (InvalidDataException) { throw new InvalidDataException(Strings.Corrupt); }
        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new InvalidDataException(string.Format(Strings.TooManyFiles, MaxEntries));
            long total = 0;
            string root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
            foreach (var e in zip.Entries)
            {
                total += e.Length;
                if (total > MaxUnpackedBytes) throw new InvalidDataException(Strings.UnpackedTooLarge);
                var target = Path.GetFullPath(Path.Combine(dest, e.FullName));
                // Entries must stay inside the folder ("../../evil.exe" is refused).
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(string.Format(Strings.UnsafePath, e.FullName));
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: false);
            }
        }
    }

    /// Moves a staged package into the widgets folder. An installed copy with the same id is
    /// replaced (the old folder goes to the Recycle Bin). Returns the widget's folder.
    public static string Install(PendingPackage p, Catalog catalog)
    {
        var existing = catalog.Find(p.Manifest.Id);
        if (existing is { Builtin: true }) throw new InvalidDataException(Strings.CantOverwriteBuiltin);

        string target;
        if (existing != null)
        {
            target = existing.Dir;
            Recycle(target);
        }
        else
        {
            string name = new string(p.Manifest.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
            target = Path.Combine(Paths.UserWidgetsDir, name);
            for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(Paths.UserWidgetsDir, $"{name}-{n}");
        }
        Directory.Move(p.WidgetDir, target);
        TryDelete(p.StageDir);
        Log.Info($"Installed {p.Manifest.Id} {p.Manifest.Version} to {target}");
        return target;
    }

    public static void Cancel(PendingPackage p) => TryDelete(p.StageDir);

    /// Zips a widget folder. Hidden files and folders (.git…) and node_modules are left out.
    public static void Export(WidgetManifest m, string file)
    {
        var tmp = file + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var path in Directory.EnumerateFiles(m.Dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(m.Dir, path);
                if (rel.Split(Path.DirectorySeparatorChar).Any(seg => seg.StartsWith('.') || seg == "node_modules")) continue;
                zip.CreateEntryFromFile(path, rel.Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, file, overwrite: true);
        Log.Info($"Exported {m.Id} to {file}");
    }

    /// Compares "1.10.0" with "1.9.2" by their numbers (anything after '-' or '+' is ignored).
    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string v) => v.Split('-', '+')[0].Split('.')
            .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var x = Parts(a); var y = Parts(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    /// Default file name for an export: "hello-1.0.0.fwz".
    public static string FileName(WidgetManifest m)
    {
        var name = m.Id.StartsWith("user.") ? m.Id[5..] : m.Id;
        return string.Concat($"{name}-{m.Version}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + Extension;
    }

    /// Sends a folder to the Recycle Bin, so an uninstall or update can be undone.
    public static void Recycle(string dir)
    {
        if (!Directory.Exists(dir)) return;
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// Removes leftovers from packages that were never installed (e.g. the app was closed).
    public static void CleanStaging() => TryDelete(Paths.StagingDir);

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn($"Could not delete {dir}: {ex.Message}"); }
    }
}
