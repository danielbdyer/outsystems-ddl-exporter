using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersArtifacts
{
    private readonly string _artifactRoot;
    private readonly bool _idempotentEmission;

    public UatUsersArtifacts(string outputDirectory, bool idempotentEmission = false)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        Root = Path.GetFullPath(outputDirectory);
        _artifactRoot = Path.Combine(Root, "uat-users");
        Directory.CreateDirectory(_artifactRoot);
        _idempotentEmission = idempotentEmission;
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

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line ?? string.Empty);
        }

        var path = ResolvePath(relativePath);
        IdempotentFileWriter.WriteAllText(path, builder.ToString(), _idempotentEmission);
    }

    public void WriteText(string relativePath, string contents)
    {
        if (relativePath is null)
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        var path = ResolvePath(relativePath);
        IdempotentFileWriter.WriteAllText(path, contents ?? string.Empty, _idempotentEmission);
    }

    public void WriteCsv(string relativePath, IEnumerable<IReadOnlyList<string>> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            WriteCsvRow(builder, row);
        }

        var path = ResolvePath(relativePath);
        IdempotentFileWriter.WriteAllText(path, builder.ToString(), _idempotentEmission);
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

    private static void WriteCsvRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        if (cells is null)
        {
            builder.AppendLine();
            return;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsvCell(cells[i]));
        }

        builder.AppendLine();
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
