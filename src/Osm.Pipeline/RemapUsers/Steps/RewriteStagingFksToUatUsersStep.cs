using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class RewriteStagingFksToUatUsersStep : RemapUsersPipelineStep
{
    public RewriteStagingFksToUatUsersStep()
        : base("rewrite-staging-fks")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var catalog = context.State.ForeignKeyCatalog;
        if (catalog.Count == 0)
        {
            context.Telemetry.Warning(Name, "User FK catalog is empty; no staging rewrites required.");
            return;
        }

        foreach (var entry in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, object?>(context.BuildCommonParameters())
            {
                ["QualifiedTable"] = entry.QualifiedTable,
                ["ColumnName"] = entry.ColumnName
            };

            var rewriteSql = BuildRewriteSql(entry);
            var unmappedCount = await context.SqlRunner.ExecuteScalarAsync<long?>(
                BuildUnmappedCountSql(entry),
                parameters,
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false) ?? 0L;

            var remappedRows = await context.SqlRunner.ExecuteAsync(
                rewriteSql,
                parameters,
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            long reassigned = 0;
            long pruned = 0;

            if (unmappedCount > 0)
            {
                if (context.Policy == RemapUsersPolicy.Reassign)
                {
                    if (!context.FallbackUserId.HasValue)
                    {
                        throw new InvalidOperationException("Fallback user id must be provided when policy is set to reassign.");
                    }

                    var reassignSql = BuildReassignSql(entry);
                    reassigned = await context.SqlRunner.ExecuteAsync(
                        reassignSql,
                        parameters,
                        context.CommandTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var pruneSql = BuildPruneSql(entry);
                    pruned = await context.SqlRunner.ExecuteAsync(
                        pruneSql,
                        parameters,
                        context.CommandTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var summary = new ColumnRewriteSummary(remappedRows, reassigned, pruned, unmappedCount, context.Policy);
            context.State.RecordRewrite(entry, summary);

            var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["table"] = entry.QualifiedTable,
                ["column"] = entry.ColumnName,
                ["remapped"] = remappedRows.ToString(CultureInfo.InvariantCulture),
                ["reassigned"] = reassigned.ToString(CultureInfo.InvariantCulture),
                ["pruned"] = pruned.ToString(CultureInfo.InvariantCulture),
                ["unmapped"] = unmappedCount.ToString(CultureInfo.InvariantCulture)
            };

            context.Telemetry.Info(Name, "Rewrote staging foreign key column.", metadata);
        }
    }

    private static string BuildRewriteSql(UserForeignKeyCatalogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WITH delta AS (");
        builder.AppendLine($"    SELECT t.[{entry.ColumnName}] AS OldId, m.TargetUserId AS NewId");
        builder.AppendLine($"    FROM stg.[{entry.TableName}] t");
        builder.AppendLine("    JOIN ctl.UserMap m");
        builder.AppendLine("      ON m.SourceEnv = @SourceEnv");
        builder.AppendLine($"     AND m.SourceUserId = t.[{entry.ColumnName}]");
        builder.AppendLine("    WHERE m.TargetUserId IS NOT NULL");
        builder.AppendLine($"      AND t.[{entry.ColumnName}] <> m.TargetUserId");
        builder.AppendLine(")");
        builder.AppendLine($"UPDATE t");
        builder.AppendLine($"   SET t.[{entry.ColumnName}] = d.NewId");
        builder.AppendLine($"OUTPUT @QualifiedTable, @ColumnName, deleted.[{entry.ColumnName}], inserted.[{entry.ColumnName}], sysutcdatetime()");
        builder.AppendLine("INTO ctl.UserKeyChanges(TableName, ColumnName, OldId, NewId, ChangedAt)");
        builder.AppendLine($"FROM stg.[{entry.TableName}] t");
        builder.AppendLine("JOIN delta d ON d.OldId = t.[" + entry.ColumnName + "];");
        return builder.ToString();
    }

    private static string BuildReassignSql(UserForeignKeyCatalogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"UPDATE s");
        builder.AppendLine($"   SET s.[{entry.ColumnName}] = @FallbackUserId");
        builder.AppendLine($"FROM stg.[{entry.TableName}] s");
        builder.AppendLine("LEFT JOIN ctl.UserMap m");
        builder.AppendLine("  ON m.SourceEnv = @SourceEnv");
        builder.AppendLine($" AND m.SourceUserId = s.[{entry.ColumnName}]");
        builder.AppendLine("WHERE m.SourceUserId IS NULL;");
        return builder.ToString();
    }

    private static string BuildPruneSql(UserForeignKeyCatalogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"DELETE s");
        builder.AppendLine($"FROM stg.[{entry.TableName}] s");
        builder.AppendLine("LEFT JOIN ctl.UserMap m");
        builder.AppendLine("  ON m.SourceEnv = @SourceEnv");
        builder.AppendLine($" AND m.SourceUserId = s.[{entry.ColumnName}]");
        builder.AppendLine("WHERE m.SourceUserId IS NULL;");
        return builder.ToString();
    }

    private static string BuildUnmappedCountSql(UserForeignKeyCatalogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SELECT COUNT_BIG(1)");
        builder.AppendLine($"FROM stg.[{entry.TableName}] s");
        builder.AppendLine("LEFT JOIN ctl.UserMap m");
        builder.AppendLine("  ON m.SourceEnv = @SourceEnv");
        builder.AppendLine($" AND m.SourceUserId = s.[{entry.ColumnName}]");
        builder.AppendLine("WHERE m.SourceUserId IS NULL;");
        return builder.ToString();
    }
}
