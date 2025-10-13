using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class StageSourceSnapshotsStep : RemapUsersPipelineStep
{
    public StageSourceSnapshotsStep()
        : base("stage-source-snapshots")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var tables = context.State.LoadOrder;
        if (tables.Count == 0)
        {
            tables = await context.SchemaGraph.GetTablesAsync(cancellationToken).ConfigureAwait(false);
        }

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tableCount"] = tables.Count.ToString()
        };

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new BulkLoadRequest(
                context.SnapshotPath,
                table.Schema,
                table.Name,
                "stg",
                context.BatchSize,
                context.CommandTimeout,
                context.Parallelism);

            await context.BulkLoader.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        }

        context.Telemetry.Info(Name, "Completed staging snapshot data.", metadata);
    }
}
