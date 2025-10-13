using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersArtifacts
{
    private readonly string _artifactRoot;

    public UatUsersArtifacts(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        Root = Path.GetFullPath(outputDirectory);
        _artifactRoot = Path.Combine(Root, "uat-users");
        Directory.CreateDirectory(_artifactRoot);
    }

    public string Root { get; }

    public string GetDefaultUserMapPath()
    {
        return Path.Combine(_artifactRoot, "00_user_map.csv");
    }

    public void WriteLines(string relativePath, IEnumerable<string> lines)
    {
        if (relativePath is null)
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        var path = ResolvePath(relativePath);
        File.WriteAllLines(path, lines);
    }

    public void WriteText(string relativePath, string contents)
    {
        if (relativePath is null)
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        var path = ResolvePath(relativePath);
        File.WriteAllText(path, contents ?? string.Empty, Encoding.UTF8);
    }

    public void WriteCsv(string relativePath, IEnumerable<IReadOnlyList<string>> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        var path = ResolvePath(relativePath);
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var row in rows)
        {
            WriteCsvRow(writer, row);
        }
    }

    private string ResolvePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(_artifactRoot, normalized);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    private static void WriteCsvRow(TextWriter writer, IReadOnlyList<string> cells)
    {
        var joined = cells.Select(EscapeCsvCell);
        writer.WriteLine(string.Join(',', joined));
    }

    private static string EscapeCsvCell(string value)
    {
        value ??= string.Empty;
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote)
        {
            return value;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
