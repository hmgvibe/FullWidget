using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace M0Spike;

// Test switches (all optional):
//   --count=N            number of widget windows
//   --z=normal|top|desktop
//   --ct=off|layered     click-through
//   --pps=1|0            share renderer processes (--process-per-site)
//   --backdrop=none|acrylic|mica
//   --drag=manual|native|appregion
//   --x=  --y=           first widget position (physical px)
//   --testbg             show a checkerboard window behind the widgets
//   --noact              WS_EX_NOACTIVATE (clicks don't activate / raise the widget)
//   --zlock              force HWND_BOTTOM on every z-order change
record Options(int Count, string Z, string ClickThrough, bool ProcessPerSite, string Backdrop,
               string Drag, int X, int Y, bool TestBg, bool NoActivate = false, bool ZLock = false,
               Rectangle? Bg = null, string Extra = "", bool MemLow = false)
{
    public static Options Parse(string[] args)
    {
        var d = args.Select(a => a.TrimStart('-').Split('=', 2))
                    .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "1");
        string S(string k, string def) => d.TryGetValue(k, out var v) ? v : def;
        return new Options(int.Parse(S("count", "1")), S("z", "normal"), S("ct", "off"),
            S("pps", "1") == "1", S("backdrop", "none"), S("drag", "manual"),
            int.Parse(S("x", "300")), int.Parse(S("y", "300")), d.ContainsKey("testbg"),
            d.ContainsKey("noact"), d.ContainsKey("zlock"),
            d.TryGetValue("bg", out var bg) && bg.Split(',').Select(int.Parse).ToArray() is [var bx, var by, var bw, var bh]
                ? new Rectangle(bx, by, bw, bh) : null,
            S("extra", "").Replace(';', ' '), d.ContainsKey("memlow"));
    }
}

static class Log
{
    static readonly string PathName = System.IO.Path.Combine(AppContext.BaseDirectory, "status.jsonl");
    static readonly object Gate = new();
    public static void Reset() => File.WriteAllText(PathName, "");
    public static void Write(object o)
    {
        lock (Gate) File.AppendAllText(PathName, JsonSerializer.Serialize(o) + "\n");
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Reset();
        var o = Options.Parse(args);
        Log.Write(new { ev = "start", o, pid = Environment.ProcessId });
        Application.Run(new SpikeContext(o));
    }
}

class SpikeContext : ApplicationContext
{
    readonly Options o;
    readonly NotifyIcon tray;
    readonly List<Form> forms = new();

    public SpikeContext(Options o)
    {
        this.o = o;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "M0 Spike", Visible = true, ContextMenuStrip = menu };
        _ = InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            if (o.TestBg)
            {
                var bg = new CheckerForm(o.Bg ?? new Rectangle(o.X - 120, o.Y - 120, 1100, 700));
                bg.Show();
                forms.Add(bg);
            }

            var udf = Path.Combine(AppContext.BaseDirectory, "wv2data");
            var browserArgs = string.Join(' ', new[] { o.ProcessPerSite ? "--process-per-site" : "", o.Extra }
                .Where(s => !string.IsNullOrEmpty(s)));
            var envOpts = new CoreWebView2EnvironmentOptions(browserArgs);
            var env = await CoreWebView2Environment.CreateAsync(null, udf, envOpts);
            Log.Write(new { ev = "env", version = env.BrowserVersionString });

            for (int i = 0; i < o.Count; i++)
            {
                var f = new WidgetForm(o, i, env, new Point(o.X + (i % 3) * 600, o.Y + (i / 3) * 420));
                f.Show();
                forms.Add(f);
            }
        }
        catch (Exception ex)
        {
            Log.Write(new { ev = "error", ex = ex.ToString() });
        }
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        foreach (var f in forms) f.Close();
        base.ExitThreadCore();
    }
}

/// A plain window with a checkerboard + colour stripes, so transparency is easy to judge.
class CheckerForm : Form
{
    public CheckerForm(Rectangle r)
    {
        // Behaves like a normal app window (caption, taskbar button, minimised by Win+D).
        FormBorderStyle = FormBorderStyle.FixedSingle;
        Text = "Checker";
        StartPosition = FormStartPosition.Manual;
        Bounds = r;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        const int s = 24;
        for (int y = 0; y < Height; y += s)
            for (int x = 0; x < Width; x += s)
                e.Graphics.FillRectangle(((x + y) / s) % 2 == 0 ? Brushes.White : Brushes.LightGray, x, y, s, s);
        Color[] c = { Color.Red, Color.Orange, Color.Gold, Color.LimeGreen, Color.DeepSkyBlue, Color.MediumPurple };
        for (int i = 0; i < c.Length; i++)
            using (var b = new SolidBrush(c[i])) e.Graphics.FillRectangle(b, 0, 60 + i * 90, Width, 30);
    }
}
