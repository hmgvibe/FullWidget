namespace FullWidget.Shell;

/// The app icon, read from app.ico (built by build\make-icon.ps1 and embedded in the exe).
static class AppIcon
{
    static Stream Open() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico") ?? throw new InvalidOperationException("app.ico not embedded");

    /// The frame closest to this size (the tray wants the small icon size, 16 px at 100 %).
    public static Icon Create(int size)
    {
        using var s = Open();
        return new Icon(s, size, size);
    }

    /// Every size, for window title bars and the taskbar (Windows picks the frame).
    public static Icon Create()
    {
        using var s = Open();
        return new Icon(s);
    }
}
