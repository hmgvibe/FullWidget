using System.Runtime.InteropServices;
using FullWidget.Core;

namespace FullWidget.Shell;

/// System-wide shortcuts (Settings → Shortcuts). Keys are written like "Ctrl+Alt+N"; an empty
/// string turns a shortcut off. A combination another program already holds can't be taken,
/// which shows next to the shortcut in the control center.
sealed class Hotkeys : NativeWindow, IDisposable
{
    /// Show or hide every widget.
    public const string ToggleAll = "toggleAll";
    /// Bring the sticky notes to the front to jot something down.
    public const string Notes = "notes";
    public static readonly string[] Actions = { ToggleAll, Notes };
    public static Dictionary<string, string> Defaults() => new() { [ToggleAll] = "Ctrl+Alt+W", [Notes] = "Ctrl+Alt+N" };

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly Action<string> run;
    readonly Dictionary<string, bool> taken = new();
    Dictionary<string, string> keys = new();
    bool paused;

    public Hotkeys(Action<string> run)
    {
        this.run = run;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE: no window, just messages
    }

    /// Registers the given shortcuts, replacing the ones before.
    public void Apply(Dictionary<string, string> map)
    {
        keys = Actions.ToDictionary(a => a, a => map.TryGetValue(a, out var k) ? k ?? "" : "");
        Register();
    }

    /// While the control center records a new shortcut, the current ones must not fire.
    public void Pause(bool on)
    {
        if (paused == on) return;
        paused = on;
        Register();
    }

    /// For the control center: each shortcut's keys and whether it could be registered.
    public object Status() => Actions.ToDictionary(a => a, a => new
    {
        keys = keys.GetValueOrDefault(a, ""),
        ok = !taken.GetValueOrDefault(a),
    });

    void Register()
    {
        for (int i = 0; i < Actions.Length; i++) UnregisterHotKey(Handle, i + 1);
        taken.Clear();
        if (paused) return;
        for (int i = 0; i < Actions.Length; i++)
        {
            var a = Actions[i];
            if (!TryParse(keys.GetValueOrDefault(a, ""), out var mods, out var vk)) continue;
            if (!RegisterHotKey(Handle, i + 1, mods | MOD_NOREPEAT, vk))
            {
                taken[a] = true;
                Log.Warn($"Shortcut {keys[a]} for {a} is taken by another program");
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() is var id && id >= 1 && id <= Actions.Length)
        {
            var a = Actions[id - 1];
            try { run(a); } catch (Exception ex) { Log.Error($"Shortcut {a} failed", ex); }
            return;
        }
        base.WndProc(ref m);
    }

    static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        [";"] = 0xBA, ["="] = 0xBB, [","] = 0xBC, ["-"] = 0xBD, ["."] = 0xBE, ["/"] = 0xBF, ["`"] = 0xC0,
        ["["] = 0xDB, ["\\"] = 0xDC, ["]"] = 0xDD, ["'"] = 0xDE,
    };

    /// "Ctrl+Alt+N" → modifiers and virtual key. A shortcut needs a modifier, except F1–F24.
    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // "+" itself isn't offered, so an empty part is a typo
        if (parts.Any(p => p.Length == 0)) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: return false;
            }
        }
        var key = parts[^1];
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) vk = char.ToUpperInvariant(key[0]);
        else if (key.Length is >= 2 and <= 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24) vk = (uint)(0x70 + f - 1);
        else if (key.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsAsciiDigit(key[3])) vk = (uint)(0x60 + key[3] - '0');
        else if (!Named.TryGetValue(key, out vk)) return false;
        bool fkey = vk is >= 0x70 and <= 0x87;
        return mods != 0 || fkey;
    }

    public void Dispose()
    {
        for (int i = 0; i < Actions.Length; i++) UnregisterHotKey(Handle, i + 1);
        DestroyHandle();
    }
}
