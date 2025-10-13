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
        if (context.UserMap.Count == 0 || context.UserFkCatalog.Count == 0)
        {
            yield break;
        }

        foreach (var column in context.UserFkCatalog)
        {
            foreach (var mapping in context.UserMap)
            {
                yield return new[]
                {
                    column.SchemaName,
                    column.TableName,
                    column.ColumnName,
                    mapping.SourceUserId.ToString(CultureInfo.InvariantCulture),
                    mapping.TargetUserId.ToString(CultureInfo.InvariantCulture),
                    string.Empty
                };
            }
        }
    }
}
