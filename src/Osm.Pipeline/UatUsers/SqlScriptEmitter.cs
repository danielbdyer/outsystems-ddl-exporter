using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Osm.Pipeline.UatUsers;

public static class SqlScriptEmitter
{
    public static string BuildScript(UatUsersContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var observedIdentifiers = CollectIdentifiers(context);
        var identifierMode = DetermineIdentifierMode(observedIdentifiers);
        var maxIdentifierLength = observedIdentifiers.Count == 0
            ? 1
            : observedIdentifiers.Max(static id => id.Value.Length);

        var builder = new StringBuilder();
        AppendHeader(builder, context);
        builder.AppendLine("SET NOCOUNT ON;");
        builder.AppendLine();
        builder.AppendLine(BuildRemapTableDefinition(identifierMode, maxIdentifierLength));
        builder.AppendLine();

        var applicableMappings = context.UserMap
            .Where(entry => context.IsOrphan(entry.SourceUserId) && entry.TargetUserId.HasValue)
            .OrderBy(entry => entry.SourceUserId)
            .ToList();

        if (applicableMappings.Count > 0)
        {
            builder.AppendLine("INSERT INTO #UserRemap VALUES");
            for (var i = 0; i < applicableMappings.Count; i++)
            {
                var entry = applicableMappings[i];
                var suffix = i == applicableMappings.Count - 1 ? ";" : ",";
                builder.Append("    (")
                    .Append(FormatIdentifierLiteral(entry.SourceUserId, identifierMode))
                    .Append(", ")
                    .Append(FormatIdentifierLiteral(entry.TargetUserId!.Value, identifierMode))
                    .Append(", ")
                    .Append(SqlFormatting.SqlStringLiteral(TrimRationale(entry.Rationale)))
                    .Append(')')
                    .AppendLine(suffix);
            }
        }
        else
        {
            builder.AppendLine("-- Populate #UserRemap before executing updates.");
        }

        builder.AppendLine();

        var missingMappings = context.UserMap
            .Where(entry => context.IsOrphan(entry.SourceUserId) && entry.TargetUserId is null)
            .OrderBy(entry => entry.SourceUserId)
            .ToList();

        if (missingMappings.Count > 0)
        {
            builder.AppendLine("-- Pending mappings without TargetUserId. Update the CSV and rerun:");
            foreach (var entry in missingMappings)
            {
                var pending = GetPendingRowCount(context, entry.SourceUserId);
                builder.Append("--   SourceUserId = ")
                    .Append(entry.SourceUserId.ToString())
                    .Append(" (rows pending: ")
                    .Append(pending.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(")");
            }

            builder.AppendLine();
        }

        AppendTargetSanityCheck(builder, context, identifierMode);

        builder.AppendLine(BuildChangesTableDefinition(identifierMode));
        builder.AppendLine();
        builder.AppendLine("IF NOT EXISTS (SELECT 1 FROM #UserRemap)");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    PRINT 'No mappings supplied. Skipping updates.';");
        builder.AppendLine("    GOTO Summary;");
        builder.AppendLine("END;");
        builder.AppendLine();

        AppendTargetSanityCheck(builder, context, identifierMode);

        foreach (var column in context.UserFkCatalog)
        {
            AppendUpdateBlock(builder, column, identifierMode);
        }

        builder.AppendLine("SELECT TableName, ColumnName, COUNT(*) AS RowsUpdated FROM #Changes GROUP BY TableName, ColumnName ORDER BY TableName, ColumnName;");
        return builder.ToString();
    }

    private static void AppendUpdateBlock(StringBuilder builder, UserFkColumn column, IdentifierMode mode)
    {
        _ = mode;
        var schema = SqlFormatting.QuoteIdentifier(column.SchemaName);
        var table = SqlFormatting.QuoteIdentifier(column.TableName);
        var quotedColumn = SqlFormatting.QuoteIdentifier(column.ColumnName);
        var tableLiteral = SqlFormatting.SqlStringLiteral($"{column.SchemaName}.{column.TableName}");
        var columnLiteral = SqlFormatting.SqlStringLiteral(column.ColumnName);

        builder.AppendLine($"PRINT 'Remapping {column.SchemaName}.{column.TableName}.{column.ColumnName}';");
        builder.AppendLine(";WITH delta AS (");
        builder.AppendLine($"    SELECT t.{quotedColumn} AS OldUserId, r.TargetUserId AS NewUserId");
        builder.AppendLine($"    FROM {schema}.{table} AS t");
        builder.AppendLine($"    JOIN #UserRemap AS r ON r.SourceUserId = t.{quotedColumn}");
        builder.AppendLine($"    WHERE t.{quotedColumn} IS NOT NULL");
        builder.AppendLine($"      AND t.{quotedColumn} <> r.TargetUserId");
        builder.AppendLine(")");
        builder.AppendLine("UPDATE t");
        builder.AppendLine($"   SET t.{quotedColumn} = d.NewUserId");
        builder.AppendLine($"OUTPUT {tableLiteral}, {columnLiteral}, deleted.{quotedColumn}, inserted.{quotedColumn}, SYSUTCDATETIME()");
        builder.AppendLine("  INTO #Changes(TableName, ColumnName, OldUserId, NewUserId, ChangedAt)");
        builder.AppendLine($"FROM {schema}.{table} AS t");
        builder.AppendLine($"JOIN delta AS d ON d.OldUserId = t.{quotedColumn};");
        builder.AppendLine();
    }

    private static void AppendHeader(StringBuilder builder, UatUsersContext context)
    {
        builder.AppendLine($"-- uat-users remap script generated {DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"-- Inputs hash: {ComputeInputsHash(context)}");
        builder.AppendLine($"-- Catalog length: {context.UserFkCatalog.Count}");
        builder.AppendLine($"-- Source fingerprint: {context.SourceFingerprint}");
        builder.AppendLine();
    }

    private static void AppendTargetSanityCheck(StringBuilder builder, UatUsersContext context, IdentifierMode mode)
    {
        _ = mode;
        var userSchema = SqlFormatting.QuoteIdentifier(context.UserSchema);
        var userTable = SqlFormatting.QuoteIdentifier(context.UserTable);
        var userIdColumn = SqlFormatting.QuoteIdentifier(context.UserIdColumn);

        builder.AppendLine($"IF EXISTS (SELECT 1 FROM #UserRemap AS r LEFT JOIN {userSchema}.{userTable} AS u ON u.{userIdColumn} = r.TargetUserId WHERE u.{userIdColumn} IS NULL)");
        builder.AppendLine("BEGIN");
        builder.AppendLine("    SELECT r.SourceUserId, r.TargetUserId");
        builder.AppendLine("    FROM #UserRemap AS r");
        builder.AppendLine($"    LEFT JOIN {userSchema}.{userTable} AS u ON u.{userIdColumn} = r.TargetUserId");
        builder.AppendLine($"    WHERE u.{userIdColumn} IS NULL;");
        builder.AppendLine("    THROW 51000, 'Target user IDs are missing from the user table.', 1;");
        builder.AppendLine("END;");
        builder.AppendLine();
    }

    private static string ComputeInputsHash(UatUsersContext context)
    {
        using var sha256 = SHA256.Create();
        var buffer = new StringBuilder();
        buffer.Append(context.SourceFingerprint)
            .Append('|')
            .Append(context.UserSchema).Append('.').Append(context.UserTable)
            .Append('|')
            .Append(context.UserIdColumn)
            .Append('|');

        foreach (var id in context.AllowedUserIds)
        {
            buffer.Append(id).Append(',');
        }

        buffer.Append('|');
        foreach (var column in context.UserFkCatalog)
        {
            buffer
                .Append(column.SchemaName).Append('|')
                .Append(column.TableName).Append('|')
                .Append(column.ColumnName).Append('|')
                .Append(column.ForeignKeyName)
                .Append(';');
        }

        buffer.Append('|');
        foreach (var mapping in context.UserMap)
        {
            buffer
                .Append(mapping.SourceUserId).Append('=');
            if (mapping.TargetUserId.HasValue)
            {
                buffer.Append(mapping.TargetUserId.Value);
            }

            buffer.Append('|');
        }

        var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static string TrimRationale(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 200 ? value : value[..200];
    }

    private static string BuildRemapTableDefinition(IdentifierMode mode, int maxIdentifierLength)
    {
        return mode switch
        {
            IdentifierMode.Numeric => "CREATE TABLE #UserRemap (SourceUserId INT PRIMARY KEY, TargetUserId INT NOT NULL, Rationale NVARCHAR(200) NULL);",
            IdentifierMode.Guid => "CREATE TABLE #UserRemap (SourceUserId UNIQUEIDENTIFIER PRIMARY KEY, TargetUserId UNIQUEIDENTIFIER NOT NULL, Rationale NVARCHAR(200) NULL);",
            IdentifierMode.Text => BuildTextRemapDefinition(maxIdentifierLength),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported identifier mode.")
        };
    }

    private static string BuildTextRemapDefinition(int maxIdentifierLength)
    {
        var length = Math.Clamp(Math.Max(maxIdentifierLength, 32), 32, 450);
        return $"CREATE TABLE #UserRemap (SourceUserId NVARCHAR({length}) PRIMARY KEY, TargetUserId NVARCHAR({length}) NOT NULL, Rationale NVARCHAR(200) NULL);";
    }

    private static string BuildChangesTableDefinition(IdentifierMode mode)
    {
        var identifierType = mode switch
        {
            IdentifierMode.Numeric => "INT",
            IdentifierMode.Guid => "UNIQUEIDENTIFIER",
            _ => "NVARCHAR(450)"
        };

        return $"CREATE TABLE #Changes (TableName sysname NOT NULL, ColumnName sysname NOT NULL, OldUserId {identifierType} NOT NULL, NewUserId {identifierType} NOT NULL, ChangedAt datetime2(3) NOT NULL);";
    }

    private static string FormatIdentifierLiteral(UserIdentifier identifier, IdentifierMode mode)
    {
        return mode switch
        {
            IdentifierMode.Numeric when identifier.NumericValue.HasValue => identifier.NumericValue.Value.ToString(CultureInfo.InvariantCulture),
            IdentifierMode.Guid => SqlFormatting.SqlStringLiteral(identifier.ToString()),
            _ => SqlFormatting.SqlStringLiteral(identifier.ToString())
        };
    }

    private static IReadOnlyCollection<UserIdentifier> CollectIdentifiers(UatUsersContext context)
    {
        var collected = new HashSet<UserIdentifier>();
        foreach (var id in context.AllowedUserIds)
        {
            collected.Add(id);
        }

        foreach (var id in context.OrphanUserIds)
        {
            collected.Add(id);
        }

        foreach (var mapping in context.UserMap)
        {
            collected.Add(mapping.SourceUserId);
            if (mapping.TargetUserId.HasValue)
            {
                collected.Add(mapping.TargetUserId.Value);
            }
        }

        foreach (var column in context.ForeignKeyValueCounts.Values)
        {
            foreach (var pair in column)
            {
                collected.Add(pair.Key);
            }
        }

        return collected.ToArray();
    }

    private static IdentifierMode DetermineIdentifierMode(IReadOnlyCollection<UserIdentifier> identifiers)
    {
        var seenNumeric = false;
        var seenGuid = false;
        var seenText = false;

        foreach (var identifier in identifiers)
        {
            if (identifier.NumericValue.HasValue)
            {
                seenNumeric = true;
                continue;
            }

            if (identifier.IsGuid)
            {
                seenGuid = true;
                continue;
            }

            seenText = true;
        }

        if (seenText || (seenGuid && seenNumeric))
        {
            return IdentifierMode.Text;
        }

        if (seenGuid)
        {
            return IdentifierMode.Guid;
        }

        return IdentifierMode.Numeric;
    }

    private enum IdentifierMode
    {
        Numeric,
        Guid,
        Text
    }

    private static long GetPendingRowCount(UatUsersContext context, UserIdentifier sourceUserId)
    {
        long total = 0;
        foreach (var column in context.UserFkCatalog)
        {
            if (context.ForeignKeyValueCounts.TryGetValue(column, out var values) && values.TryGetValue(sourceUserId, out var count))
            {
                total += count;
            }
        }

        return total;
    }
}
