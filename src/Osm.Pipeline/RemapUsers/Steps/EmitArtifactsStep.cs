using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        await WriteCatalogArtifactsAsync(context, cancellationToken).ConfigureAwait(false);

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
                new[] { "TableSchema", "TableName", "ColumnName", "Remapped", "Reassigned", "Pruned", "Unmapped", "Policy" }
            };

            deltaRows.AddRange(dryRun.ColumnChanges.Select(change => new[]
            {
                change.TableSchema,
                change.TableName,
                change.ColumnName,
                change.RemappedRows.ToString(CultureInfo.InvariantCulture),
                change.ReassignedRows.ToString(CultureInfo.InvariantCulture),
                change.PrunedRows.ToString(CultureInfo.InvariantCulture),
                change.UnmappedRows.ToString(CultureInfo.InvariantCulture),
                change.Policy.ToString()
            }));

            await context.ArtifactWriter.WriteCsvAsync(
                "fk-rewrites.delta.csv",
                deltaRows,
                cancellationToken).ConfigureAwait(false);

            var unmappedRows = dryRun.ColumnChanges
                .Where(change => change.UnmappedRows > 0)
                .Select(change => (change.TableSchema, change.TableName, change.ColumnName, change.UnmappedRows, change.Policy))
                .ToArray();

            if (unmappedRows.Length > 0)
            {
                var unmappedCsv = new List<IReadOnlyList<string>>
                {
                    new[] { "TableSchema", "TableName", "ColumnName", "Unmapped", "Policy" }
                };

                unmappedCsv.AddRange(unmappedRows.Select(row => new[]
                {
                    row.TableSchema,
                    row.TableName,
                    row.ColumnName,
                    row.UnmappedRows.ToString(CultureInfo.InvariantCulture),
                    row.Policy.ToString()
                }));

                await context.ArtifactWriter.WriteCsvAsync(
                    "unmapped.impact.csv",
                    unmappedCsv,
                    cancellationToken).ConfigureAwait(false);
            }

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
                    },
                    policy = context.Policy.ToString()
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

        if (context.State.ReferentialProbes is { Count: > 0 } probes)
        {
            await WriteReferentialProbesAsync(context, probes, cancellationToken).ConfigureAwait(false);
        }

        if (context.DryRun)
        {
            await WriteDryRunGuardFilesAsync(context, cancellationToken).ConfigureAwait(false);
        }

        await WriteSessionLogAsync(context, cancellationToken).ConfigureAwait(false);

        context.Telemetry.Info(Name, "Wrote remap-users artifacts to disk.");
    }

    private static async Task WriteSessionLogAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("parameters:");
        AppendParameter(builder, "sourceEnv", context.SourceEnvironment);
        AppendParameter(builder, "snapshotPath", context.SnapshotPath);
        AppendParameter(builder, "policy", context.Policy.ToString());
        AppendParameter(builder, "dryRun", context.DryRun.ToString());
        AppendParameter(builder, "includePii", context.IncludePii.ToString());
        AppendParameter(builder, "rebuildMap", context.RebuildMap.ToString());
        AppendParameter(builder, "matchingRules", string.Join(",", context.MatchingRules.Select(rule => rule.ToString())));
        AppendParameter(builder, "fallbackUserId", context.FallbackUserId?.ToString(CultureInfo.InvariantCulture) ?? "");
        AppendParameter(builder, "batchSize", context.BatchSize.ToString(CultureInfo.InvariantCulture));
        AppendParameter(builder, "commandTimeoutSeconds", ((int)context.CommandTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        AppendParameter(builder, "parallelism", context.Parallelism.ToString(CultureInfo.InvariantCulture));
        AppendParameter(builder, "artifactDirectory", context.ArtifactDirectory);
        builder.AppendLine("steps:");

        foreach (var entry in context.Telemetry.Entries.OrderBy(e => e.Timestamp))
        {
            var metadata = entry.Metadata is null || entry.Metadata.Count == 0
                ? string.Empty
                : string.Join(";", entry.Metadata.Select(pair => pair.Key + "=" + pair.Value));
            builder.Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(entry.Step);
            builder.Append(' ');
            builder.Append(entry.EventType);
            if (entry.Duration.HasValue)
            {
                builder.Append(" duration=");
                builder.Append(entry.Duration.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
                builder.Append('s');
            }

            if (!string.IsNullOrWhiteSpace(metadata))
            {
                builder.Append(" metadata=");
                builder.Append(metadata);
            }

            if (!string.IsNullOrWhiteSpace(entry.Message))
            {
                builder.Append(" message=");
                builder.Append(entry.Message);
            }

            if (!string.IsNullOrWhiteSpace(entry.ExceptionType))
            {
                builder.Append(" exception=");
                builder.Append(entry.ExceptionType);
                if (!string.IsNullOrWhiteSpace(entry.ExceptionMessage))
                {
                    builder.Append(':');
                    builder.Append(entry.ExceptionMessage);
                }
            }

            builder.AppendLine();
        }

        await context.ArtifactWriter.WriteTextAsync("session.log", builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static void AppendParameter(StringBuilder builder, string name, string value)
    {
        builder.Append("  ");
        builder.Append(name);
        builder.Append('=');
        builder.AppendLine(string.IsNullOrWhiteSpace(value) ? "(empty)" : value);
    }

    private static async Task WriteCatalogArtifactsAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var catalog = context.State.ForeignKeyCatalog;
        var catalogPayload = catalog
            .Select(entry => new
            {
                tableSchema = entry.TableSchema,
                tableName = entry.TableName,
                columnName = entry.ColumnName,
                qualifiedTable = entry.QualifiedTable,
                pathSegments = entry.PathSegments
            })
            .ToArray();

        await context.ArtifactWriter.WriteJsonAsync(
            "user-fk-catalog.json",
            catalogPayload,
            cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine("# Foreign keys referencing ossys_User");
        if (catalog.Count == 0)
        {
            builder.AppendLine("(none detected)");
        }
        else
        {
            foreach (var entry in catalog)
            {
                var path = entry.PathSegments.Count == 0 ? "ossys_User" : string.Join(" -> ", entry.PathSegments);
                builder.AppendLine($"{entry.TableSchema}.{entry.TableName}.{entry.ColumnName} => {path}");
            }
        }

        await context.ArtifactWriter.WriteTextAsync("user-fk-catalog.txt", builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteDryRunGuardFilesAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var hash = RemapUsersRunHasher.ComputeHash(context.RunParameters);
        await context.ArtifactWriter.WriteTextAsync("dry-run.hash", hash, cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine("remap-users dry-run summary");
        builder.AppendLine(new string('=', 32));
        builder.AppendLine($"source-env: {context.SourceEnvironment}");
        builder.AppendLine($"snapshot: {context.SnapshotPath}");
        builder.AppendLine($"user-table: {context.UserTable}");
        builder.AppendLine($"policy: {context.Policy}");
        builder.AppendLine($"matching-rules: {string.Join(", ", context.RunParameters.MatchingRules)}");
        builder.AppendLine($"batch-size: {context.BatchSize}");
        builder.AppendLine($"parallelism: {context.Parallelism}");
        builder.AppendLine();

        if (context.State.UserMapReport is { } coverage)
        {
            var matched = coverage.Coverage.Sum(row => row.MatchedCount);
            var total = matched + coverage.UnresolvedCount;
            builder.AppendLine("user-map coverage:");
            foreach (var row in coverage.Coverage)
            {
                var percent = total > 0 ? (row.MatchedCount / (double)total) * 100d : 0d;
                builder.AppendLine($"  {row.MatchReason}: {row.MatchedCount} ({percent:F2}% of staged users)");
            }

            builder.AppendLine($"  unresolved: {coverage.UnresolvedCount}");
            if (coverage.SampleUnresolvedIdentifiers.Count > 0)
            {
                builder.AppendLine("  sample-unresolved:");
                foreach (var sample in coverage.SampleUnresolvedIdentifiers)
                {
                    builder.AppendLine("    - " + sample);
                }
            }

            builder.AppendLine();
        }

        if (context.State.DryRunSummary is { } dryRun)
        {
            builder.AppendLine("fk rewrite totals:");
            builder.AppendLine($"  remapped: {dryRun.TotalRemapped}");
            builder.AppendLine($"  reassigned: {dryRun.TotalReassigned}");
            builder.AppendLine($"  pruned: {dryRun.TotalPruned}");
            builder.AppendLine($"  unmapped: {dryRun.TotalUnmapped}");
        }

        await context.ArtifactWriter.WriteTextAsync("dry-run.summary.txt", builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteReferentialProbesAsync(
        RemapUsersContext context,
        IReadOnlyList<ReferentialProbeResult> probes,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "TableSchema", "TableName", "ColumnName", "ValidRows", "InvalidRows", "SampleInvalidUserIds" }
        };

        foreach (var probe in probes)
        {
            var sample = probe.SampleInvalidUserIds.Count == 0
                ? string.Empty
                : string.Join(';', probe.SampleInvalidUserIds.Select(context.RedactIdentifier));

            rows.Add(new[]
            {
                probe.TableSchema,
                probe.TableName,
                probe.ColumnName,
                probe.ValidRowCount.ToString(CultureInfo.InvariantCulture),
                probe.InvalidRowCount.ToString(CultureInfo.InvariantCulture),
                sample
            });
        }

        await context.ArtifactWriter.WriteCsvAsync("referential-probes.csv", rows, cancellationToken).ConfigureAwait(false);
    }
}
