using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Osm.Pipeline.UatUsers;

internal static class AllowedUserLoader
{
    public static IReadOnlyCollection<long> Load(string path, string userIdColumn)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Allowed user list path must be provided.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(userIdColumn))
        {
            throw new ArgumentException("User identifier column must be provided.", nameof(userIdColumn));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Allowed user list '{path}' was not found.", path);
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return Array.Empty<long>();
        }

        var header = ParseRow(lines[0]);
        var userIndex = IndexOf(header, userIdColumn);
        if (userIndex < 0)
        {
            throw new InvalidDataException($"CSV '{path}' does not contain a '{userIdColumn}' column.");
        }

        var result = new SortedSet<long>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var row = ParseRow(line);
            if (row.Count <= userIndex)
            {
                continue;
            }

            var cell = row[userIndex]?.Trim();
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                throw new InvalidDataException($"Invalid user identifier '{cell}' on line {i + 1}.");
            }

            result.Add(id);
        }

        return result;
    }

    private static List<string> ParseRow(string? line)
    {
        var cells = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return cells;
        }

        var span = line.AsSpan();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch == '\"')
            {
                if (inQuotes && i + 1 < span.Length && span[i + 1] == '\"')
                {
                    builder.Append('\"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                cells.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        cells.Add(builder.ToString());
        return cells;
    }

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
