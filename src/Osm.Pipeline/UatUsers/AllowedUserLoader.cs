using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Osm.Pipeline.UatUsers;

internal sealed record AllowedUserLoadResult(
    IReadOnlyCollection<UserIdentifier> UserIds,
    int SqlRowCount,
    int ListRowCount);

internal static class AllowedUserLoader
{
    private static readonly Regex InsertStatementRegex = new(
        @"INSERT\s+INTO\s+(?:\[?(?<schema>[^\]\s]+)\]?\.)?\[?(?<table>[^\]\s]+)\]?\s*\((?<columns>[^)]+)\)\s+VALUES\s*(?<values>(?:N?'(?:''|[^'])*'|[^;'])+);",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static AllowedUserLoadResult Load(
        string? ddlPath,
        string? userIdsPath,
        string userSchema,
        string userTable,
        string userIdColumn)
    {
        if (string.IsNullOrWhiteSpace(userSchema))
        {
            throw new ArgumentException("User schema must be provided.", nameof(userSchema));
        }

        if (string.IsNullOrWhiteSpace(userTable))
        {
            throw new ArgumentException("User table must be provided.", nameof(userTable));
        }

        if (string.IsNullOrWhiteSpace(userIdColumn))
        {
            throw new ArgumentException("User identifier column must be provided.", nameof(userIdColumn));
        }

        if (string.IsNullOrWhiteSpace(ddlPath) && string.IsNullOrWhiteSpace(userIdsPath))
        {
            throw new ArgumentException("At least one allowed user input must be provided.");
        }

        var results = new SortedSet<UserIdentifier>();
        var sqlRowCount = 0;
        var listRowCount = 0;

        if (!string.IsNullOrWhiteSpace(ddlPath))
        {
            foreach (var id in LoadFromSql(ddlPath!, userSchema, userTable, userIdColumn))
            {
                sqlRowCount++;
                results.Add(id);
            }
        }

        if (!string.IsNullOrWhiteSpace(userIdsPath))
        {
            foreach (var id in LoadFromList(userIdsPath!, userIdColumn))
            {
                listRowCount++;
                results.Add(id);
            }
        }

        return new AllowedUserLoadResult(results, sqlRowCount, listRowCount);
    }

    private static IEnumerable<UserIdentifier> LoadFromSql(
        string path,
        string userSchema,
        string userTable,
        string userIdColumn)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Allowed user DDL '{path}' was not found.", path);
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in InsertStatementRegex.Matches(text))
        {
            var table = match.Groups["table"].Value;
            if (!string.Equals(table, userTable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var schema = match.Groups["schema"].Value;
            if (!string.IsNullOrEmpty(schema) && !string.Equals(schema, userSchema, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = ParseSqlColumns(match.Groups["columns"].Value);
            var index = columns.FindIndex(column => string.Equals(column, userIdColumn, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            var valuesSegment = match.Groups["values"].Value;
            foreach (var row in SplitSqlRows(valuesSegment))
            {
                var cells = ParseSqlRow(row);
                if (cells.Count <= index)
                {
                    continue;
                }

                var cell = NormalizeSqlLiteral(cells[index]);
                if (string.IsNullOrEmpty(cell) || cell.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!UserIdentifier.TryParse(cell, out var identifier))
                {
                    throw new InvalidDataException($"Unable to parse user identifier '{cell}' from '{path}'.");
                }

                yield return identifier;
            }
        }
    }

    private static IEnumerable<UserIdentifier> LoadFromList(string path, string userIdColumn)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Allowed user list '{path}' was not found.", path);
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            yield break;
        }

        var firstDataIndex = FindFirstDataLineIndex(lines);
        if (firstDataIndex < 0)
        {
            yield break;
        }

        var firstLine = lines[firstDataIndex];
        var trimmedFirstLine = firstLine.Trim();
        var looksLikeHeader = string.Equals(trimmedFirstLine, userIdColumn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmedFirstLine, "UserId", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeHeader && TryParseSingleValue(firstLine, out var singleValue))
        {
            yield return singleValue;
            for (var i = firstDataIndex + 1; i < lines.Length; i++)
            {
                if (TryParseSingleValue(lines[i], out var value))
                {
                    yield return value;
                }
            }

            yield break;
        }

        var header = ParseCsvRow(lines[firstDataIndex]);
        var index = IndexOf(header, userIdColumn);
        if (index < 0)
        {
            index = IndexOf(header, "UserId");
        }
        if (index < 0 && header.Count == 1)
        {
            index = 0;
        }
        if (index < 0)
        {
            throw new InvalidDataException($"List '{path}' does not contain a '{userIdColumn}' column.");
        }

        for (var i = firstDataIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var row = ParseCsvRow(line);
            if (row.Count <= index)
            {
                continue;
            }

            var cell = row[index]?.Trim();
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            if (!UserIdentifier.TryParse(cell, out var identifier))
            {
                throw new InvalidDataException($"Invalid user identifier '{cell}' on line {i + 1} of '{path}'.");
            }

            yield return identifier;
        }
    }

    private static int FindFirstDataLineIndex(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool TryParseSingleValue(string line, out UserIdentifier value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            return false;
        }

        if (trimmed.Contains(',', StringComparison.Ordinal) || trimmed.Contains('"'))
        {
            return false;
        }

        return UserIdentifier.TryParse(trimmed, out value);
    }

    private static List<string> ParseCsvRow(string? line)
    {
        var cells = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return cells;
        }

        var span = line.AsSpan();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < span.Length && span[i + 1] == '"')
                {
                    builder.Append('"');
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

    private static List<string> ParseSqlColumns(string text)
    {
        var columns = new List<string>();
        var segments = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..^1];
            }

            columns.Add(trimmed.Trim());
        }

        return columns;
    }

    private static IEnumerable<string> SplitSqlRows(string valuesSegment)
    {
        var builder = new StringBuilder();
        var depth = 0;
        var inString = false;

        for (var i = 0; i < valuesSegment.Length; i++)
        {
            var ch = valuesSegment[i];

            if (ch == '\'')
            {
                if (depth > 0)
                {
                    builder.Append(ch);
                }

                if (inString && i + 1 < valuesSegment.Length && valuesSegment[i + 1] == '\'')
                {
                    if (depth > 0)
                    {
                        builder.Append('\'');
                    }

                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (ch == '(')
                {
                    if (depth > 0)
                    {
                        builder.Append(ch);
                    }

                    depth++;
                    continue;
                }

                if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return builder.ToString();
                        builder.Clear();
                        continue;
                    }

                    if (depth > 0)
                    {
                        builder.Append(ch);
                    }

                    continue;
                }

                if (depth == 0)
                {
                    continue;
                }
            }

            if (depth > 0)
            {
                builder.Append(ch);
            }
        }
    }

    private static List<string> ParseSqlRow(string row)
    {
        var cells = new List<string>();
        if (string.IsNullOrEmpty(row))
        {
            return cells;
        }

        var builder = new StringBuilder();
        var inString = false;

        for (var i = 0; i < row.Length; i++)
        {
            var ch = row[i];
            if (ch == '\'')
            {
                if (inString && i + 1 < row.Length && row[i + 1] == '\'')
                {
                    builder.Append('\'');
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (ch == ',' && !inString)
            {
                cells.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        cells.Add(builder.ToString().Trim());
        return cells;
    }

    private static string NormalizeSqlLiteral(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return value;
        }

        if (value.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && value.EndsWith("'", StringComparison.Ordinal))
        {
            return value[2..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }
}
