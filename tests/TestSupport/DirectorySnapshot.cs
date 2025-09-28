using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tests.Support;

public static class DirectorySnapshot
{
    public static void AssertMatches(string expectedRoot, string actualRoot)
    {
        if (expectedRoot is null)
        {
            throw new ArgumentNullException(nameof(expectedRoot));
        }

        if (actualRoot is null)
        {
            throw new ArgumentNullException(nameof(actualRoot));
        }

        var expectedFiles = ReadAllFiles(expectedRoot);
        var actualFiles = ReadAllFiles(actualRoot);

        Assert.Empty(expectedFiles.Keys.Except(actualFiles.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(actualFiles.Keys.Except(expectedFiles.Keys, StringComparer.OrdinalIgnoreCase));

        foreach (var (relativePath, expectedContent) in expectedFiles)
        {
            Assert.True(actualFiles.TryGetValue(relativePath, out var actualContent),
                $"Missing file '{relativePath}' in actual output.");
            Assert.Equal(expectedContent, actualContent);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadAllFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => NormalizeRelativePath(Path.GetRelativePath(root, path)),
                path => NormalizeContent(path, File.ReadAllText(path)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string NormalizeContent(string path, string content)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(content);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd();
        }

        return content.Replace("\r\n", "\n").TrimEnd();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
