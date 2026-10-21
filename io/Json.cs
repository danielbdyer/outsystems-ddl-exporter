using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DbChange.Io;

/// <summary>
/// JSON as dbchange writes it on every operating system: two-space indent, LF, a final newline, and
/// only the escaping JSON itself requires (nothing here is embedded in HTML), so one document is one
/// sequence of bytes.
/// </summary>
public static class Json
{
    public static JsonSerializerOptions Options { get; } = Canonical();

    /// <summary>The document as text, ready for <see cref="Write"/>.</summary>
    public static string Text(JsonNode node) => node.ToJsonString(Options) + "\n";

    /// <summary>The document on one line, escaped alike, for a block that holds one document a line (predict's machine block).</summary>
    public static string Line(JsonNode node) => node.ToJsonString(OneLine);

    private static readonly JsonSerializerOptions OneLine = Canonical(indented: false);

    private static JsonSerializerOptions Canonical(bool indented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
