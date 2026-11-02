using FullWidget.Bridge;
using FullWidget.Core;

namespace FullWidget.Shell;

/// Windows notifications for widgets, shown through the tray icon. A notification can be booked
/// for later: the host keeps the time, so it arrives on time even while the widget's page is
/// paused (full-screen app, hide all) or reloading.
sealed class Notifier : IDisposable
{
    const int MaxPerWidget = 20;
    static readonly TimeSpan MaxAhead = TimeSpan.FromDays(7);
    /// After sleep or a long pause, a notification this late is dropped instead of shown.
    static readonly TimeSpan MaxLate = TimeSpan.FromMinutes(15);

    sealed record Booking(string Instance, string Tag, DateTimeOffset At, string Title, string Body);

    readonly List<Booking> booked = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Action<string, string> show;

    public Notifier(Action<string, string> show)
    {
        this.show = show;
        timer.Tick += (_, _) => Tick();
    }

    /// Shows now, or at `at`. A booking with the same tag from the same widget instance is replaced.
    public void Schedule(string instance, string? tag, DateTimeOffset? at, string title, string body)
    {
        title = Clip(title, 64);
        body = Clip(body, 256);
        if (title.Length == 0 && body.Length == 0) throw new BridgeException("notification needs a title or a body");
        tag = Clip(tag ?? "", 64);
        if (tag.Length > 0) Cancel(instance, tag);

        var now = DateTimeOffset.Now;
        if (at == null || at <= now) { Show(instance, title, body); return; }
        if (at - now > MaxAhead) throw new BridgeException("notifications can be booked at most 7 days ahead");
        if (booked.Count(b => b.Instance == instance) >= MaxPerWidget)
            throw new BridgeException($"too many booked notifications (at most {MaxPerWidget} per widget)");
        booked.Add(new Booking(instance, tag, at.Value, title, body));
        Log.Info($"Notification from {instance} booked for {at.Value.LocalDateTime:HH:mm:ss}");
        timer.Start();
    }

    /// Cancels the instance's booking with this tag, or all of its bookings when tag is null.
    public void Cancel(string instance, string? tag)
    {
        booked.RemoveAll(b => b.Instance == instance && (tag == null || b.Tag == tag));
        if (booked.Count == 0) timer.Stop();
    }

    void Tick()
    {
        var now = DateTimeOffset.Now;
        var due = booked.Where(b => b.At <= now).OrderBy(b => b.At).ToList();
        if (due.Count == 0) return;
        booked.RemoveAll(due.Contains);
        if (booked.Count == 0) timer.Stop();
        foreach (var b in due)
            if (now - b.At <= MaxLate) Show(b.Instance, b.Title, b.Body);
            else Log.Info($"Notification from {b.Instance} dropped: {(now - b.At).TotalMinutes:0} min late");
    }

    void Show(string instance, string title, string body)
    {
        Log.Info($"Notification from {instance}: {title}");
        show(title, body);
    }

    static string Clip(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    public void Dispose() => timer.Dispose();
}
