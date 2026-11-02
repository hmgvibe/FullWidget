using FullWidget.Shell;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FullWidget.Core;

/// "新建組件": copies templates/hello-widget into the user's widgets folder under a new id.
static class Scaffold
{
    public const string Template = "hello-widget";

    /// Creates the folder and returns (id, folder). The display name may be any language;
    /// the id and folder name are made from its ASCII letters and digits.
    public static (string id, string dir) Create(string displayName, Catalog catalog)
    {
        displayName = displayName.Trim();
        if (displayName.Length == 0) throw new InvalidDataException(Strings.EnterName);
        if (displayName.Length > 40) throw new InvalidDataException(Strings.NameTooLong);
        // The template in the UI language, if there is one: hello-widget.en, hello-widget.zh-CN…
        var src = Path.Combine(Paths.TemplatesDir, $"{Template}.{Strings.Lang}");
        if (!Directory.Exists(src)) src = Path.Combine(Paths.TemplatesDir, Template);
        if (!Directory.Exists(src)) throw new InvalidDataException(Strings.NoTemplate);

        string slug = Slug(displayName);
        string id = "user." + slug, dir = Path.Combine(Paths.UserWidgetsDir, slug);
        for (int n = 2; catalog.Find(id) != null || Directory.Exists(dir); n++)
        {
            id = $"user.{slug}-{n}";
            dir = Path.Combine(Paths.UserWidgetsDir, $"{slug}-{n}");
        }

        Directory.CreateDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".json" or ".html" or ".js" or ".css" or ".md")
            {
                string name = ext switch
                {
                    ".json" => JsonEncodedText.Encode(displayName, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
                    ".html" => WebUtility.HtmlEncode(displayName),
                    ".js" => JsonEncodedText.Encode(displayName, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
                    _ => displayName,
                };
                var text = File.ReadAllText(file).Replace("{{id}}", id).Replace("{{name}}", name);
                File.WriteAllText(target, text, new UTF8Encoding(false));
            }
            else File.Copy(file, target);
        }
        // Type definitions next to main.js, so editors like VS Code complete `widget.` calls.
        var dts = Path.Combine(Paths.SdkDir, "widget.d.ts");
        if (File.Exists(dts)) File.Copy(dts, Path.Combine(dir, "widget.d.ts"), overwrite: true);
        Log.Info($"Created widget {id} in {dir}");
        return (id, dir);
    }

    static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if ((c is ' ' or '-' or '_' or '.') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        if (s.Length > 40) s = s[..40].Trim('-');
        return s.Length >= 2 ? s : "my-widget";
    }
}
