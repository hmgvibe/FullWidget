using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FullWidget.Core;

/// Contents of a widget's widget.json.
sealed class WidgetManifest
{
    public string Id { get; set; } = "";
    public JsonElement? Name { get; set; }
    public JsonElement? Description { get; set; }
    public string Version { get; set; } = "0.0.0";
    public string? Author { get; set; }
    public string Entry { get; set; } = "index.html";
    public string? Icon { get; set; }
    /// Group in the widget library: time, weather, system, productivity, info or media.
    public string? Category { get; set; }
    /// Picture for the widget library, relative to the folder; may contain {theme} and {lang},
    /// e.g. "preview/{theme}.{lang}.webp".
    public string? Preview { get; set; }
    public SizeSpec Size { get; set; } = new();
    /// "desktop" (default), "normal" or "top".
    public string? Layer { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<SettingSpec> Settings { get; set; } = new();

    [JsonIgnore] public string Dir { get; set; } = "";
    [JsonIgnore] public bool Builtin { get; set; }

    [JsonIgnore] public string DisplayName => Text.Pick(Name, Id);

    /// Each widget type gets its own origin, so their web storage is isolated, while all
    /// share the site "widgets.local" and therefore one renderer process.
    [JsonIgnore] public string HostName => Regex.Replace(Id.ToLowerInvariant(), "[^a-z0-9-]", "-") + ".widgets.local";

    /// Declared in widget.json (whether or not the user has allowed it yet; see Core.Permissions).
    public bool Declares(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    public static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.Compiled);
}

sealed class SizeSpec
{
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 120;
    public double MinWidth { get; set; } = 80;
    public double MinHeight { get; set; } = 48;
    public double MaxWidth { get; set; } = 2000;
    public double MaxHeight { get; set; } = 2000;
    public bool Resizable { get; set; } = true;
    /// How much the content grows when the card is made bigger than width × height:
    /// 0 = not at all (more room only), 1 = in step with the card. Applied to the rem size.
    public double Grow { get; set; }
}

/// One user-editable option. The control center (M3) builds a form from these.
sealed class SettingSpec
{
    public string Key { get; set; } = "";
    /// toggle | text | textarea | number | slider | select | color | font | file
    public string Type { get; set; } = "text";
    public JsonElement? Label { get; set; }
    public JsonElement? Description { get; set; }
    public JsonElement? Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public JsonElement? Placeholder { get; set; }
    public List<OptionSpec>? Options { get; set; }
}

sealed class OptionSpec
{
    public JsonElement Value { get; set; }
    public JsonElement? Label { get; set; }
}
