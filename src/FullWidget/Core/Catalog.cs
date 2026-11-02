using FullWidget.Shell;
using System.Text.Json;

namespace FullWidget.Core;

/// A widget folder that could not be loaded, and why (shown in the control center).
sealed record CatalogProblem(string Folder, string Dir, string Message);

/// All widget types found on disk: built-in ones next to the exe, plus user ones in AppData.
sealed class Catalog
{
    /// Ids starting with this belong to the built-in widgets.
    public const string ReservedPrefix = "fullwidget.";
    /// The built-in widgets' ids before the app was renamed from DeskWidgets.
    public const string LegacyPrefix = "deskwidgets.";

    public static bool IsReserved(string id) =>
        id.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase) || id.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);

    /// "deskwidgets.clock" becomes "fullwidget.clock"; other ids stay as they are.
    public static string Migrate(string id) =>
        id.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase) ? ReservedPrefix + id[LegacyPrefix.Length..] : id;

    readonly Dictionary<string, WidgetManifest> byId = new(StringComparer.OrdinalIgnoreCase);
    readonly List<CatalogProblem> problems = new();

    public IReadOnlyCollection<WidgetManifest> All => byId.Values;
    public IReadOnlyList<CatalogProblem> Problems => problems;

    public WidgetManifest? Find(string id) => byId.GetValueOrDefault(id);

    public void Scan()
    {
        byId.Clear();
        problems.Clear();
        ScanDir(Paths.BuiltinWidgetsDir, builtin: true);
        ScanDir(Paths.UserWidgetsDir, builtin: false);
        Log.Info($"Catalog: {byId.Count} widget(s): {string.Join(", ", byId.Keys)}" +
                 (problems.Count > 0 ? $"; {problems.Count} not loaded" : ""));
    }

    void ScanDir(string root, bool builtin)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue; // staging folders (.install) and the like
            if (!File.Exists(Path.Combine(dir, "widget.json"))) continue;
            try
            {
                var m = Load(dir, builtin);
                if (!builtin && IsReserved(m.Id))
                    throw new InvalidDataException(string.Format(Strings.ReservedId, m.Id, ReservedPrefix));
                if (byId.TryGetValue(m.Id, out var other))
                    throw new InvalidDataException(string.Format(Strings.DuplicateId, m.Id, Path.GetFileName(other.Dir)));
                byId[m.Id] = m;
            }
            catch (InvalidDataException ex)
            {
                problems.Add(new CatalogProblem(name, Path.GetFullPath(dir), ex.Message));
                Log.Warn($"Catalog: skipped {dir}: {ex.Message}");
            }
        }
    }

    /// Reads and checks a widget folder's widget.json. Problems come back as InvalidDataException
    /// with a message meant for the widget's author.
    public static WidgetManifest Load(string dir, bool builtin)
    {
        var file = Path.Combine(dir, "widget.json");
        WidgetManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<WidgetManifest>(File.ReadAllText(file), Json.File);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? string.Format(Strings.LineN, line + 1) : "";
            string detail = ex.Message.Split(" Path:", 2)[0].Split(" LineNumber:", 2)[0].Trim();
            throw new InvalidDataException(string.Format(Strings.JsonError, where, JsonHint(detail), detail));
        }
        catch (IOException ex)
        {
            throw new InvalidDataException(string.Format(Strings.CantRead, ex.Message));
        }
        if (m == null) throw new InvalidDataException(Strings.EmptyManifest);
        m.Dir = Path.GetFullPath(dir);
        m.Builtin = builtin;
        Validate(m);
        return m;
    }

    /// The usual causes of the parser's messages, in words a beginner can act on.
    static string JsonHint(string detail) =>
        detail.Contains("after a value") ? Strings.HintComma
        : detail.Contains("after a property name") ? Strings.HintColon
        : detail.Contains("invalid start of a value") ? Strings.HintQuotes
        : detail.Contains("could not be converted") ? Strings.HintType
        : detail.Contains("end of data") || detail.Contains("depth") ? Strings.HintBrackets
        : "";

    static void Validate(WidgetManifest m)
    {
        if (string.IsNullOrWhiteSpace(m.Id)) throw new InvalidDataException(Strings.MissingId);
        if (!WidgetManifest.IdPattern.IsMatch(m.Id))
            throw new InvalidDataException(string.Format(Strings.BadId, m.Id));
        var entry = Path.GetFullPath(Path.Combine(m.Dir, m.Entry));
        if (!entry.StartsWith(m.Dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
            throw new InvalidDataException(string.Format(Strings.NoEntry, m.Entry));
        var s = m.Size;
        s.MinWidth = Math.Clamp(s.MinWidth, 32, 4000);
        s.MinHeight = Math.Clamp(s.MinHeight, 32, 4000);
        s.MaxWidth = Math.Max(s.MaxWidth, s.MinWidth);
        s.MaxHeight = Math.Max(s.MaxHeight, s.MinHeight);
        s.Width = Math.Clamp(s.Width, s.MinWidth, s.MaxWidth);
        s.Height = Math.Clamp(s.Height, s.MinHeight, s.MaxHeight);
        var keys = new HashSet<string>();
        foreach (var setting in m.Settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Key)) throw new InvalidDataException(Strings.SettingNoKey);
            if (!keys.Add(setting.Key)) throw new InvalidDataException(string.Format(Strings.SettingDupKey, setting.Key));
        }
    }
}
