using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullWidget.Core;

static class Json
{
    /// For files we write (config, storage).
    public static readonly JsonSerializerOptions File = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Chinese readable in files
    };

    /// For messages sent to web pages. The default encoder escapes &lt; &gt; &amp;,
    /// so the output is also safe to embed inside a &lt;script&gt; tag.
    public static readonly JsonSerializerOptions Web = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Web);

    /// Writes a file atomically so a crash never leaves half a config behind. Another program
    /// (antivirus, backup, an editor) holding the file open makes the replace fail for a moment,
    /// so it retries, and as a last resort writes the file directly rather than lose the data.
    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, text);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                System.IO.File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(40 * (attempt + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not replace {Path.GetFileName(path)} ({ex.Message}); writing it directly");
                System.IO.File.WriteAllText(path, text);
                try { System.IO.File.Delete(tmp); } catch { /* harmless leftover */ }
                return;
            }
        }
    }
}
