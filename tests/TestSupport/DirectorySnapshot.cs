using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

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

        var missingFiles = expectedFiles.Keys
            .Except(actualFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedFiles = actualFiles.Keys
            .Except(expectedFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mismatchedFiles = new List<FileDifference>();
        foreach (var (relativePath, expectedContent) in expectedFiles)
        {
            if (!actualFiles.TryGetValue(relativePath, out var actualContent))
            {
                continue;
            }

            if (!string.Equals(expectedContent, actualContent, StringComparison.Ordinal))
            {
                mismatchedFiles.Add(new FileDifference(relativePath, DescribeDifference(expectedContent, actualContent)));
            }
        }

        if (missingFiles.Length == 0 && unexpectedFiles.Length == 0 && mismatchedFiles.Count == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("Directory snapshot mismatch.");

        if (missingFiles.Length > 0)
        {
            message.AppendLine("Missing files:");
            foreach (var path in missingFiles)
            {
                message.Append("  - ").AppendLine(path);
            }
        }

        if (unexpectedFiles.Length > 0)
        {
            message.AppendLine("Unexpected files:");
            foreach (var path in unexpectedFiles)
            {
                message.Append("  - ").AppendLine(path);
            }
        }

        if (mismatchedFiles.Count > 0)
        {
            message.AppendLine("Content differences:");
            foreach (var difference in mismatchedFiles)
            {
                message.Append("  - ").AppendLine(difference.RelativePath);
                message.AppendLine(Indent(difference.Details, "    "));
            }
        }

        throw new XunitException(message.ToString());
    }

    private static IReadOnlyDictionary<string, string> ReadAllFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => NormalizeComparisonPath(NormalizeRelativePath(Path.GetRelativePath(root, path))),
                path => NormalizeFileContent(path),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string NormalizeComparisonPath(string path)
    {
        const string CanonicalZipSuffix = ".zip.canonical.txt";

        if (path.EndsWith(CanonicalZipSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(0, path.Length - CanonicalZipSuffix.Length) + ".zip";
        }

        return path;
    }

    private static string NormalizeFileContent(string path)
    {
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeZipContent(path);
        }

        return NormalizeContent(path, File.ReadAllText(path));
    }

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

    private static string NormalizeZipContent(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var builder = new StringBuilder();

        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"entry:{entry.FullName}");

            if (entry.Length == 0)
            {
                builder.AppendLine();
                continue;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            var entryContent = reader.ReadToEnd();
            builder.AppendLine(NormalizeContent(entry.FullName, entryContent));
        }

        return builder.ToString().TrimEnd();
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

    private static string DescribeDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var length = Math.Min(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < length; i++)
        {
            if (!string.Equals(expectedLines[i], actualLines[i], StringComparison.Ordinal))
            {
                return $"First difference at line {i + 1}{Environment.NewLine}" +
                       $"expected: {expectedLines[i]}{Environment.NewLine}" +
                       $"actual  : {actualLines[i]}";
            }
        }

        if (expectedLines.Length != actualLines.Length)
        {
            return $"Line count differs. Expected {expectedLines.Length}, actual {actualLines.Length}.";
        }

        return "Files differ but no line-level difference could be identified.";
    }

    private static string Indent(string value, string prefix)
    {
        var lines = value.Replace("\r", string.Empty).Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => prefix + line));
    }

    private sealed record FileDifference(string RelativePath, string Details);
}
