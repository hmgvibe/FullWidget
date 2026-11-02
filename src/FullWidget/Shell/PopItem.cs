using System.Text.Json.Serialization;

namespace FullWidget.Shell;

/// One entry of the drawn popup menu (ui/menu). The page renders it; the host runs `Run`.
/// Kinds: header, item, toggle, seg, sub, check, sep, row (of btn).
sealed class PopItem
{
    public string Kind { get; init; } = "item";
    public string? Id { get; set; }
    public string? Text { get; init; }
    /// Header: second line. Item: right-side hint. Sub: the current value.
    public string? Hint { get; init; }
    public string? Icon { get; init; }
    public bool On { get; init; }
    public bool Danger { get; init; }
    public bool Disabled { get; init; }
    /// Ask once more before running, showing this text (e.g. "再按一次移除").
    public string? Confirm { get; init; }
    public string? Value { get; init; }
    public List<PopOption>? Options { get; init; }
    /// A sub page's entries, or a row's buttons.
    public List<PopItem>? Items { get; init; }

    /// Gets the chosen value for segmented controls, null otherwise.
    [JsonIgnore] public Action<string?>? Run { get; init; }
    /// Toggles and segmented controls keep the menu open, so the change is visible.
    [JsonIgnore] public bool KeepOpen { get; init; }

    public static PopItem Header(string text, string? hint, string? icon) => new() { Kind = "header", Text = text, Hint = hint, Icon = icon };
    public static PopItem Item(string text, Action run, string? hint = null, string? icon = null, bool danger = false, string? confirm = null, bool disabled = false) =>
        new() { Text = text, Run = _ => run(), Hint = hint, Icon = icon, Danger = danger, Confirm = confirm, Disabled = disabled };
    public static PopItem Toggle(string text, bool on, Action run, bool disabled = false) =>
        new() { Kind = "toggle", Text = text, On = on, Run = _ => run(), KeepOpen = true, Disabled = disabled };
    public static PopItem Seg(string text, string value, IEnumerable<(string value, string label)> options, Action<string> run) =>
        new() { Kind = "seg", Text = text, Value = value, Options = options.Select(o => new PopOption(o.value, o.label)).ToList(), Run = v => { if (v != null) run(v); }, KeepOpen = true };
    public static PopItem Sub(string text, string? hint, List<PopItem> items) => new() { Kind = "sub", Text = text, Hint = hint, Items = items, Disabled = items.Count == 0 };
    public static PopItem Check(string text, bool on, Action run, string? icon = null) => new() { Kind = "check", Text = text, On = on, Run = _ => run(), Icon = icon };
    public static PopItem Row(params PopItem[] buttons) => new() { Kind = "row", Items = buttons.ToList() };
    public static PopItem Btn(string text, Action run, bool keepOpen = false) => new() { Kind = "btn", Text = text, Run = _ => run(), KeepOpen = keepOpen };
    public static PopItem Sep => new() { Kind = "sep" };
}

sealed record PopOption(string Value, string Label);
