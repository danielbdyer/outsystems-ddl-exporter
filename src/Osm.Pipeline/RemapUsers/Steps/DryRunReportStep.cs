using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class DryRunReportStep : RemapUsersPipelineStep
{
    public DryRunReportStep()
        : base("dry-run-report")
    {
    }

    protected override Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var deltas = context.State.RewriteSummaries
            .Select(pair => new ColumnDelta(
                pair.Key.TableSchema,
                pair.Key.TableName,
                pair.Key.ColumnName,
                pair.Value.RemappedRowCount,
                pair.Value.ReassignedRowCount,
                pair.Value.PrunedRowCount,
                pair.Value.UnmappedRowCount,
                pair.Value.Policy))
            .OrderBy(delta => delta.TableSchema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(delta => delta.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(delta => delta.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalRemapped = deltas.Sum(static delta => delta.RemappedRows);
        var totalReassigned = deltas.Sum(static delta => delta.ReassignedRows);
        var totalPruned = deltas.Sum(static delta => delta.PrunedRows);
        var totalUnmapped = deltas.Sum(static delta => delta.UnmappedRows);

        var summary = new DryRunSummary(deltas, totalRemapped, totalReassigned, totalPruned, totalUnmapped);
        context.State.SetDryRunSummary(summary);

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["columns"] = deltas.Length.ToString(CultureInfo.InvariantCulture),
            ["remapped"] = totalRemapped.ToString(CultureInfo.InvariantCulture),
            ["reassigned"] = totalReassigned.ToString(CultureInfo.InvariantCulture),
            ["pruned"] = totalPruned.ToString(CultureInfo.InvariantCulture)
        };

        context.Telemetry.Info(Name, "Aggregated dry-run delta report.", metadata);
        return Task.CompletedTask;
    }
}
