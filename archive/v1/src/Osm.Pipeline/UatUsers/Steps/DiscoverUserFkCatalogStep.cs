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

public sealed class DiscoverUserFkCatalogStep : IPipelineStep<UatUsersContext>
{
    private readonly ILogger<DiscoverUserFkCatalogStep> _logger;

    public DiscoverUserFkCatalogStep(ILogger<DiscoverUserFkCatalogStep>? logger = null)
    {
        _logger = logger ?? NullLogger<DiscoverUserFkCatalogStep>.Instance;
    }

    public string Name => "discover-user-fk-catalog";

    public async Task ExecuteAsync(UatUsersContext ctx, CancellationToken cancellationToken)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        _logger.LogInformation(
            "Discovering foreign keys referencing {Schema}.{Table}.{Column}.",
            ctx.UserSchema,
            ctx.UserTable,
            ctx.UserIdColumn);

        var includeSet = ctx.IncludeColumns;
        var foreignKeys = (await ctx.SchemaGraph.GetForeignKeysAsync(cancellationToken).ConfigureAwait(false)).ToList();
        _logger.LogInformation("Schema graph returned {ForeignKeyCount} foreign keys.", foreignKeys.Count);

        if (ctx.SchemaGraph is ModelSchemaGraph modelGraph)
        {
            var synthetic = modelGraph.GetSyntheticUserForeignKeys(
                ctx.UserSchema,
                ctx.UserTable,
                ctx.UserIdColumn,
                ctx.UserEntityIdentifier);

            if (synthetic.Count > 0)
            {
                foreignKeys.AddRange(synthetic);
                _logger.LogInformation(
                    "Augmented catalog with {SyntheticCount} synthetic references derived from model attributes.",
                    synthetic.Count);
            }
        }

        var matches = foreignKeys
            .Where(fk =>
                fk.Referenced.Schema.Equals(ctx.UserSchema, StringComparison.OrdinalIgnoreCase) &&
                fk.Referenced.Table.Equals(ctx.UserTable, StringComparison.OrdinalIgnoreCase))
            .SelectMany(fk => fk.Columns
                .Where(column => column.ReferencedColumn.Equals(ctx.UserIdColumn, StringComparison.OrdinalIgnoreCase))
                .Select(column => new UserFkColumn(
                    fk.Parent.Schema,
                    fk.Parent.Table,
                    column.ParentColumn,
                    fk.Name)))
            .ToList();

        _logger.LogInformation("Matched {MatchCount} candidate foreign key columns.", matches.Count);

        var deduplicated = matches
            .GroupBy(column => (column.SchemaName, column.TableName, column.ColumnName), StringTupleComparer.Instance)
            .Select(group => group
                .OrderBy(entry => entry.ForeignKeyName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();

        if (includeSet is not null && includeSet.Count > 0)
        {
            var beforeFilter = deduplicated.Count;
            deduplicated = deduplicated
                .Where(column => includeSet.Contains(column.ColumnName))
                .ToList();

            _logger.LogInformation(
                "Filtered catalog from {Before} to {After} columns using include list ({IncludeCount} entries).",
                beforeFilter,
                deduplicated.Count,
                includeSet.Count);
        }

        var catalog = deduplicated
            .OrderBy(column => column.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ctx.SetUserFkCatalog(catalog);

        _logger.LogInformation(
            "Catalog finalized with {CatalogCount} columns across {TableCount} tables.",
            catalog.Count,
            catalog.Select(column => (column.SchemaName, column.TableName)).Distinct().Count());

        var artifactLines = catalog
            .Select(column => string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}  -- {3}",
                column.SchemaName,
                column.TableName,
                column.ColumnName,
                column.ForeignKeyName));

        ctx.Artifacts.WriteLines("03_catalog.txt", artifactLines);
        _logger.LogInformation(
            "Catalog artifact written to {Path}.",
            Path.Combine(ctx.Artifacts.Root, "uat-users", "03_catalog.txt"));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Schema, string Table, string Column)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Schema, string Table, string Column) x, (string Schema, string Table, string Column) y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Schema, string Table, string Column) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
        }
    }
}
