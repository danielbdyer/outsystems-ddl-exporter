using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Osm.Pipeline.RemapUsers;

public sealed class RemapUsersArtifactWriter : IRemapUsersArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _root;

    public RemapUsersArtifactWriter(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Artifact directory must be provided.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public async Task WriteJsonAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var fullPath = GetFullPath(relativePath);
        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteCsvAsync(string relativePath, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        var fullPath = GetFullPath(relativePath);
        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', row.Select(FormatCsvField))).ConfigureAwait(false);
        }
    }

    public async Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(relativePath);
        await File.WriteAllTextAsync(fullPath, text ?? string.Empty, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private string GetFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        var sanitized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_root, sanitized);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static string FormatCsvField(string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
        {
            return value;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
