namespace FullWidget.Core;

static class Log
{
    static readonly object Gate = new();
    const long MaxBytes = 1 << 20;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var file = Paths.LogFile;
                var fi = new FileInfo(file);
                if (fi.Exists && fi.Length > MaxBytes) File.Move(file, file + ".1", overwrite: true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
