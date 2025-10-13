using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Steps;

public sealed class DiscoverUserFkCatalogStep : IPipelineStep<UatUsersContext>
{
    public string Name => "discover-user-fk-catalog";

    public async Task ExecuteAsync(UatUsersContext ctx, CancellationToken cancellationToken)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        var includeSet = ctx.IncludeColumns;
        var userSchema = ctx.UserSchema;
        var userTable = ctx.UserTable;
        var userIdColumn = ctx.UserIdColumn;

        var foreignKeys = await ctx.SchemaGraph.GetForeignKeysAsync(cancellationToken).ConfigureAwait(false);
        var matches = foreignKeys
            .Where(fk =>
                fk.Referenced.Schema.Equals(userSchema, StringComparison.OrdinalIgnoreCase) &&
                fk.Referenced.Table.Equals(userTable, StringComparison.OrdinalIgnoreCase))
            .SelectMany(fk => fk.Columns
                .Where(column => column.ReferencedColumn.Equals(userIdColumn, StringComparison.OrdinalIgnoreCase))
                .Select(column => new UserFkColumn(
                    fk.Parent.Schema,
                    fk.Parent.Table,
                    column.ParentColumn,
                    fk.Name)))
            .ToList();

        var deduplicated = matches
            .GroupBy(column => (column.SchemaName, column.TableName, column.ColumnName), StringTupleComparer.Instance)
            .Select(group => group
                .OrderBy(entry => entry.ForeignKeyName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();

        if (includeSet is not null && includeSet.Count > 0)
        {
            deduplicated = deduplicated
                .Where(column => includeSet.Contains(column.ColumnName))
                .ToList();
        }

        var catalog = deduplicated
            .OrderBy(column => column.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ctx.SetUserFkCatalog(catalog);

        var artifactLines = catalog
            .Select(column => string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}  -- {3}",
                column.SchemaName,
                column.TableName,
                column.ColumnName,
                column.ForeignKeyName));

        ctx.Artifacts.WriteLines("03_catalog.txt", artifactLines);
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
