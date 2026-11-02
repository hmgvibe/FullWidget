using System.Text.Json;

namespace FullWidget.Core;

/// Per-instance key/value storage (widget.storage in the SDK), one JSON file per instance.
sealed class StorageStore : IDisposable
{
    const int MaxBytesPerInstance = 2 * 1024 * 1024;

    readonly Dictionary<string, Dictionary<string, JsonElement>> cache = new();
    readonly HashSet<string> dirty = new();
    readonly System.Windows.Forms.Timer flushTimer = new() { Interval = 800 };

    public StorageStore()
    {
        flushTimer.Tick += (_, _) => Flush();
    }

    static string FileFor(string instanceId) => Path.Combine(Paths.StorageDir, instanceId + ".json");

    Dictionary<string, JsonElement> Open(string instanceId)
    {
        if (cache.TryGetValue(instanceId, out var d)) return d;
        d = new();
        var file = FileFor(instanceId);
        if (File.Exists(file))
        {
            try { d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file), Json.File) ?? new(); }
            catch (Exception ex) { Log.Error($"Storage {instanceId} unreadable", ex); }
        }
        cache[instanceId] = d;
        return d;
    }

    public JsonElement? Get(string instanceId, string key) => Open(instanceId).TryGetValue(key, out var v) ? v : null;

    public IReadOnlyCollection<string> Keys(string instanceId) => Open(instanceId).Keys;

    public void Set(string instanceId, string key, JsonElement value)
    {
        var d = Open(instanceId);
        d[key] = value.Clone();
        if (JsonSerializer.SerializeToUtf8Bytes(d).Length > MaxBytesPerInstance)
        {
            d.Remove(key);
            throw new InvalidOperationException("storage quota exceeded (2 MB per widget)");
        }
        MarkDirty(instanceId);
    }

    public void Remove(string instanceId, string key)
    {
        if (Open(instanceId).Remove(key)) MarkDirty(instanceId);
    }

    public void DeleteInstance(string instanceId)
    {
        cache.Remove(instanceId);
        dirty.Remove(instanceId);
        try { File.Delete(FileFor(instanceId)); } catch (Exception ex) { Log.Warn($"Storage delete failed: {ex.Message}"); }
    }

    void MarkDirty(string instanceId)
    {
        dirty.Add(instanceId);
        flushTimer.Stop();
        flushTimer.Start();
    }

    public void Flush()
    {
        flushTimer.Stop();
        foreach (var id in dirty.ToList())
        {
            try { Json.WriteAtomic(FileFor(id), JsonSerializer.Serialize(cache[id], Json.File)); }
            catch (Exception ex) { Log.Error($"Storage save failed for {id}", ex); }
        }
        dirty.Clear();
    }

    public void Dispose()
    {
        Flush();
        flushTimer.Dispose();
    }
}
