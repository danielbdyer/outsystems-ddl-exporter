using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public sealed record EntityDependencySortOptions(bool DeferJunctionTables)
{
    public static EntityDependencySortOptions Default { get; } = new(false);
}

public enum EntityDependencyOrderingMode
{
    Alphabetical,
    Topological,
    JunctionDeferred
}

public static class EntityDependencySorter
{
    public static EntityDependencyOrderingResult SortByForeignKeys(
        IReadOnlyList<StaticEntityTableData> tables,
        OsmModel? model,
        NamingOverrideOptions? namingOverrides = null,
        EntityDependencySortOptions? options = null,
        CircularDependencyOptions? circularDependencyOptions = null)
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
                AlphabeticalFallbackApplied: false,
                Mode: EntityDependencyOrderingMode.Alphabetical);
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
                AlphabeticalFallbackApplied: false,
                Mode: EntityDependencyOrderingMode.Alphabetical);
        }

        options ??= EntityDependencySortOptions.Default;

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

        namingOverrides ??= NamingOverrideOptions.Empty;

        var lookup = TableLookup.Create(nodes);
        var entityLookup = BuildEntityIdentityLookup(model);
        var classification = options.DeferJunctionTables
            ? JunctionTableClassifier.Classify(model, nodes, lookup, namingOverrides)
            : JunctionTableClassification.Disabled;
        var graphStats = BuildDependencyGraph(
            model,
            nodes,
            edges,
            indegree,
            comparer,
            namingOverrides,
            lookup,
            entityLookup);

        if (nodes.Count <= 1)
        {
            return new EntityDependencyOrderingResult(
                alphabetical,
                nodes.Count,
                graphStats.EdgeCount,
                graphStats.MissingEdgeCount,
                ModelAvailable: true,
                CycleDetected: false,
                AlphabeticalFallbackApplied: false,
                Mode: EntityDependencyOrderingMode.Alphabetical);
        }

        var ordered = TopologicalSort(nodes, edges, indegree, comparer, classification, circularDependencyOptions);
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

        var orderingMode = DetermineOrderingMode(
            modelAvailable: true,
            graphStats.EdgeCount,
            cycleDetected,
            fallbackApplied,
            classification);

        return new EntityDependencyOrderingResult(
            ordered,
            nodes.Count,
            graphStats.EdgeCount,
            graphStats.MissingEdgeCount,
            ModelAvailable: true,
            CycleDetected: cycleDetected,
            AlphabeticalFallbackApplied: fallbackApplied,
            Mode: orderingMode);
    }

    private static DependencyGraphStatistics BuildDependencyGraph(
        OsmModel model,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
        TableKeyComparer comparer,
        NamingOverrideOptions namingOverrides,
        TableLookup lookup,
        IReadOnlyDictionary<TableKey, EntityIdentity> entityLookup)
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
                var sourceCandidates = TableNameCandidates.Create(
                    entity.PhysicalName.Value,
                    namingOverrides.GetEffectiveTableName(
                        entity.Schema.Value,
                        entity.PhysicalName.Value,
                        entity.LogicalName.Value,
                        module.Name.Value));

                if (!lookup.TryResolve(
                        entity.Schema.Value,
                        sourceCandidates,
                        module.Name.Value,
                        entity.LogicalName.Value,
                        out var sourceKey) ||
                    entity.Relationships.IsDefaultOrEmpty)
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
                        if (!HasValidConstraint(constraint))
                        {
                            continue;
                        }

                        var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                            ? entity.Schema.Value
                            : constraint.ReferencedSchema.Trim();

                        var referencedPhysical = constraint.ReferencedTable.Trim();
                        var referencedKey = TableKey.From(referencedSchema, referencedPhysical);
                        entityLookup.TryGetValue(referencedKey, out var targetIdentity);

                        var targetLogical = targetIdentity?.LogicalName ?? relationship.TargetEntity.Value;
                        var targetModule = targetIdentity?.Module;

                        var targetCandidates = TableNameCandidates.Create(
                            referencedPhysical,
                            namingOverrides.GetEffectiveTableName(
                                referencedSchema,
                                referencedPhysical,
                                targetLogical,
                                targetModule));

                        if (!lookup.TryResolve(
                                referencedSchema,
                                targetCandidates,
                                targetModule,
                                targetLogical,
                                out var targetKey))
                        {
                            missingEdgeCount++;
                            continue;
                        }

                        if (comparer.Equals(sourceKey, targetKey))
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
        TableKeyComparer comparer,
        JunctionTableClassification classification,
        CircularDependencyOptions? circularDependencyOptions)
    {
        var result = ImmutableArray.CreateBuilder<StaticEntityTableData>();
        result.Capacity = nodes.Count;
        var readyComparer = new ReadyQueueComparer(nodes, classification, circularDependencyOptions);
        var ready = nodes.Keys
            .Where(key => indegree.TryGetValue(key, out var degree) && degree == 0)
            .ToList();
        ready.Sort(readyComparer);

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
                    InsertSorted(ready, neighbor, readyComparer);
                }
            }
        }

        return result.ToImmutable();
    }

    private static void InsertSorted(
        IList<TableKey> ready,
        TableKey key,
        IComparer<TableKey> comparer)
    {
        var index = 0;
        for (; index < ready.Count; index++)
        {
            var comparison = comparer.Compare(key, ready[index]);
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

    private static bool HasValidConstraint(RelationshipActualConstraint constraint)
    {
        if (constraint is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(constraint.ReferencedTable))
        {
            return false;
        }

        if (constraint.Columns.IsDefaultOrEmpty)
        {
            return false;
        }

        return constraint.Columns.Any(static column =>
            !string.IsNullOrWhiteSpace(column.OwnerColumn) &&
            !string.IsNullOrWhiteSpace(column.ReferencedColumn));
    }

    private static EntityDependencyOrderingMode DetermineOrderingMode(
        bool modelAvailable,
        int edgeCount,
        bool cycleDetected,
        bool fallbackApplied,
        JunctionTableClassification classification)
    {
        if (!modelAvailable || cycleDetected || fallbackApplied)
        {
            return EntityDependencyOrderingMode.Alphabetical;
        }

        if (classification.HasPrioritizedJunctions)
        {
            return EntityDependencyOrderingMode.JunctionDeferred;
        }

        return edgeCount > 0
            ? EntityDependencyOrderingMode.Topological
            : EntityDependencyOrderingMode.Alphabetical;
    }

    private static IReadOnlyDictionary<TableKey, EntityIdentity> BuildEntityIdentityLookup(OsmModel model)
    {
        var comparer = new TableKeyComparer();
        var lookup = new Dictionary<TableKey, EntityIdentity>(comparer);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var key = TableKey.From(entity.Schema.Value, entity.PhysicalName.Value);
                lookup[key] = new EntityIdentity(module.Name.Value, entity.LogicalName.Value);
            }
        }

        return lookup;
    }

    private sealed record TableKey(string Schema, string Table)
    {
        public static TableKey From(StaticEntitySeedTableDefinition definition)
        {
            var physicalName = string.IsNullOrWhiteSpace(definition.PhysicalName)
                ? definition.EffectiveName
                : definition.PhysicalName;

            return new TableKey(
                definition.Schema ?? string.Empty,
                physicalName ?? string.Empty);
        }

        public static TableKey From(string schema, string table)
        {
            return new TableKey(schema ?? string.Empty, table ?? string.Empty);
        }

        public static bool Equals(StaticEntitySeedTableDefinition definition, TableKey key)
            => new TableKeyComparer().Equals(From(definition), key);
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

    private sealed class TableLookup
    {
        private readonly IReadOnlyDictionary<TableKey, StaticEntityTableData> _nodes;
        private readonly IDictionary<TableKey, TableKey> _effectiveLookup;
        private readonly IDictionary<ModuleEntityKey, TableKey> _moduleLookup;

        private TableLookup(
            IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
            IDictionary<TableKey, TableKey> effectiveLookup,
            IDictionary<ModuleEntityKey, TableKey> moduleLookup)
        {
            _nodes = nodes;
            _effectiveLookup = effectiveLookup;
            _moduleLookup = moduleLookup;
        }

        public static TableLookup Create(IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
        {
            var comparer = new TableKeyComparer();
            var effectiveLookup = new Dictionary<TableKey, TableKey>(comparer);
            var moduleLookup = new Dictionary<ModuleEntityKey, TableKey>(ModuleEntityKeyComparer.Instance);

            foreach (var (key, table) in nodes)
            {
                var definition = table.Definition;
                if (!string.IsNullOrWhiteSpace(definition.EffectiveName))
                {
                    var alias = TableKey.From(definition.Schema ?? string.Empty, definition.EffectiveName);
                    if (!effectiveLookup.ContainsKey(alias))
                    {
                        effectiveLookup[alias] = key;
                    }
                }

                if (!string.IsNullOrWhiteSpace(definition.Module) &&
                    !string.IsNullOrWhiteSpace(definition.LogicalName))
                {
                    var moduleKey = new ModuleEntityKey(definition.Module!, definition.LogicalName!);
                    if (!moduleLookup.ContainsKey(moduleKey))
                    {
                        moduleLookup[moduleKey] = key;
                    }
                }
            }

            return new TableLookup(nodes, effectiveLookup, moduleLookup);
        }

        public bool TryResolve(
            string? schema,
            ImmutableArray<string> candidateTables,
            string? module,
            string? logicalName,
            out TableKey key)
        {
            var normalizedSchema = NormalizeSchema(schema);

            if (!candidateTables.IsDefaultOrEmpty)
            {
                foreach (var candidate in candidateTables)
                {
                    if (TryResolveBySchema(normalizedSchema, candidate, out key))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(module) && !string.IsNullOrWhiteSpace(logicalName))
            {
                var moduleKey = new ModuleEntityKey(module.Trim(), logicalName.Trim());
                if (_moduleLookup.TryGetValue(moduleKey, out var resolvedModule))
                {
                    key = resolvedModule;
                    return true;
                }
            }

            key = default!;
            return false;
        }

        private bool TryResolveBySchema(string schema, string? table, out TableKey key)
        {
            if (string.IsNullOrWhiteSpace(table))
            {
                key = default!;
                return false;
            }

            var normalized = table.Trim();
            var candidate = new TableKey(schema, normalized);
            if (_nodes.ContainsKey(candidate))
            {
                key = candidate;
                return true;
            }

            if (_effectiveLookup.TryGetValue(candidate, out var resolvedAlias))
            {
                key = resolvedAlias;
                return true;
            }

            key = default!;
            return false;
        }

        private static string NormalizeSchema(string? schema)
            => string.IsNullOrWhiteSpace(schema) ? string.Empty : schema.Trim();
    }

    private sealed record ModuleEntityKey(string Module, string LogicalName);

    private sealed class ModuleEntityKeyComparer : IEqualityComparer<ModuleEntityKey>
    {
        public static ModuleEntityKeyComparer Instance { get; } = new();

        public bool Equals(ModuleEntityKey? x, ModuleEntityKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Module, y.Module, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.LogicalName, y.LogicalName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ModuleEntityKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Module ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LogicalName ?? string.Empty));
        }
    }

    private sealed class TableNameCandidates
    {
        public static ImmutableArray<string> Create(params string?[] names)
        {
            if (names is null || names.Length == 0)
            {
                return ImmutableArray<string>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var trimmed = name.Trim();
                if (seen.Add(trimmed))
                {
                    builder.Add(trimmed);
                }
            }

            return builder.ToImmutable();
        }
    }

    private sealed class JunctionTableClassifier
    {
        public static JunctionTableClassification Classify(
            OsmModel? model,
            IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
            TableLookup lookup,
            NamingOverrideOptions namingOverrides)
        {
            if (model is null || nodes.Count == 0 || model.Modules.IsDefaultOrEmpty)
            {
                return JunctionTableClassification.Disabled;
            }

            var comparer = new TableKeyComparer();
            var junctions = new HashSet<TableKey>(comparer);

            foreach (var module in model.Modules)
            {
                if (module.Entities.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var entity in module.Entities)
                {
                    var candidates = TableNameCandidates.Create(
                        entity.PhysicalName.Value,
                        namingOverrides.GetEffectiveTableName(
                            entity.Schema.Value,
                            entity.PhysicalName.Value,
                            entity.LogicalName.Value,
                            module.Name.Value));

                    if (!lookup.TryResolve(
                            entity.Schema.Value,
                            candidates,
                            module.Name.Value,
                            entity.LogicalName.Value,
                            out var key) ||
                        !nodes.TryGetValue(key, out var table))
                    {
                        continue;
                    }

                    if (IsJunction(entity, table))
                    {
                        junctions.Add(key);
                    }
                }
            }

            return junctions.Count == 0
                ? JunctionTableClassification.Disabled
                : new JunctionTableClassification(junctions, enabled: true);
        }

        private static bool IsJunction(EntityModel entity, StaticEntityTableData table)
        {
            if (entity.Relationships.IsDefaultOrEmpty)
            {
                return false;
            }

            var definition = table.Definition;
            if (definition.Columns.IsDefaultOrEmpty)
            {
                return false;
            }

            var nonKeyAliases = definition.Columns
                .Where(static column => !column.IsPrimaryKey)
                .SelectMany(GetColumnAliases)
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (nonKeyAliases.Length < 2)
            {
                return false;
            }

            var nonKeySet = new HashSet<string>(nonKeyAliases, StringComparer.OrdinalIgnoreCase);
            var fkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownerKey = CreateEntityKey(definition.Schema ?? entity.Schema.Value, definition.PhysicalName ?? entity.PhysicalName.Value);

            foreach (var relationship in entity.Relationships)
            {
                if (relationship.ActualConstraints.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var constraint in relationship.ActualConstraints)
                {
                    if (!HasValidConstraint(constraint))
                    {
                        continue;
                    }

                    var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                        ? entity.Schema.Value
                        : constraint.ReferencedSchema.Trim();

                    var referencedTable = string.IsNullOrWhiteSpace(constraint.ReferencedTable)
                        ? relationship.TargetPhysicalName.Value
                        : constraint.ReferencedTable.Trim();

                    var referencedKey = CreateEntityKey(referencedSchema, referencedTable);
                    if (string.Equals(ownerKey, referencedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var matchedColumn = false;
                    foreach (var column in constraint.Columns)
                    {
                        matchedColumn |= TrackForeignKeyColumn(column.OwnerColumn, column.OwnerAttribute, nonKeySet, fkColumns);
                    }

                    if (matchedColumn)
                    {
                        referencedTargets.Add(referencedKey);
                    }
                }
            }

            if (referencedTargets.Count < 2)
            {
                return false;
            }

            foreach (var column in definition.Columns)
            {
                if (column.IsPrimaryKey)
                {
                    continue;
                }

                if (!GetColumnAliases(column).Any(alias => fkColumns.Contains(alias)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class JunctionTableClassification
    {
        private readonly ISet<TableKey>? _junctions;
        private readonly bool _enabled;

        public static JunctionTableClassification Disabled { get; } = new(null, false);

        public JunctionTableClassification(ISet<TableKey>? junctions, bool enabled)
        {
            _junctions = junctions;
            _enabled = enabled;
        }

        public bool HasPrioritizedJunctions => _enabled && _junctions is not null && _junctions.Count > 0;

        public bool IsJunction(TableKey key)
            => HasPrioritizedJunctions && _junctions!.Contains(key);
    }

    private sealed class ReadyQueueComparer : IComparer<TableKey>
    {
        private readonly IReadOnlyDictionary<TableKey, StaticEntityTableData> _nodes;
        private readonly JunctionTableClassification _classification;
        private readonly CircularDependencyOptions? _circularDependencyOptions;
        private readonly StaticEntityTableComparer _tableComparer = new();

        public ReadyQueueComparer(
            IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
            JunctionTableClassification classification,
            CircularDependencyOptions? circularDependencyOptions = null)
        {
            _nodes = nodes;
            _classification = classification ?? JunctionTableClassification.Disabled;
            _circularDependencyOptions = circularDependencyOptions;
        }

        public int Compare(TableKey? x, TableKey? y)
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

            // Apply manual ordering from CircularDependencyOptions if configured
            if (_circularDependencyOptions is not null)
            {
                var xTableName = GetPhysicalTableName(x);
                var yTableName = GetPhysicalTableName(y);
                var xPosition = _circularDependencyOptions.GetManualPosition(xTableName);
                var yPosition = _circularDependencyOptions.GetManualPosition(yTableName);

                // If both have manual positions, compare by position (lower first)
                if (xPosition.HasValue && yPosition.HasValue)
                {
                    return xPosition.Value.CompareTo(yPosition.Value);
                }

                // If only one has a manual position, it goes before the other
                if (xPosition.HasValue)
                {
                    return -1;
                }

                if (yPosition.HasValue)
                {
                    return 1;
                }
            }

            if (_classification.HasPrioritizedJunctions)
            {
                var xJunction = _classification.IsJunction(x);
                var yJunction = _classification.IsJunction(y);
                if (xJunction != yJunction)
                {
                    return xJunction ? 1 : -1;
                }
            }

            return _tableComparer.Compare(_nodes[x], _nodes[y]);
        }

        private string GetPhysicalTableName(TableKey key)
        {
            if (!_nodes.TryGetValue(key, out var table))
            {
                return key.Table;
            }

            var definition = table.Definition;
            return !string.IsNullOrWhiteSpace(definition.PhysicalName)
                ? definition.PhysicalName
                : definition.EffectiveName ?? key.Table;
        }
    }

    private static bool TrackForeignKeyColumn(
        string? ownerColumn,
        string? ownerAttribute,
        ISet<string> nonKeyColumns,
        ISet<string> fkColumns)
    {
        var matched = false;

        if (!string.IsNullOrWhiteSpace(ownerColumn))
        {
            var normalized = ownerColumn.Trim();
            fkColumns.Add(normalized);
            matched |= nonKeyColumns.Contains(normalized);
        }

        if (!string.IsNullOrWhiteSpace(ownerAttribute))
        {
            var normalizedAttribute = ownerAttribute.Trim();
            fkColumns.Add(normalizedAttribute);
            matched |= nonKeyColumns.Contains(normalizedAttribute);
        }

        return matched;
    }

    private static IEnumerable<string> GetColumnAliases(StaticEntitySeedColumn column)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in EnumerateAliases(column))
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var normalized = alias.Trim();
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }

        static IEnumerable<string?> EnumerateAliases(StaticEntitySeedColumn column)
        {
            yield return column.ColumnName;
            yield return column.EffectiveColumnName;
            yield return column.EmissionName;
            yield return column.TargetColumnName;
            yield return column.LogicalName;
        }
    }

    private static string CreateEntityKey(string? schema, string? table)
    {
        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? string.Empty : schema.Trim();
        var normalizedTable = string.IsNullOrWhiteSpace(table) ? string.Empty : table.Trim();
        return string.IsNullOrEmpty(normalizedSchema)
            ? normalizedTable.ToLowerInvariant()
            : $"{normalizedSchema}.{normalizedTable}".ToLowerInvariant();
    }

    private sealed record EntityIdentity(string Module, string LogicalName);

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
    public sealed record EntityDependencyOrderingResult(
        ImmutableArray<StaticEntityTableData> Tables,
        int NodeCount,
        int EdgeCount,
        int MissingEdgeCount,
        bool ModelAvailable,
        bool CycleDetected,
        bool AlphabeticalFallbackApplied,
        EntityDependencyOrderingMode Mode)
    {
        public static EntityDependencyOrderingResult Empty(bool modelAvailable)
            => new(
                ImmutableArray<StaticEntityTableData>.Empty,
                NodeCount: 0,
                EdgeCount: 0,
                MissingEdgeCount: 0,
                ModelAvailable: modelAvailable,
                CycleDetected: false,
                AlphabeticalFallbackApplied: false,
                Mode: EntityDependencyOrderingMode.Alphabetical);

        public bool TopologicalOrderingAttempted =>
            ModelAvailable && NodeCount > 1 &&
            (EdgeCount > 0 || Mode == EntityDependencyOrderingMode.JunctionDeferred);

        public bool TopologicalOrderingApplied =>
            Mode != EntityDependencyOrderingMode.Alphabetical && !CycleDetected && !AlphabeticalFallbackApplied;
    }

    private sealed record DependencyGraphStatistics(int EdgeCount, int MissingEdgeCount);
}
