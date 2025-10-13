using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        var builder = new StringBuilder();
        builder.AppendLine("-- uat-users generated script");
        builder.AppendLine("SET NOCOUNT ON;");
        builder.AppendLine("IF OBJECT_ID('tempdb..#UserRemap', 'U') IS NOT NULL DROP TABLE #UserRemap;");
        builder.AppendLine("CREATE TABLE #UserRemap (SourceUserId INT NOT NULL, TargetUserId INT NOT NULL);");

        if (context.UserMap.Count > 0)
        {
            builder.AppendLine("INSERT INTO #UserRemap (SourceUserId, TargetUserId) VALUES");
            for (var i = 0; i < context.UserMap.Count; i++)
            {
                var entry = context.UserMap[i];
                var suffix = i == context.UserMap.Count - 1 ? ";" : ",";
                builder.Append("    (")
                    .Append(entry.SourceUserId.ToString(CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(entry.TargetUserId.ToString(CultureInfo.InvariantCulture))
                    .Append(')')
                    .AppendLine(suffix);
            }
        }
        else
        {
            builder.AppendLine("-- Populate #UserRemap before executing updates.");
        }

        builder.AppendLine();
        builder.AppendLine("IF OBJECT_ID('tempdb..#Changes', 'U') IS NOT NULL DROP TABLE #Changes;");
        builder.AppendLine("CREATE TABLE #Changes (TableName sysname NOT NULL, ColumnName sysname NOT NULL, OldUserId INT NULL, NewUserId INT NULL, ChangedAt datetime2(3) NOT NULL);");
        builder.AppendLine();

        foreach (var column in context.UserFkCatalog)
        {
            AppendUpdateBlock(builder, column);
        }

        builder.AppendLine("SELECT TableName, ColumnName, OldUserId, NewUserId, ChangedAt FROM #Changes ORDER BY ChangedAt;");
        return builder.ToString();
    }

    private static void AppendUpdateBlock(StringBuilder builder, UserFkColumn column)
    {
        var schema = QuoteIdentifier(column.SchemaName);
        var table = QuoteIdentifier(column.TableName);
        var quotedColumn = QuoteIdentifier(column.ColumnName);
        var tableLiteral = SqlStringLiteral($"{column.SchemaName}.{column.TableName}");
        var columnLiteral = SqlStringLiteral(column.ColumnName);

        builder.AppendLine(";WITH delta AS (");
        builder.AppendLine($"    SELECT t.{quotedColumn} AS OldUserId, r.TargetUserId AS NewUserId");
        builder.AppendLine($"    FROM {schema}.{table} t");
        builder.AppendLine($"    JOIN #UserRemap r ON r.SourceUserId = t.{quotedColumn}");
        builder.AppendLine($"    WHERE t.{quotedColumn} <> r.TargetUserId");
        builder.AppendLine(")");
        builder.AppendLine("UPDATE t");
        builder.AppendLine($"   SET t.{quotedColumn} = d.NewUserId");
        builder.AppendLine($"OUTPUT {tableLiteral}, {columnLiteral}, deleted.{quotedColumn}, inserted.{quotedColumn}, SYSUTCDATETIME()");
        builder.AppendLine("  INTO #Changes(TableName, ColumnName, OldUserId, NewUserId, ChangedAt)");
        builder.AppendLine($"FROM {schema}.{table} t");
        builder.AppendLine($"JOIN delta d ON d.OldUserId = t.{quotedColumn};");
        builder.AppendLine();
    }

    private static string QuoteIdentifier(string identifier)
    {
        identifier ??= string.Empty;
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string SqlStringLiteral(string value)
    {
        value ??= string.Empty;
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
