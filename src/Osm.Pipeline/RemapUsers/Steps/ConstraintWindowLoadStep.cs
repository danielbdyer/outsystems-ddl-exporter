using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class ConstraintWindowLoadStep : RemapUsersPipelineStep
{
    public ConstraintWindowLoadStep()
        : base("constraint-window-load")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var loadOrder = context.State.LoadOrder;
        if (loadOrder.Count == 0)
        {
            loadOrder = await context.SchemaGraph.GetTopologicallySortedTablesAsync(cancellationToken).ConfigureAwait(false);
        }

        var emptyParameters = new Dictionary<string, object?>(StringComparer.Ordinal);

        await context.SqlRunner.ExecuteInTransactionAsync(
            "RemapUsersLoad",
            context.CommandTimeout,
            async (runner, ct) =>
            {
                foreach (var table in loadOrder)
                {
                    await runner.ExecuteAsync(
                        $"ALTER TABLE {table.QualifiedName} NOCHECK CONSTRAINT ALL;",
                        emptyParameters,
                        ct).ConfigureAwait(false);
                }

                foreach (var table in loadOrder)
                {
                    await runner.ExecuteAsync(
                        $"DELETE FROM {table.QualifiedName};",
                        emptyParameters,
                        ct).ConfigureAwait(false);

                    await runner.ExecuteAsync(
                        $"INSERT INTO {table.QualifiedName} SELECT * FROM stg.[{table.Name}];",
                        emptyParameters,
                        ct).ConfigureAwait(false);
                }

                foreach (var table in loadOrder.Reverse())
                {
                    await runner.ExecuteAsync(
                        $"ALTER TABLE {table.QualifiedName} WITH CHECK CHECK CONSTRAINT ALL;",
                        emptyParameters,
                        ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        context.Telemetry.Info(Name, "Loaded staging data into base tables under constraint window.", new Dictionary<string, string?>
        {
            ["tableCount"] = loadOrder.Count.ToString()
        });
    }
}
