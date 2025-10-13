using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers.Steps;

internal sealed class DiscoverUserFkCatalogStep : RemapUsersPipelineStep
{
    public DiscoverUserFkCatalogStep()
        : base("discover-user-fk-catalog")
    {
    }

    protected override async Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        var foreignKeys = await context.SchemaGraph
            .GetForeignKeysAsync(cancellationToken)
            .ConfigureAwait(false);

        if (foreignKeys.Count == 0)
        {
            context.State.ReplaceForeignKeyCatalog(Array.Empty<UserForeignKeyCatalogEntry>());
            context.Telemetry.Info(Name, "Schema graph reported no foreign keys. Control catalog remains empty.");
            return;
        }

        var targetTableName = context.UserTable;
        var primaryKeyColumn = context.UserPrimaryKeyColumn;
        var comparer = StringComparer.OrdinalIgnoreCase;

        var fkLookup = foreignKeys
            .GroupBy(fk => (fk.TargetTable.Schema, fk.TargetTable.Name, fk.TargetColumn), TargetKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.ToImmutableArray(), TargetKeyComparer.Instance);

        var queue = new Queue<CatalogTraversalNode>();
        var visited = new HashSet<CatalogTraversalKey>(CatalogTraversalKeyComparer.Instance);
        var catalogEntries = new List<UserForeignKeyCatalogEntry>();

        foreach (var fk in foreignKeys)
        {
            if (!comparer.Equals(fk.TargetTable.Name, targetTableName) ||
                !comparer.Equals(fk.TargetColumn, primaryKeyColumn))
            {
                continue;
            }

            var entry = new UserForeignKeyCatalogEntry(
                fk.SourceTable.Schema,
                fk.SourceTable.Name,
                fk.SourceColumn,
                new[] { fk.Name });

            catalogEntries.Add(entry);
            visited.Add(new CatalogTraversalKey(fk.SourceTable.Schema, fk.SourceTable.Name, fk.SourceColumn));
            queue.Enqueue(new CatalogTraversalNode(fk.SourceTable, fk.SourceColumn, entry.PathSegments.ToArray()));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = queue.Dequeue();
            var lookupKey = (node.Table.Schema, node.Table.Name, node.ColumnName);
            if (!fkLookup.TryGetValue(lookupKey, out var referencingFks))
            {
                continue;
            }

            foreach (var fk in referencingFks)
            {
                var nextKey = new CatalogTraversalKey(fk.SourceTable.Schema, fk.SourceTable.Name, fk.SourceColumn);
                if (!visited.Add(nextKey))
                {
                    continue;
                }

                var pathSegments = node.PathSegments.Concat(new[] { fk.Name }).ToArray();
                var entry = new UserForeignKeyCatalogEntry(
                    fk.SourceTable.Schema,
                    fk.SourceTable.Name,
                    fk.SourceColumn,
                    pathSegments);

                catalogEntries.Add(entry);
                queue.Enqueue(new CatalogTraversalNode(fk.SourceTable, fk.SourceColumn, pathSegments));
            }
        }

        catalogEntries.Sort(static (left, right) =>
        {
            var schemaComparison = string.Compare(left.TableSchema, right.TableSchema, StringComparison.OrdinalIgnoreCase);
            if (schemaComparison != 0)
            {
                return schemaComparison;
            }

            var tableComparison = string.Compare(left.TableName, right.TableName, StringComparison.OrdinalIgnoreCase);
            if (tableComparison != 0)
            {
                return tableComparison;
            }

            return string.Compare(left.ColumnName, right.ColumnName, StringComparison.OrdinalIgnoreCase);
        });

        context.State.ReplaceForeignKeyCatalog(catalogEntries);
        context.State.SetLoadOrder(await context.SchemaGraph.GetTopologicallySortedTablesAsync(cancellationToken).ConfigureAwait(false));

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["catalog.count"] = catalogEntries.Count.ToString(CultureInfo.InvariantCulture),
            ["userTable"] = context.UserTable
        };

        context.Telemetry.Info(Name, "Discovered foreign keys referencing user table.", metadata);
    }

    private sealed record CatalogTraversalNode(
        SchemaTable Table,
        string ColumnName,
        IReadOnlyList<string> PathSegments);

    private sealed record CatalogTraversalKey(string Schema, string Table, string Column);

    private sealed class CatalogTraversalKeyComparer : IEqualityComparer<CatalogTraversalKey>
    {
        public static CatalogTraversalKeyComparer Instance { get; } = new();

        public bool Equals(CatalogTraversalKey? x, CatalogTraversalKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CatalogTraversalKey obj)
        {
            if (obj is null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
        }
    }

    private sealed class TargetKeyComparer : IEqualityComparer<(string Schema, string Table, string Column)>
    {
        public static TargetKeyComparer Instance { get; } = new();

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
