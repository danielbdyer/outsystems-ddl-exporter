using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public static class EntityDependencySorter
{
    public static EntityDependencyOrderingResult SortByForeignKeys(
        IReadOnlyList<StaticEntityTableData> tables,
        OsmModel? model)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return EntityDependencyOrderingResult.Empty(model is not null);
        }

        var materialized = tables
            .Where(static table => table is not null)
            .ToImmutableArray();

        var alphabetical = SortAlphabetically(materialized);
        if (materialized.IsDefaultOrEmpty)
        {
            return new EntityDependencyOrderingResult(
                alphabetical,
                NodeCount: 0,
                EdgeCount: 0,
                MissingEdgeCount: 0,
                ModelAvailable: model is not null,
                CycleDetected: false,
                AlphabeticalFallbackApplied: false);
        }

        if (model is null)
        {
            return new EntityDependencyOrderingResult(
                alphabetical,
                materialized.Length,
                EdgeCount: 0,
                MissingEdgeCount: 0,
                ModelAvailable: false,
                CycleDetected: false,
                AlphabeticalFallbackApplied: false);
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

        var edges = nodes.Keys.ToDictionary(
            static key => key,
            _ => new HashSet<TableKey>(comparer),
            comparer);

        var indegree = nodes.Keys.ToDictionary(static key => key, _ => 0, comparer);

        var graphStats = BuildDependencyGraph(model, nodes, edges, indegree, comparer);

        if (nodes.Count <= 1)
        {
            return new EntityDependencyOrderingResult(
                alphabetical,
                nodes.Count,
                graphStats.EdgeCount,
                graphStats.MissingEdgeCount,
                ModelAvailable: true,
                CycleDetected: false,
                AlphabeticalFallbackApplied: false);
        }

        var ordered = TopologicalSort(nodes, edges, indegree, comparer);
        var cycleDetected = ordered.Length != nodes.Count;
        var fallbackApplied = false;

        if (cycleDetected)
        {
            // Cycles detected. Append remaining nodes using the alphabetical fallback
            var remainingKeys = nodes.Keys
                .Where(key => ordered.All(table => !TableKey.Equals(table.Definition, key)))
                .ToArray();

            if (remainingKeys.Length > 0)
            {
                var fallback = SortAlphabetically(remainingKeys.Select(key => nodes[key]).ToImmutableArray());
                ordered = ordered.AddRange(fallback);
                fallbackApplied = true;
            }
        }

        return new EntityDependencyOrderingResult(
            ordered,
            nodes.Count,
            graphStats.EdgeCount,
            graphStats.MissingEdgeCount,
            ModelAvailable: true,
            CycleDetected: cycleDetected,
            AlphabeticalFallbackApplied: fallbackApplied);
    }

    private static DependencyGraphStatistics BuildDependencyGraph(
        OsmModel model,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
        TableKeyComparer comparer)
    {
        var edgeCount = 0;
        var missingEdgeCount = 0;

        if (model.Modules.IsDefaultOrEmpty)
        {
            return new DependencyGraphStatistics(edgeCount, missingEdgeCount);
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
                            missingEdgeCount++;
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
                            edgeCount++;
                        }
                    }
                }
            }
        }

        return new DependencyGraphStatistics(edgeCount, missingEdgeCount);
    }

    private static ImmutableArray<StaticEntityTableData> TopologicalSort(
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
        TableKeyComparer comparer)
    {
        var result = ImmutableArray.CreateBuilder<StaticEntityTableData>();
        result.Capacity = nodes.Count;
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

        return result.ToImmutable();
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

    private sealed record DependencyGraphStatistics(int EdgeCount, int MissingEdgeCount);
}

public sealed record EntityDependencyOrderingResult(
    ImmutableArray<StaticEntityTableData> Tables,
    int NodeCount,
    int EdgeCount,
    int MissingEdgeCount,
    bool ModelAvailable,
    bool CycleDetected,
    bool AlphabeticalFallbackApplied)
{
    public static EntityDependencyOrderingResult Empty(bool modelAvailable)
        => new(
            ImmutableArray<StaticEntityTableData>.Empty,
            NodeCount: 0,
            EdgeCount: 0,
            MissingEdgeCount: 0,
            ModelAvailable: modelAvailable,
            CycleDetected: false,
            AlphabeticalFallbackApplied: false);

    public bool TopologicalOrderingAttempted => ModelAvailable && NodeCount > 1 && EdgeCount > 0;

    public bool TopologicalOrderingApplied =>
        TopologicalOrderingAttempted && !CycleDetected && !AlphabeticalFallbackApplied;
}
