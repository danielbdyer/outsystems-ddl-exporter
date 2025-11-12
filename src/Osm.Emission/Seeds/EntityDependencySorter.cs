using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public static class EntityDependencySorter
{
    public static ImmutableArray<StaticEntityTableData> SortByForeignKeys(
        IReadOnlyList<StaticEntityTableData> tables,
        OsmModel? model)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return ImmutableArray<StaticEntityTableData>.Empty;
        }

        var materialized = tables
            .Where(static table => table is not null)
            .ToImmutableArray();

        if (materialized.IsDefaultOrEmpty || model is null)
        {
            return SortAlphabetically(materialized);
        }

        var comparer = new TableKeyComparer();
        var nodes = new Dictionary<TableKey, StaticEntityTableData>(comparer);

        foreach (var table in materialized)
        {
            var key = TableKey.From(table.Definition);
            if (!nodes.ContainsKey(key))
            {
                nodes[key] = table;
            }
        }

        if (nodes.Count <= 1)
        {
            return SortAlphabetically(materialized);
        }

        var edges = nodes.Keys.ToDictionary(
            static key => key,
            _ => new HashSet<TableKey>(comparer),
            comparer);

        var indegree = nodes.Keys.ToDictionary(static key => key, _ => 0, comparer);

        BuildDependencyGraph(model, nodes, edges, indegree, comparer);

        var ordered = TopologicalSort(nodes, edges, indegree, comparer);
        if (ordered.Length == nodes.Count)
        {
            return ordered;
        }

        // Cycles detected. Append remaining nodes using the alphabetical fallback
        var remainingKeys = nodes.Keys
            .Where(key => ordered.All(table => !TableKey.Equals(table.Definition, key)))
            .ToArray();

        if (remainingKeys.Length == 0)
        {
            return ordered;
        }

        var fallback = SortAlphabetically(remainingKeys.Select(key => nodes[key]).ToImmutableArray());
        return ordered.AddRange(fallback);
    }

    private static void BuildDependencyGraph(
        OsmModel model,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
        TableKeyComparer comparer)
    {
        if (model.Modules.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var sourceKey = TableKey.From(entity.Schema.Value, entity.PhysicalName.Value);
                if (!nodes.ContainsKey(sourceKey) || entity.Relationships.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var relationship in entity.Relationships)
                {
                    if (relationship.ActualConstraints.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    foreach (var constraint in relationship.ActualConstraints)
                    {
                        if (string.IsNullOrWhiteSpace(constraint.ReferencedTable))
                        {
                            continue;
                        }

                        var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                            ? entity.Schema.Value
                            : constraint.ReferencedSchema.Trim();

                        var targetKey = TableKey.From(referencedSchema, constraint.ReferencedTable.Trim());
                        if (!nodes.ContainsKey(targetKey))
                        {
                            continue;
                        }

                        if (comparer.Equals(sourceKey, targetKey))
                        {
                            continue;
                        }

                        if (constraint.Columns.IsDefaultOrEmpty ||
                            !constraint.Columns.Any(static column =>
                                !string.IsNullOrWhiteSpace(column.OwnerColumn) &&
                                !string.IsNullOrWhiteSpace(column.ReferencedColumn)))
                        {
                            continue;
                        }

                        var dependents = edges[targetKey];

                        if (dependents.Add(sourceKey))
                        {
                            indegree[sourceKey] = indegree[sourceKey] + 1;
                        }
                    }
                }
            }
        }
    }

    private static ImmutableArray<StaticEntityTableData> TopologicalSort(
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
        TableKeyComparer comparer)
    {
        var result = ImmutableArray.CreateBuilder<StaticEntityTableData>(nodes.Count);
        var ready = nodes.Keys
            .Where(key => indegree.TryGetValue(key, out var degree) && degree == 0)
            .OrderBy(key => nodes[key], new StaticEntityTableComparer())
            .ToList();

        while (ready.Count > 0)
        {
            var currentKey = ready[0];
            ready.RemoveAt(0);

            result.Add(nodes[currentKey]);

            if (!edges.TryGetValue(currentKey, out var neighbors) || neighbors.Count == 0)
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                indegree[neighbor] = Math.Max(0, indegree[neighbor] - 1);
                if (indegree[neighbor] == 0)
                {
                    InsertSorted(ready, neighbor, nodes);
                }
            }
        }

        return result.MoveToImmutable();
    }

    private static void InsertSorted(
        IList<TableKey> ready,
        TableKey key,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        var comparer = new StaticEntityTableComparer();
        var table = nodes[key];
        var index = 0;
        for (; index < ready.Count; index++)
        {
            var comparison = comparer.Compare(table, nodes[ready[index]]);
            if (comparison < 0)
            {
                break;
            }
        }

        ready.Insert(index, key);
    }

    private static ImmutableArray<StaticEntityTableData> SortAlphabetically(
        ImmutableArray<StaticEntityTableData> tables)
    {
        if (tables.IsDefaultOrEmpty)
        {
            return ImmutableArray<StaticEntityTableData>.Empty;
        }

        return tables
            .OrderBy(table => table, new StaticEntityTableComparer())
            .ToImmutableArray();
    }

    private sealed record TableKey(string Schema, string Table)
    {
        public static TableKey From(StaticEntitySeedTableDefinition definition)
        {
            return new TableKey(
                definition.Schema ?? string.Empty,
                definition.PhysicalName ?? string.Empty);
        }

        public static TableKey From(string schema, string table)
        {
            return new TableKey(schema ?? string.Empty, table ?? string.Empty);
        }

        public static bool Equals(StaticEntitySeedTableDefinition definition, TableKey key)
        {
            return string.Equals(definition.Schema, key.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(definition.PhysicalName, key.Table, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class TableKeyComparer : IEqualityComparer<TableKey>
    {
        public bool Equals(TableKey? x, TableKey? y)
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
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(TableKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table ?? string.Empty));
        }
    }

    private sealed class StaticEntityTableComparer : IComparer<StaticEntityTableData>
    {
        public int Compare(StaticEntityTableData? x, StaticEntityTableData? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var moduleComparison = string.Compare(
                x.Definition.Module,
                y.Definition.Module,
                StringComparison.OrdinalIgnoreCase);
            if (moduleComparison != 0)
            {
                return moduleComparison;
            }

            var logicalComparison = string.Compare(
                x.Definition.LogicalName,
                y.Definition.LogicalName,
                StringComparison.OrdinalIgnoreCase);
            if (logicalComparison != 0)
            {
                return logicalComparison;
            }

            return string.Compare(
                x.Definition.EffectiveName,
                y.Definition.EffectiveName,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
