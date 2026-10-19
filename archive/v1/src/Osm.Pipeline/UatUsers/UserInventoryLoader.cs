using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Osm.Pipeline.UatUsers;

public sealed record UserInventoryRecord(
    UserIdentifier UserId,
    string? Username,
    string? Email,
    string? Name,
    string? ExternalId,
    string? IsActive,
    string? CreationDate,
    string? LastLogin);

public sealed record UserInventoryLoadResult(
    IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> Records,
    int RowCount);

internal static class UserInventoryLoader
{
    private static readonly string[] EmailColumns = ["email", "e-mail", "emailaddress", "e-mailaddress", "e_mail", "email_address", "e-mail_address"];
    private static readonly string[] ExternalIdColumns = ["external_id", "externalid", "external-id"];
    private static readonly string[] IsActiveColumns = ["is_active", "isactive", "is-active"];
    private static readonly string[] CreationDateColumns = ["creation_date", "creationdate", "createdon"];
    private static readonly string[] LastLoginColumns = ["last_login", "lastlogin", "last_logged_in"];

    public static UserInventoryLoadResult Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("User inventory path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"User inventory '{path}' was not found.", path);
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new InvalidDataException("User inventory CSV must include a header row.");
        }

        var firstDataIndex = FindFirstDataLineIndex(lines);
        if (firstDataIndex < 0)
        {
            throw new InvalidDataException("User inventory CSV did not contain any data rows.");
        }

        var header = ParseRow(lines[firstDataIndex]);
        if (header.Count == 0)
        {
            throw new InvalidDataException("User inventory CSV header is empty.");
        }

        var idIndex = IndexOf(header, "id");
        if (idIndex < 0)
        {
            throw new InvalidDataException("User inventory CSV must include an 'Id' column.");
        }

        var usernameIndex = IndexOf(header, "username");
        var emailIndex = IndexOf(header, EmailColumns);
        var nameIndex = IndexOf(header, "name");
        var externalIdIndex = IndexOf(header, ExternalIdColumns);
        var isActiveIndex = IndexOf(header, IsActiveColumns);
        var creationDateIndex = IndexOf(header, CreationDateColumns);
        var lastLoginIndex = IndexOf(header, LastLoginColumns);

        var records = new Dictionary<UserIdentifier, UserInventoryRecord>();
        var rowCount = 0;

        for (var i = firstDataIndex + 1; i < lines.Length; i++)
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

            if (row.Count <= idIndex)
            {
                throw new InvalidDataException($"User inventory row {i + 1} does not include an Id value.");
            }

            var idValue = GetCell(row, idIndex);
            if (string.IsNullOrWhiteSpace(idValue))
            {
                continue;
            }

            if (!UserIdentifier.TryParse(idValue, out var identifier))
            {
                throw new InvalidDataException($"Invalid user identifier '{idValue}' on line {i + 1} of '{path}'.");
            }

            rowCount++;

            if (!records.TryAdd(identifier, new UserInventoryRecord(
                    identifier,
                    GetCell(row, usernameIndex),
                    GetCell(row, emailIndex),
                    GetCell(row, nameIndex),
                    GetCell(row, externalIdIndex),
                    GetCell(row, isActiveIndex),
                    NormalizeDate(GetCell(row, creationDateIndex)),
                    NormalizeDate(GetCell(row, lastLoginIndex)))))
            {
                throw new InvalidDataException($"Duplicate user identifier '{identifier}' detected in '{path}'.");
            }
        }

        return new UserInventoryLoadResult(
            ImmutableSortedDictionary.CreateRange(records),
            rowCount);
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.ToString("O", CultureInfo.InvariantCulture);
        }

        return value.Trim();
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

    private static List<string> ParseRow(string? line)
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

    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
        {
            return string.Empty;
        }

        return row[index]?.Trim() ?? string.Empty;
    }

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        return IndexOf(header, new[] { name });
    }

    private static int IndexOf(IReadOnlyList<string> header, IReadOnlyList<string> candidates)
    {
        if (header is null)
        {
            return -1;
        }

        var normalized = candidates
            .Select(candidate => candidate?.Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();

        if (normalized.Length == 0)
        {
            return -1;
        }

        for (var i = 0; i < header.Count; i++)
        {
            var cell = header[i]?.Trim();
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            var lowered = cell.ToLowerInvariant();
            if (normalized.Contains(lowered))
            {
                return i;
            }
        }

        return -1;
    }
}
