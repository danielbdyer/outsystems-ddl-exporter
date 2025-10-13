using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class EmitArtifactsStep : IPipelineStep<UatUsersContext>
{
    public string Name => "emit-artifacts";

    public Task ExecuteAsync(UatUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var previewRows = BuildPreviewRows(context);
        context.Artifacts.WriteCsv("01_preview.csv", previewRows);

        var script = SqlScriptEmitter.BuildScript(context);
        context.Artifacts.WriteText("02_apply_user_remap.sql", script);
        return Task.CompletedTask;
    }

    private static IEnumerable<IReadOnlyList<string>> BuildPreviewRows(UatUsersContext context)
    {
        yield return new[] { "Schema", "Table", "Column", "SourceUserId", "TargetUserId", "RowCount" };
        if (context.OrphanUserIds.Count == 0 || context.UserFkCatalog.Count == 0)
        {
            yield break;
        }

        var mappingLookup = context.UserMap.ToDictionary(entry => entry.SourceUserId, entry => entry);
        var orphanSet = new HashSet<long>(context.OrphanUserIds);

        foreach (var column in context.UserFkCatalog)
        {
            if (!context.ForeignKeyValueCounts.TryGetValue(column, out var values))
            {
                continue;
            }

            foreach (var pair in values.OrderBy(static entry => entry.Key))
            {
                if (!orphanSet.Contains(pair.Key))
                {
                    continue;
                }

                var target = mappingLookup.TryGetValue(pair.Key, out var mapping) && mapping.TargetUserId.HasValue
                    ? mapping.TargetUserId.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;

                yield return new[]
                {
                    column.SchemaName,
                    column.TableName,
                    column.ColumnName,
                    pair.Key.ToString(CultureInfo.InvariantCulture),
                    target,
                    pair.Value.ToString(CultureInfo.InvariantCulture)
                };
            }
        }
    }
}
