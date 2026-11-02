using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Windows;

namespace FullWidget.Providers;

/// Pictures in a folder the user chose in a widget's "folder" setting. Choosing the folder is the
/// permission: a widget can read only that folder (and what's inside it), only pictures, and a
/// third-party widget only once the user has picked one; a default folder in its widget.json
/// isn't enough. Files are served on the widget's own origin at /__files/&lt;setting&gt;/&lt;path&gt;.
static class FolderFiles
{
    public static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".webp", ".bmp", ".avif", ".heic", ".heif", ".tif", ".tiff" };

    const int MaxList = 5000;

    /// The folder behind setting `key`, or an error saying why there is none.
    public static string Root(WidgetWindow w, App app, string key)
    {
        var spec = w.Manifest.Settings.FirstOrDefault(s => s.Key == key);
        if (spec == null || spec.Type != "folder")
            throw new BridgeException($"'{key}' is not a setting of type \"folder\" in widget.json");
        bool chosen = w.Cfg.Settings.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String;
        if (!chosen && !Permissions.Trusted(app.Config.Data, w.Manifest))
            throw new BridgeException("no folder chosen yet (the user picks it in this widget's settings)");
        var s = w.EffectiveSettings().GetValueOrDefault(key);
        string path = s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        if (path.Length == 0) throw new BridgeException("no folder chosen yet");
        if (!Path.IsPathFullyQualified(path)) throw new BridgeException($"'{path}' is not a full path");
        if (!Directory.Exists(path)) throw new BridgeException($"the folder '{path}' does not exist");
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }

    /// The pictures in the folder: { folder, files: [{ path, name, size, modified, url }], more }.
    public static async Task<object> ListAsync(string root, string key, bool recursive)
    {
        return await Task.Run(() =>
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MaxRecursionDepth = 8,
            };
            var files = new List<object>();
            bool more = false;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opts))
            {
                if (!ImageTypes.Contains(f.Extension)) continue;
                if (files.Count >= MaxList) { more = true; break; }
                string rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/');
                files.Add(new
                {
                    path = rel,
                    name = Path.GetFileNameWithoutExtension(f.Name),
                    size = f.Length,
                    modified = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                    url = $"/__files/{Uri.EscapeDataString(key)}/{string.Join('/', rel.Split('/').Select(Uri.EscapeDataString))}",
                });
            }
            return new { folder = root, name = Path.GetFileName(root), files, more };
        });
    }

    /// A file below `root`, or null if the path leaves it or isn't a picture.
    public static string? Resolve(string root, string rel)
    {
        string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        if (!ImageTypes.Contains(Path.GetExtension(full)) || !File.Exists(full)) return null;
        var attr = File.GetAttributes(full);
        return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0 ? null : full;
    }

    /// When the photo was taken (from the camera's data), else null.
    public static object Info(string full)
    {
        long? taken = null;
        int width = 0, height = 0;
        try
        {
            var iid = typeof(IShellItem2).GUID;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out IShellItem2 item) == 0)
            {
                var k = new PROPERTYKEY(new Guid("14B81DA1-0135-4D31-96D9-6CBFC9671A99"), 36867); // System.Photo.DateTaken
                if (item.GetFileTime(ref k, out long ft) == 0 && ft > 0)
                    taken = DateTimeOffset.FromFileTime(ft).ToUnixTimeMilliseconds();
                var kw = new PROPERTYKEY(new Guid("6444048F-4C8B-11D1-8B70-080036B11A03"), 3); // System.Image.HorizontalSize
                var kh = new PROPERTYKEY(new Guid("6444048F-4C8B-11D1-8B70-080036B11A03"), 4); // System.Image.VerticalSize
                if (item.GetUInt32(ref kw, out uint pw) == 0) width = (int)pw;
                if (item.GetUInt32(ref kh, out uint ph) == 0) height = (int)ph;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException) { }
        return new { taken, width, height };
    }

    /// A copy of the picture that fits in `size` × `size` pixels, made by Windows (it knows the
    /// photo's rotation, and HEIC when that extension is installed). A JPEG, or a PNG when the
    /// picture has transparency. Null if Windows can't read it.
    public static (byte[] bytes, string mime)? Thumbnail(string full, int size)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out IShellItemImageFactory factory) != 0) return null;
            const int SIIGBF_THUMBNAILONLY = 0x8;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_THUMBNAILONLY, out hbm) != 0) return null;
            return Encode(hbm);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or ExternalException) { return null; }
        finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
    }

    static (byte[], string)? Encode(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, bits, ref bmi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        bool anyAlpha = false, partial = false;
        for (int i = 3; i < bits.Length; i += 4)
        {
            if (bits[i] != 0) anyAlpha = true;
            if (bits[i] != 255) partial = true;
        }
        bool transparent = anyAlpha && partial;
        if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

        using var bmp = new Bitmap(w, h, transparent ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < h; y++) Marshal.Copy(bits, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        using var ms = new MemoryStream();
        if (transparent)
        {
            bmp.Save(ms, ImageFormat.Png);
            return (ms.ToArray(), "image/png");
        }
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
        bmp.Save(ms, jpeg, ep);
        return (ms.ToArray(), "image/jpeg");
    }

    // ── Windows shell ──

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY(Guid fmtid, uint pid) { public Guid fmtid = fmtid; public uint pid = pid; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap); }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem2
    {
        // IShellItem
        void BindToHandler(IntPtr bindCtx, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IntPtr parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IntPtr other, uint hint, out int order);
        // IShellItem2
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyStoreWithCreateObject(int flags, IntPtr createObject, ref Guid riid, out IntPtr ppv);
        void GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(ref PROPERTYKEY key, ref Guid riid, out IntPtr ppv);
        void Update(IntPtr bindCtx);
        void GetProperty(ref PROPERTYKEY key, IntPtr value);
        void GetCLSID(ref PROPERTYKEY key, out Guid clsid);
        [PreserveSig] int GetFileTime(ref PROPERTYKEY key, out long filetime);
        [PreserveSig] int GetInt32(ref PROPERTYKEY key, out int value);
        void GetString(ref PROPERTYKEY key, out IntPtr value);
        [PreserveSig] int GetUInt32(ref PROPERTYKEY key, out uint value);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem2 item);

    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
