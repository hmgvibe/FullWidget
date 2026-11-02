using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FullWidget.Bridge;
using FullWidget.Core;

namespace FullWidget.Shell;

/// Programs, folders and files for widgets with the "shell.launch" permission: opening them,
/// choosing them in a file dialog, and their names and icons.
static class Launcher
{
    /// Opens a program, shortcut, folder or document as a double-click in Explorer would, without arguments.
    public static void Launch(string path)
    {
        string full = Check(path);
        Log.Info($"Launch: {full}");
        var psi = new ProcessStartInfo(full) { UseShellExecute = true };
        if (File.Exists(full)) psi.WorkingDirectory = Path.GetDirectoryName(full) ?? "";
        try { Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* the user said no to UAC */ }
        catch (System.ComponentModel.Win32Exception ex) { throw new BridgeException($"could not open '{full}': {ex.Message}"); }
    }

    static string Check(string path)
    {
        path = Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"')); // "%USERPROFILE%\Downloads" works too
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) throw new BridgeException("a full path is needed, e.g. C:\\Windows\\notepad.exe");
        string full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full)) throw new BridgeException($"'{full}' does not exist");
        return full;
    }

    /// Name, kind and icon of a path, for showing it in a list.
    public static object Info(string path, int size)
    {
        string full;
        try { full = Check(path); }
        catch (BridgeException) { return new { path, exists = false, name = Path.GetFileNameWithoutExtension(path ?? ""), kind = "missing", icon = (string?)null }; }
        bool dir = Directory.Exists(full);
        string ext = dir ? "" : Path.GetExtension(full).ToLowerInvariant();
        string kind = dir ? "folder" : ext is ".exe" or ".lnk" or ".appref-ms" or ".url" or ".bat" or ".cmd" ? "app" : "file";
        return new { path = full, exists = true, name = DisplayName(full, dir, ext), kind, icon = Icon(full, Math.Clamp(size, 16, 256)) };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    static string DisplayName(string full, bool dir, string ext)
    {
        if (dir || ext is ".lnk" or ".url" or ".appref-ms")
        {
            // as Explorer shows it: "下載" for the Downloads folder, "Local Disk (D:)", a shortcut without ".lnk"
            var sfi = new SHFILEINFO();
            if (SHGetFileInfo(full, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x200 /* SHGFI_DISPLAYNAME */) != IntPtr.Zero
                && !string.IsNullOrWhiteSpace(sfi.szDisplayName))
            {
                var n = sfi.szDisplayName;
                return ext.Length > 0 && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? n[..^ext.Length] : n;
            }
            if (dir)
            {
                var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
                return name.Length > 0 ? name : full;
            }
        }
        if (ext == ".exe")
        {
            // "Google Chrome" rather than "chrome"
            try
            {
                var v = FileVersionInfo.GetVersionInfo(full);
                var d = (v.FileDescription ?? "").Trim();
                if (d.Length is > 1 and < 60 && !d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return d;
            }
            catch (FileNotFoundException) { }
        }
        return Path.GetFileNameWithoutExtension(full);
    }

    // ── icons ──

    static readonly Dictionary<string, (DateTime stamp, string uri)> icons = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// The icon Explorer shows for the path, as a PNG data: URI with transparency.
    static string? Icon(string full, int size)
    {
        DateTime stamp;
        try { stamp = File.Exists(full) ? File.GetLastWriteTimeUtc(full) : Directory.GetLastWriteTimeUtc(full); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { stamp = DateTime.MinValue; } // "shell:AppsFolder\…"
        string key = $"{full}|{size}";
        if (icons.TryGetValue(key, out var c) && c.stamp == stamp) return c.uri;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out var factory) != 0) return null;
            const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0) return null;
            var uri = ToPng(hbm);
            if (uri != null) icons[key] = (stamp, uri);
            return uri;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException) { return null; }
        finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
    }

    static string? ToPng(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, bits, ref bmi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        // old-style icons come without alpha: make them opaque
        bool anyAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb); // the shell's pixels are premultiplied
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(bits, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    // ── apps by their Windows app ID ──

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bindCtx, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true, EntryPoint = "SHCreateItemFromParsingName")]
    static extern int SHCreateShellItem(string path, IntPtr bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    static readonly Dictionary<string, (string name, string? icon)> apps = new(StringComparer.OrdinalIgnoreCase);

    /// The name and icon of the app behind an app ID ("Spotify.exe", "MSEdge",
    /// "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"), as the Start menu shows them.
    public static (string name, string? icon) AppByUserModelId(string aumid)
    {
        if (aumid.Length == 0) return ("", null);
        if (apps.TryGetValue(aumid, out var known)) return known;
        string name = "";
        string? icon = null;
        try
        {
            string path = @"shell:AppsFolder\" + aumid;
            var iid = typeof(IShellItem).GUID;
            if (!aumid.Contains(Path.DirectorySeparatorChar) &&SHCreateShellItem(path, IntPtr.Zero, ref iid, out var item) == 0
                && item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var p) == 0)
            {
                name = Marshal.PtrToStringUni(p) ?? "";
                Marshal.FreeCoTaskMem(p);
                icon = Icon(path, 64);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException) { }
        if (name.Length == 0 && aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // a program known only by its file name: ask the running process where it lives
            foreach (var pr in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(aumid)))
                using (pr)
                    try
                    {
                        if (name.Length == 0 && pr.MainModule?.FileName is { } f)
                        {
                            name = DisplayName(f, false, ".exe");
                            icon = Icon(f, 64);
                        }
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        if (name.Length == 0)
        {
            var n = aumid.Split('!')[^1];
            name = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
        }
        return apps[aumid] = (name, icon);
    }

    // ── choosing ──

    /// A file or folder dialog; null when cancelled.
    public static string? Pick(string type, string? filter, string? title, string? start = null)
    {
        if (type == "folder")
        {
            using var f = new FolderBrowserDialog { Description = title ?? "", UseDescriptionForTitle = !string.IsNullOrEmpty(title), ShowNewFolderButton = false };
            if (start != null && Directory.Exists(start)) f.InitialDirectory = start; // opens where the setting points now
            return f.ShowDialog() == DialogResult.OK ? f.SelectedPath : null;
        }
        using var d = new OpenFileDialog
        {
            Title = title ?? "",
            Filter = filter switch
            {
                "programs" => Strings.FilterPrograms,
                "images" => Strings.FilterImages,
                _ => Strings.FilterAll,
            },
            DereferenceLinks = false, // keep the shortcut itself: its name and icon are what the user knows
            InitialDirectory = filter == "programs"
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs"
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
    }

    // ── Chinese text ──

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int LCMapStringEx(string locale, uint flags, string src, int srcLen, [Out] char[]? dest, int destLen, IntPtr ver, IntPtr reserved, IntPtr param);

    /// Simplified → Traditional or back, character by character, with Windows' own tables.
    public static string ConvertChinese(string text, bool toTraditional)
    {
        if (text.Length == 0) return text;
        if (text.Length > 20000) throw new BridgeException("text too long (20000 characters at most)");
        uint flags = toTraditional ? 0x04000000u /* LCMAP_TRADITIONAL_CHINESE */ : 0x02000000u /* LCMAP_SIMPLIFIED_CHINESE */;
        int n = LCMapStringEx("zh-CN", flags, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) return text;
        var buf = new char[n];
        LCMapStringEx("zh-CN", flags, text, text.Length, buf, n, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new string(buf, 0, n);
    }
}
