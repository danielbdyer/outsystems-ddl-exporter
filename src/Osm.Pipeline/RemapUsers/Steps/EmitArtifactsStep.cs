using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class EmitArtifactsStep : RemapUsersPipelineStep
{
    public EmitArtifactsStep()
        : base("emit-artifacts")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        if (context.State.UserMapReport is { } coverage)
        {
            await context.ArtifactWriter.WriteJsonAsync(
                "user-map.coverage.json",
                new
                {
                    sourceEnv = context.SourceEnvironment,
                    coverage = coverage.Coverage
                        .Select(row => new { reason = row.MatchReason, count = row.MatchedCount })
                        .ToArray(),
                    unresolved = coverage.UnresolvedCount,
                    sampleUnresolved = coverage.SampleUnresolvedIdentifiers
                },
                cancellationToken).ConfigureAwait(false);

            var coverageRows = new List<IReadOnlyList<string>> { new[] { "MatchReason", "MatchedCount" } };
            coverageRows.AddRange(coverage.Coverage.Select(row => new[]
            {
                row.MatchReason,
                row.MatchedCount.ToString(CultureInfo.InvariantCulture)
            }));

            await context.ArtifactWriter.WriteCsvAsync(
                "user-map.coverage.csv",
                coverageRows,
                cancellationToken).ConfigureAwait(false);
        }

        if (context.State.DryRunSummary is { } dryRun)
        {
            var deltaRows = new List<IReadOnlyList<string>>
            {
                new[] { "TableSchema", "TableName", "ColumnName", "Remapped", "Reassigned", "Pruned", "Unmapped" }
            };

            deltaRows.AddRange(dryRun.ColumnChanges.Select(change => new[]
            {
                change.TableSchema,
                change.TableName,
                change.ColumnName,
                change.RemappedRows.ToString(CultureInfo.InvariantCulture),
                change.ReassignedRows.ToString(CultureInfo.InvariantCulture),
                change.PrunedRows.ToString(CultureInfo.InvariantCulture),
                change.UnmappedRows.ToString(CultureInfo.InvariantCulture)
            }));

            await context.ArtifactWriter.WriteCsvAsync(
                "fk-rewrites.delta.csv",
                deltaRows,
                cancellationToken).ConfigureAwait(false);

            await context.ArtifactWriter.WriteJsonAsync(
                "dry-run.summary.json",
                new
                {
                    totals = new
                    {
                        remapped = dryRun.TotalRemapped,
                        reassigned = dryRun.TotalReassigned,
                        pruned = dryRun.TotalPruned,
                        unmapped = dryRun.TotalUnmapped
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (context.State.PostLoadValidation is { } validation)
        {
            await context.ArtifactWriter.WriteJsonAsync(
                "postload.validation.json",
                new
                {
                    disabledForeignKeys = validation.DisabledForeignKeys,
                    untrustedForeignKeys = validation.UntrustedForeignKeys,
                    referentialIntegrityVerified = validation.ReferentialIntegrityVerified,
                    errors = validation.ValidationErrors
                },
                cancellationToken).ConfigureAwait(false);
        }

        var loadOrder = context.State.LoadOrder;
        if (loadOrder.Count > 0)
        {
            var lines = loadOrder.Select(table => table.QualifiedName).ToArray();
            await context.ArtifactWriter.WriteTextAsync(
                "load.order.txt",
                string.Join(Environment.NewLine, lines),
                cancellationToken).ConfigureAwait(false);
        }

        context.Telemetry.Info(Name, "Wrote remap-users artifacts to disk.");
    }
}
