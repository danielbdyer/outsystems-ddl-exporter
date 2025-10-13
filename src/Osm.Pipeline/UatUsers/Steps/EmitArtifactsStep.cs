using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class EmitArtifactsStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<EmitArtifactsStep> _logger;

    public EmitArtifactsStep(ILogger<EmitArtifactsStep>? logger = null)
    {
        _logger = logger ?? NullLogger<EmitArtifactsStep>.Instance;
    }

    public string Name => "emit-artifacts";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var previewRows = BuildPreviewRows(context).ToList();
        context.Artifacts.WriteCsv("01_preview.csv", previewRows);
        _logger.LogInformation(
            "Preview artifact written to {Path} with {DataRowCount} data rows.",
            Path.Combine(context.Artifacts.Root, "uat-users", "01_preview.csv"),
            Math.Max(previewRows.Count - 1, 0));

        var script = SqlScriptEmitter.BuildScript(context);
        context.Artifacts.WriteText("02_apply_user_remap.sql", script);
        _logger.LogInformation(
            "Apply script emitted to {Path} (Length={Length} characters).",
            Path.Combine(context.Artifacts.Root, "uat-users", "02_apply_user_remap.sql"),
            script.Length);
        return Task.CompletedTask;
    }

    private static IEnumerable<IReadOnlyList<string>> BuildPreviewRows(UatUsersContext context)
    {
        yield return new[] { "TableName", "ColumnName", "OldUserId", "NewUserId", "RowCount" };
        if (context.OrphanUserIds.Count == 0 || context.UserFkCatalog.Count == 0)
        {
            yield break;
        }

        var mappingLookup = context.UserMap
            .Where(entry => entry.TargetUserId.HasValue)
            .ToDictionary(entry => entry.SourceUserId, entry => entry.TargetUserId!.Value);
        if (mappingLookup.Count == 0)
        {
            yield break;
        }

        foreach (var column in context.UserFkCatalog)
        {
            if (!context.ForeignKeyValueCounts.TryGetValue(column, out var values))
            {
                continue;
            }

            foreach (var pair in values.OrderBy(static entry => entry.Key))
            {
                if (!context.IsOrphan(pair.Key))
                {
                    continue;
                }

                if (!mappingLookup.TryGetValue(pair.Key, out var targetUserId))
                {
                    continue;
                }

                if (pair.Value == 0)
                {
                    continue;
                }

                yield return new[]
                {
                    string.Concat(column.SchemaName, ".", column.TableName),
                    column.ColumnName,
                    pair.Key.ToString(CultureInfo.InvariantCulture),
                    targetUserId.ToString(CultureInfo.InvariantCulture),
                    pair.Value.ToString(CultureInfo.InvariantCulture)
                };
            }
        }
    }
}
