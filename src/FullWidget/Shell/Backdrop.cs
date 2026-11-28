using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FullWidget.Core;
using FullWidget.Native;

namespace FullWidget.Shell;

/// The "snapshot" card (experimental): a blurred picture of what is behind a widget, taken once
/// when asked and not kept up to date. While it is taken, FullWidget's own windows are left out of
/// screen captures (Windows 10 2004 and later; nothing changes on screen), so the picture shows
/// what is behind them. Unlike the frosted card, Windows draws nothing: the corners and the shadow
/// are the page's own, like on a clear card.
static class Backdrop
{
    const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    /// How much smaller the picture is blurred, and the blur's radius there.
    const int Shrink = 6, Radius = 4;

    public static string FileOf(string instanceId) => Path.Combine(Paths.LocalDir, "backdrops", instanceId + ".png");

    /// Changes whenever a new picture is taken (for the page's URL); null while there is none.
    public static long? Version(string instanceId)
    {
        var f = FileOf(instanceId);
        return File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks : null;
    }

    public static void Delete(string instanceId)
    {
        try { File.Delete(FileOf(instanceId)); } catch (Exception ex) { Log.Warn($"Snapshot card: could not delete the picture of {instanceId}: {ex.Message}"); }
    }

    /// Takes the pictures behind these window rectangles (physical pixels). Called on the UI thread.
    /// False when Windows can't leave our windows out of the capture.
    public static async Task<bool> CaptureAsync(IReadOnlyList<(string id, Rectangle rect)> targets)
    {
        var changed = new List<IntPtr>();
        var shots = new List<(string id, Bitmap bmp)>();
        try
        {
            foreach (var h in OurWindows())
                if (GetWindowDisplayAffinity(h, out var a) && a == WDA_NONE && SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE))
                    changed.Add(h);
            if (changed.Count == 0)
            {
                Log.Warn("Snapshot card: can't leave FullWidget's windows out of the picture (needs Windows 10 2004 or later)");
                return false;
            }
            // the change shows from the next frames on
            DwmFlush();
            await Task.Delay(80);
            DwmFlush();
            foreach (var (id, r) in targets)
            {
                if (r.Width <= 0 || r.Height <= 0) continue;
                var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                shots.Add((id, b));
            }
        }
        finally
        {
            foreach (var h in changed) SetWindowDisplayAffinity(h, WDA_NONE);
        }
        await Task.Run(() =>
        {
            foreach (var (id, b) in shots)
                using (b)
                using (var blurred = Blur(b))
                    Save(id, blurred);
        });
        Log.Info($"Snapshot card: took {shots.Count} picture(s)");
        return true;
    }

    static List<IntPtr> OurWindows()
    {
        var list = new List<IntPtr>();
        for (var h = Win32.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = Win32.GetWindow(h, 2 /* GW_HWNDNEXT */))
        {
            Win32.GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId && Win32.IsWindowVisible(h)) list.Add(h);
        }
        return list;
    }

    /// Shrinks the picture, blurs it there (three box blurs: close to a Gaussian), and scales it
    /// back up smoothly.
    static Bitmap Blur(Bitmap src)
    {
        int w = Math.Max(1, src.Width / Shrink), h = Math.Max(1, src.Height / Shrink);
        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }

        var data = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new int[w * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        var c = new float[3][];
        for (int k = 0; k < 3; k++) c[k] = new float[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            c[0][i] = (px[i] >> 16) & 255;
            c[1][i] = (px[i] >> 8) & 255;
            c[2][i] = px[i] & 255;
        }
        var tmp = new float[px.Length];
        for (int k = 0; k < 3; k++)
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(c[k], tmp, w, h, horizontal: true);
                BoxBlur(tmp, c[k], w, h, horizontal: false);
            }
        for (int i = 0; i < px.Length; i++)
            px[i] = unchecked((int)0xFF000000) | (Byte(c[0][i]) << 16) | (Byte(c[1][i]) << 8) | Byte(c[2][i]);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        small.UnlockBits(data);

        var big = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        using (var edges = new ImageAttributes())
        {
            edges.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(small, new Rectangle(0, 0, big.Width, big.Height), 0, 0, w, h, GraphicsUnit.Pixel, edges);
        }
        return big;
    }

    static int Byte(float v) => (int)Math.Clamp(Math.Round(v), 0, 255);

    /// One box blur along rows or columns; the picture's edge pixels repeat beyond it.
    static void BoxBlur(float[] from, float[] to, int w, int h, bool horizontal)
    {
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        float scale = 1f / (2 * Radius + 1);
        for (int line = 0; line < lines; line++)
        {
            int At(int i) => horizontal ? line * w + Math.Clamp(i, 0, len - 1) : Math.Clamp(i, 0, len - 1) * w + line;
            float sum = 0;
            for (int i = -Radius; i <= Radius; i++) sum += from[At(i)];
            for (int i = 0; i < len; i++)
            {
                to[At(i)] = sum * scale;
                sum += from[At(i + Radius + 1)] - from[At(i - Radius)];
            }
        }
    }

    static void Save(string id, Bitmap bmp)
    {
        var file = FileOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        bmp.Save(tmp, ImageFormat.Png);
        File.Move(tmp, file, overwrite: true);
    }
}
