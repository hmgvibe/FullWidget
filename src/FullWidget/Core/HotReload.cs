namespace FullWidget.Core;

/// Developer mode: watches widget folders and reports changed files in batches, once the
/// editor has finished saving (editors often write a file several times in a row).
sealed class HotReload : IDisposable
{
    readonly List<FileSystemWatcher> watchers = new();
    readonly HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Forms.Timer settle = new() { Interval = 250 };
    readonly SynchronizationContext ui;
    readonly Action<IReadOnlyCollection<string>> onChanged;

    public HotReload(IEnumerable<string> roots, Action<IReadOnlyCollection<string>> onChanged)
    {
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("no UI context");
        this.onChanged = onChanged;
        settle.Tick += (_, _) => Flush();
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var w = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Changed += (_, e) => Add(root, e.FullPath);
            w.Created += (_, e) => Add(root, e.FullPath);
            w.Deleted += (_, e) => Add(root, e.FullPath);
            w.Renamed += (_, e) => { Add(root, e.OldFullPath); Add(root, e.FullPath); };
            w.Error += (_, e) => Log.Warn($"Hot reload watcher: {e.GetException().Message}");
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        Log.Info($"Hot reload on: {string.Join(", ", watchers.Select(w => w.Path))}");
    }

    readonly List<(string dir, long until)> muted = new();

    /// Ignores changes under a folder for a moment: the app just changed it itself (install,
    /// new widget, uninstall) and has already reloaded what needs reloading.
    public void Mute(string dir, int ms = 2000)
    {
        lock (muted) muted.Add((Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), Environment.TickCount64 + ms));
    }

    bool IsMuted(string path)
    {
        lock (muted)
        {
            long now = Environment.TickCount64;
            muted.RemoveAll(m => m.until < now);
            return muted.Any(m => path.Equals(m.dir, StringComparison.OrdinalIgnoreCase)
                               || path.StartsWith(m.dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Watcher events arrive on thread-pool threads.
    void Add(string root, string path)
    {
        if (Ignored(Path.GetRelativePath(root, path)) || IsMuted(path)) return;
        lock (changed) changed.Add(path);
        ui.Post(_ => { settle.Stop(); settle.Start(); }, null);
    }

    /// Hidden folders (.git, .install staging), node_modules and editor temp files.
    static bool Ignored(string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Any(p => p.StartsWith('.') || p == "node_modules")) return true;
        var name = parts[^1];
        return name.EndsWith('~') || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase);
    }

    void Flush()
    {
        settle.Stop();
        string[] batch;
        lock (changed) { batch = changed.ToArray(); changed.Clear(); }
        if (batch.Length == 0) return;
        try { onChanged(batch); }
        catch (Exception ex) { Log.Error("Hot reload failed", ex); }
    }

    public void Dispose()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
        settle.Dispose();
    }
}
