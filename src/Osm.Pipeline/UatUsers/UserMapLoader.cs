using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Osm.Pipeline.UatUsers;

public static class UserMapLoader
{
    public static IReadOnlyList<UserMappingEntry> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            return Array.Empty<UserMappingEntry>();
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return Array.Empty<UserMappingEntry>();
        }

        var header = ParseRow(lines[0]);
        var sourceIndex = IndexOf(header, "sourceuserid");
        var targetIndex = IndexOf(header, "targetuserid");
        var noteIndex = IndexOf(header, "note");

        if (sourceIndex < 0 || targetIndex < 0)
        {
            throw new InvalidDataException("Mapping CSV must include SourceUserId and TargetUserId columns.");
        }

        var raw = new List<UserMappingEntry>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var row = ParseRow(line);
            if (row.Count == 0)
            {
                continue;
            }

            var sourceValue = GetCell(row, sourceIndex);
            var targetValue = GetCell(row, targetIndex);
            if (!long.TryParse(sourceValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceId))
            {
                throw new InvalidDataException($"Invalid SourceUserId '{sourceValue}' on line {i + 1}.");
            }

            long? targetId = null;
            if (!string.IsNullOrEmpty(targetValue))
            {
                if (!long.TryParse(targetValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTarget))
                {
                    throw new InvalidDataException($"Invalid TargetUserId '{targetValue}' on line {i + 1}.");
                }

                targetId = parsedTarget;
            }

            var note = noteIndex >= 0 ? GetCell(row, noteIndex) : null;
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            raw.Add(new UserMappingEntry(sourceId, targetId, note));
        }

        var deduplicated = new Dictionary<long, UserMappingEntry>();
        foreach (var entry in raw)
        {
            if (!deduplicated.TryGetValue(entry.SourceUserId, out var existing))
            {
                deduplicated.Add(entry.SourceUserId, entry);
                continue;
            }

            if (existing.TargetUserId is null && entry.TargetUserId is not null)
            {
                deduplicated[entry.SourceUserId] = entry;
                continue;
            }

            if (existing.TargetUserId == entry.TargetUserId && string.IsNullOrEmpty(existing.Note) && !string.IsNullOrEmpty(entry.Note))
            {
                deduplicated[entry.SourceUserId] = entry;
            }
        }

        var result = new List<UserMappingEntry>(deduplicated.Values);
        result.Sort(static (left, right) => left.SourceUserId.CompareTo(right.SourceUserId));
        return result;
    }

    private static List<string> ParseRow(string line)
    {
        var cells = new List<string>();
        if (line is null)
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

    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
        {
            return string.Empty;
        }

        return row[index]?.Trim() ?? string.Empty;
    }
}
