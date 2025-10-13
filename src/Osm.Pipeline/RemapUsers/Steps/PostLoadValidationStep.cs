using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        var referentialProbes = new List<ReferentialProbeResult>();

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

        foreach (var entry in context.State.ForeignKeyCatalog)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var summarySql = $@"SELECT
    SUM(CASE WHEN u.[{context.UserPrimaryKeyColumn}] IS NOT NULL THEN 1 ELSE 0 END) AS ValidCount,
    SUM(CASE WHEN u.[{context.UserPrimaryKeyColumn}] IS NULL AND b.[{entry.ColumnName}] IS NOT NULL THEN 1 ELSE 0 END) AS InvalidCount
FROM {entry.QualifiedTable} b
LEFT JOIN [{context.UserTableSchema}].[{context.UserTableName}] u
  ON u.[{context.UserPrimaryKeyColumn}] = b.[{entry.ColumnName}];";

            var summary = await context.SqlRunner.QueryAsync(
                summarySql,
                context.BuildCommonParameters(),
                static record => (
                    Valid: record.IsDBNull(0) ? 0L : record.GetInt64(0),
                    Invalid: record.IsDBNull(1) ? 0L : record.GetInt64(1)),
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            var validCount = summary.Count > 0 ? summary[0].Valid : 0L;
            var invalidCount = summary.Count > 0 ? summary[0].Invalid : 0L;

            var sampleSql = $@"SELECT TOP (5) b.[{entry.ColumnName}]
FROM {entry.QualifiedTable} b
LEFT JOIN [{context.UserTableSchema}].[{context.UserTableName}] u
  ON u.[{context.UserPrimaryKeyColumn}] = b.[{entry.ColumnName}]
WHERE b.[{entry.ColumnName}] IS NOT NULL
  AND u.[{context.UserPrimaryKeyColumn}] IS NULL
ORDER BY b.[{entry.ColumnName}];";

            var samples = await context.SqlRunner.QueryAsync(
                sampleSql,
                context.BuildCommonParameters(),
                record => record.IsDBNull(0) ? string.Empty : record.GetValue(0)?.ToString() ?? string.Empty,
                context.CommandTimeout,
                cancellationToken).ConfigureAwait(false);

            referentialProbes.Add(new ReferentialProbeResult(
                entry.TableSchema,
                entry.TableName,
                entry.ColumnName,
                validCount,
                invalidCount,
                samples));
        }

        var referentialIntegrityVerified = disabledCount == 0 && untrustedCount == 0 && errors.Count == 0;
        var report = new PostLoadValidationReport(disabledCount, untrustedCount, referentialIntegrityVerified, errors);
        context.State.SetPostLoadValidation(report);
        context.State.SetReferentialProbes(referentialProbes);

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["disabledFks"] = disabledCount.ToString(CultureInfo.InvariantCulture),
            ["untrustedFks"] = untrustedCount.ToString(CultureInfo.InvariantCulture),
            ["rowMismatches"] = errors.Count.ToString(CultureInfo.InvariantCulture)
        };

        context.Telemetry.Info(Name, "Completed post-load validation checks.", metadata);
    }
}
