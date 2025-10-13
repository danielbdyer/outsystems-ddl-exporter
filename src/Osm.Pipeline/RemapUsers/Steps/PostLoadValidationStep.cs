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
}
