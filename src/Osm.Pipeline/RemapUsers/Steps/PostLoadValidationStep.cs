using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class PostLoadValidationStep : RemapUsersPipelineStep
{
    public PostLoadValidationStep()
        : base("post-load-validation")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var disabledCount = await context.SqlRunner.ExecuteScalarAsync<int?>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1;",
            context.BuildCommonParameters(),
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false) ?? 0;

        var untrustedCount = await context.SqlRunner.ExecuteScalarAsync<int?>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_not_trusted = 1;",
            context.BuildCommonParameters(),
            context.CommandTimeout,
            cancellationToken).ConfigureAwait(false) ?? 0;

        var errors = new List<string>();
        var loadOrder = context.State.LoadOrder.Count == 0
            ? await context.SchemaGraph.GetTopologicallySortedTablesAsync(cancellationToken).ConfigureAwait(false)
            : context.State.LoadOrder;

        foreach (var table in loadOrder)
        {
            var baseCount = await context.SqlRunner.ExecuteScalarAsync<long?>(
                $"SELECT COUNT_BIG(1) FROM {table.QualifiedName};",
                context.BuildCommonParameters(),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false) ?? 0;

            var stagingCount = await context.SqlRunner.ExecuteScalarAsync<long?>(
                $"SELECT COUNT_BIG(1) FROM stg.[{table.Name}];",
                context.BuildCommonParameters(),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false) ?? 0;

            if (baseCount != stagingCount)
            {
                errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Row count mismatch for {0}: base={1}, staging={2}.",
                    table.QualifiedName,
                    baseCount,
                    stagingCount));
            }
        }

        var referentialProbes = await BuildReferentialProbesAsync(context, cancellationToken).ConfigureAwait(false);
        if (referentialProbes.Count > 0)
        {
            context.State.SetReferentialProbes(referentialProbes);
        }

        var referentialIntegrityVerified = disabledCount == 0 && untrustedCount == 0 && errors.Count == 0;
        var report = new PostLoadValidationReport(disabledCount, untrustedCount, referentialIntegrityVerified, errors);
        context.State.SetPostLoadValidation(report);

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["disabledFks"] = disabledCount.ToString(CultureInfo.InvariantCulture),
            ["untrustedFks"] = untrustedCount.ToString(CultureInfo.InvariantCulture),
            ["rowMismatches"] = errors.Count.ToString(CultureInfo.InvariantCulture)
        };

        context.Telemetry.Info(Name, "Completed post-load validation checks.", metadata);
    }

    private static async Task<IReadOnlyList<ReferentialProbeResult>> BuildReferentialProbesAsync(
        RemapUsersContext context,
        CancellationToken cancellationToken)
    {
        var catalog = context.State.ForeignKeyCatalog;
        if (catalog.Count == 0)
        {
            return Array.Empty<ReferentialProbeResult>();
        }

        var comparer = new TableColumnComparer();
        var uniqueColumns = catalog
            .GroupBy(entry => (entry.TableSchema, entry.TableName, entry.ColumnName), comparer)
            .Select(group => group.First())
            .ToArray();

        var probes = new List<ReferentialProbeResult>(uniqueColumns.Length);
        var parameters = context.BuildCommonParameters();

        foreach (var entry in uniqueColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metrics = await context.SqlRunner.QueryAsync(
                BuildMetricsSql(entry, context),
                parameters,
                static record => new ProbeMetrics(
                    record.IsDBNull(0) ? 0L : record.GetInt64(0),
                    record.IsDBNull(1) ? 0L : record.GetInt64(1)),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            var metric = metrics.Count > 0 ? metrics[0] : new ProbeMetrics(0, 0);

            var validSamples = await context.SqlRunner.QueryAsync(
                BuildSampleSql(entry, context, onlyInvalid: false),
                parameters,
                static record => FormatProbeValue(record),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<string> invalidSamples = Array.Empty<string>();
            if (metric.InvalidRows > 0)
            {
                invalidSamples = await context.SqlRunner.QueryAsync(
                    BuildSampleSql(entry, context, onlyInvalid: true),
                    parameters,
                    static record => FormatProbeValue(record),
                    context.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            probes.Add(new ReferentialProbeResult(
                entry.TableSchema,
                entry.TableName,
                entry.ColumnName,
                metric.CheckedRows,
                metric.InvalidRows,
                validSamples,
                invalidSamples));
        }

        return probes;
    }

    private static string BuildMetricsSql(UserForeignKeyCatalogEntry entry, RemapUsersContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SELECT");
        builder.AppendLine($"    SUM(CASE WHEN t.[{entry.ColumnName}] IS NOT NULL THEN 1 ELSE 0 END) AS CheckedRows,");
        builder.AppendLine($"    SUM(CASE WHEN t.[{entry.ColumnName}] IS NOT NULL AND u.[{context.UserPrimaryKeyColumn}] IS NULL THEN 1 ELSE 0 END) AS InvalidRows");
        builder.AppendLine($"FROM {entry.QualifiedTable} t");
        builder.AppendLine($"LEFT JOIN [{context.UserTableSchema}].[{context.UserTableName}] u");
        builder.AppendLine($"  ON u.[{context.UserPrimaryKeyColumn}] = t.[{entry.ColumnName}];");
        return builder.ToString();
    }

    private static string BuildSampleSql(UserForeignKeyCatalogEntry entry, RemapUsersContext context, bool onlyInvalid)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SELECT TOP (5) t.[{entry.ColumnName}]");
        builder.AppendLine($"FROM {entry.QualifiedTable} t");
        builder.AppendLine($"LEFT JOIN [{context.UserTableSchema}].[{context.UserTableName}] u");
        builder.AppendLine($"  ON u.[{context.UserPrimaryKeyColumn}] = t.[{entry.ColumnName}]");
        builder.AppendLine($"WHERE t.[{entry.ColumnName}] IS NOT NULL");
        builder.AppendLine(onlyInvalid
            ? $"  AND u.[{context.UserPrimaryKeyColumn}] IS NULL"
            : $"  AND u.[{context.UserPrimaryKeyColumn}] IS NOT NULL");
        builder.AppendLine($"ORDER BY t.[{entry.ColumnName}];");
        return builder.ToString();
    }

    private static string FormatProbeValue(IDataRecord record)
    {
        if (record.IsDBNull(0))
        {
            return string.Empty;
        }

        var value = record.GetValue(0);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private sealed record ProbeMetrics(long CheckedRows, long InvalidRows);

    private sealed class TableColumnComparer : IEqualityComparer<(string Schema, string Table, string Column)>
    {
        public bool Equals((string Schema, string Table, string Column) x, (string Schema, string Table, string Column) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table, string Column) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
        }
    }
}
