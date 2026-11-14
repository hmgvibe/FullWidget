using FullWidget.Core;
using Microsoft.Web.WebView2.Core;

namespace FullWidget.Windows;

/// Watches that one of FullWidget's own pages (control center, menu) starts. Now and then a page
/// comes up without its bridge to FullWidget (window.chrome.webview) and so never says it is
/// ready: the control center stays blank, a menu never opens (FullWidget#4). A reload brings the
/// bridge back, which is what this does, after writing down what it saw.
sealed class PageWatch : IDisposable
{
    const int Max = 3;
    readonly string name, origin;
    readonly Func<CoreWebView2?> core;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    bool ready;
    int reloads;

    /// origin: the page's own address ("https://app.fullwidget.local/"); navigations elsewhere
    /// are cancelled by the window and don't count.
    public PageWatch(string name, string origin, Func<CoreWebView2?> core)
    {
        this.name = name;
        this.origin = origin;
        this.core = core;
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var c = core();
            if (c == null || ready) return;
            string bridge = "?";
            try { bridge = await c.ExecuteScriptAsync("!!(window.chrome && window.chrome.webview)"); }
            catch (Exception ex) { bridge = $"? ({ex.Message})"; }
            if (ready) return;
            if (reloads >= Max)
            {
                Log.Error($"{name}: the page still hasn't started after {Max} reloads (bridge: {bridge})");
                return;
            }
            reloads++;
            Log.Warn($"{name}: the page loaded but didn't say it was ready within 5 s (bridge: {bridge}); reloading ({reloads}/{Max})");
            try { c.Reload(); } catch (Exception ex) { Log.Warn($"{name}: reload failed: {ex.Message}"); }
        };
    }

    /// Hooks the page's navigation: call before Navigate.
    public void Attach(CoreWebView2 c)
    {
        bool ours = false;
        c.NavigationStarting += (_, e) =>
        {
            ours = e.Uri.StartsWith(origin, StringComparison.OrdinalIgnoreCase);
            if (ours) { ready = false; timer.Stop(); }
        };
        c.NavigationCompleted += (_, e) =>
        {
            if (!ours) return;
            if (!e.IsSuccess) Log.Warn($"{name}: the page didn't load ({e.WebErrorStatus})");
            if (!ready) { timer.Stop(); timer.Start(); }
        };
        c.ProcessFailed += (_, e) => Log.Warn($"{name}: WebView2 process failed: {e.ProcessFailedKind} ({e.Reason})");
    }

    /// The page said it is ready.
    public void Ready()
    {
        ready = true;
        timer.Stop();
        if (reloads > 0) Log.Info($"{name}: the page started after {reloads} reload(s)");
        reloads = 0;
    }

    public void Dispose() => timer.Dispose();
}
