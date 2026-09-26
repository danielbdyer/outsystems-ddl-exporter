using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public sealed record EntityDependencySortOptions(
    bool DeferJunctionTables)
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
        private static readonly TableKeyComparer KeyComparer = new();

        public static EntityDependencyOrderingResult SortByForeignKeys(
            IReadOnlyList<StaticEntityTableData> tables,
            OsmModel? model,
            NamingOverrideOptions? namingOverrides = null,
            EntityDependencySortOptions? options = null,
            CircularDependencyOptions? circularDependencyOptions = null,
            ICollection<string>? diagnostics = null)
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

        var nodes = new Dictionary<TableKey, StaticEntityTableData>(KeyComparer);

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
            _ => new HashSet<TableKey>(KeyComparer),
            KeyComparer);

        var indegree = nodes.Keys.ToDictionary(static key => key, _ => 0, KeyComparer);

        namingOverrides ??= NamingOverrideOptions.Empty;

        var lookup = TableLookup.Create(nodes);
        var entityLookup = BuildEntityIdentityLookup(model);
        var entityByTable = BuildEntityLookup(model);
        var classification = options.DeferJunctionTables
            ? JunctionTableClassifier.Classify(model, nodes, lookup, namingOverrides)
            : JunctionTableClassification.Disabled;
        var graphStats = BuildDependencyGraph(
            model,
            nodes,
            edges,
            indegree,
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

        var indegreeSnapshot = new Dictionary<TableKey, int>(indegree, KeyComparer);
        var ordered = TopologicalSort(
            nodes,
            edges,
            new Dictionary<TableKey, int>(indegreeSnapshot, KeyComparer),
            classification,
            circularDependencyOptions);
        var cycleDetected = ordered.Length != nodes.Count;
        var fallbackApplied = false;
        var stronglyConnectedComponents = ImmutableArray<ImmutableHashSet<TableKey>>.Empty;

        var autoResolutionAllowed = ShouldAttemptAutomaticCycleResolution(circularDependencyOptions);

        if (cycleDetected && !autoResolutionAllowed)
        {
            diagnostics?.Add(
                "Skipping automatic asymmetric cycle detection because manual cycle ordering overrides are configured.");
        }

        if (cycleDetected)
        {
            stronglyConnectedComponents = TarjanScc.FindStronglyConnectedComponents(nodes.Keys, edges, KeyComparer);
        }

        if (cycleDetected && autoResolutionAllowed)
        {
            var remainingKeys = nodes.Keys
                .Where(key => ordered.All(table => !TableKey.Equals(table.Definition, key)))
                .ToArray();

            diagnostics?.Add($"Auto-resolution: Found {remainingKeys.Length} remaining node(s) after initial topological sort.");

            var (autoCycles, edgesToRemove) = DetectAsymmetricCycles(
                remainingKeys,
                edges,
                nodes,
                entityByTable);

            diagnostics?.Add($"Auto-resolution: Detected {autoCycles.Length} auto-cycle(s), identified {edgesToRemove.Count} edge(s) to remove.");

            if (!autoCycles.IsDefaultOrEmpty && edgesToRemove.Count > 0)
            {
                var mergedOptions = MergeCircularDependencyOptions(circularDependencyOptions, autoCycles);

                var adjustedEdges = CloneEdges(edges);
                var adjustedIndegree = new Dictionary<TableKey, int>(indegreeSnapshot, KeyComparer);
                var removedEdges = 0;

                foreach (var (target, dependent) in edgesToRemove)
                {
                    if (!adjustedEdges.TryGetValue(target, out var dependents))
                    {
                        continue;
                    }

                    if (dependents.Remove(dependent))
                    {
                        adjustedIndegree[dependent] = Math.Max(0, adjustedIndegree[dependent] - 1);
                        removedEdges++;
                    }
                }

                var retried = TopologicalSort(
                    nodes,
                    adjustedEdges,
                    adjustedIndegree,
                    classification,
                    mergedOptions);

                if (retried.Length == nodes.Count)
                {
                    diagnostics?.Add($"Auto-resolution succeeded! All {nodes.Count} table(s) ordered after removing {removedEdges} edge(s).");
                    ordered = retried;
                    cycleDetected = false;
                    graphStats = graphStats with { EdgeCount = Math.Max(0, graphStats.EdgeCount - removedEdges) };
                    circularDependencyOptions = mergedOptions;
                }
                else
                {
                    diagnostics?.Add($"Auto-resolution failed. Retry produced {retried.Length} of {nodes.Count} ordered table(s). {nodes.Count - retried.Length} table(s) remain in cycle(s).");
                }
            }
        }

        if (cycleDetected &&
            !stronglyConnectedComponents.IsDefaultOrEmpty &&
            stronglyConnectedComponents.Length > 0)
        {
            // Always attempt intelligent/manual cycle resolution (with or without manual config)
            circularDependencyOptions ??= CircularDependencyOptions.Empty;

            if (!autoResolutionAllowed)
            {
                diagnostics?.Add(
                    $"Manual cycle ordering configured; attempting to resolve {stronglyConnectedComponents.Length} strongly connected component(s).");
            }
            else
            {
                diagnostics?.Add(
                    $"Attempting intelligent cycle resolution for {stronglyConnectedComponents.Length} strongly connected component(s).");
            }

            var edgesToBreak = IdentifyEdgesToBreakFromManualOrdering(
                stronglyConnectedComponents.Select(component => component.ToHashSet(KeyComparer)).ToList(),
                edges,
                circularDependencyOptions,
                entityByTable,
                nodes);

            diagnostics?.Add(edgesToBreak.Count > 0
                ? (autoResolutionAllowed
                    ? $"Intelligent ordering identified {edgesToBreak.Count} backward edge(s) to remove."
                    : $"Manual ordering identified {edgesToBreak.Count} backward edge(s) to remove.")
                : (autoResolutionAllowed
                    ? "Intelligent ordering did not identify any backward edges to remove."
                    : "Manual ordering did not identify any backward edges to remove."));

            if (edgesToBreak.Count > 0)
            {
                var adjustedEdges = CloneEdges(edges);
                var adjustedIndegree = new Dictionary<TableKey, int>(indegreeSnapshot, KeyComparer);
                var removedEdges = 0;

                foreach (var (target, dependent) in edgesToBreak)
                {
                    if (!adjustedEdges.TryGetValue(target, out var dependents))
                    {
                        continue;
                    }

                    if (dependents.Remove(dependent))
                    {
                        adjustedIndegree[dependent] = Math.Max(0, adjustedIndegree[dependent] - 1);
                        removedEdges++;
                    }
                }

                var retried = TopologicalSort(
                    nodes,
                    adjustedEdges,
                    adjustedIndegree,
                    classification,
                    circularDependencyOptions);

                if (retried.Length == nodes.Count)
                {
                    ordered = retried;
                    cycleDetected = false;
                    graphStats = graphStats with { EdgeCount = Math.Max(0, graphStats.EdgeCount - removedEdges) };
                    diagnostics?.Add(autoResolutionAllowed
                        ? "Intelligent ordering successfully resolved the detected cycle(s)."
                        : "Manual ordering successfully resolved the detected cycle(s).");
                }
                else
                {
                    diagnostics?.Add($"Intelligent ordering partially resolved: {retried.Length}/{nodes.Count} tables ordered, cycle remains.");
                }
            }
        }

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

        // Convert SCCs to string arrays for external consumption
        ImmutableArray<ImmutableArray<string>>? sccStrings = null;
        if (!stronglyConnectedComponents.IsDefaultOrEmpty)
        {
            var builder = ImmutableArray.CreateBuilder<ImmutableArray<string>>(stronglyConnectedComponents.Length);
            foreach (var scc in stronglyConnectedComponents)
            {
                var tableNames = scc.Select(key => $"{key.Schema}.{key.Table}").ToImmutableArray();
                builder.Add(tableNames);
            }
            sccStrings = builder.ToImmutable();
        }

        return new EntityDependencyOrderingResult(
            ordered,
            nodes.Count,
            graphStats.EdgeCount,
            graphStats.MissingEdgeCount,
            ModelAvailable: true,
            CycleDetected: cycleDetected,
            AlphabeticalFallbackApplied: fallbackApplied,
            Mode: orderingMode,
            StronglyConnectedComponents: sccStrings);
    }

    private static DependencyGraphStatistics BuildDependencyGraph(
        OsmModel model,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        IDictionary<TableKey, HashSet<TableKey>> edges,
        IDictionary<TableKey, int> indegree,
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
                    // For static entities (or entities without physical FK constraints),
                    // fallback to using logical relationship metadata for topological ordering
                    if (relationship.ActualConstraints.IsDefaultOrEmpty)
                    {
                        if (!string.IsNullOrWhiteSpace(relationship.TargetPhysicalName.Value))
                        {
                            var referencedSchema = entity.Schema.Value;
                            var referencedPhysical = relationship.TargetPhysicalName.Value.Trim();
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

                            if (lookup.TryResolve(
                                    referencedSchema,
                                    targetCandidates,
                                    targetModule,
                                    targetLogical,
                                    out var targetKey))
                            {
                                if (!KeyComparer.Equals(sourceKey, targetKey))
                                {
                                    var dependents = edges[targetKey];

                                    if (dependents.Add(sourceKey))
                                    {
                                        indegree[sourceKey] = indegree[sourceKey] + 1;
                                        edgeCount++;
                                    }
                                }
                            }
                            else
                            {
                                missingEdgeCount++;
                            }
                        }

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

                        if (KeyComparer.Equals(sourceKey, targetKey))
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

    private static bool ShouldAttemptAutomaticCycleResolution(CircularDependencyOptions? circularDependencyOptions)
    {
        return circularDependencyOptions is null ||
               circularDependencyOptions.AllowedCycles.IsDefaultOrEmpty ||
               circularDependencyOptions.AllowedCycles.Length == 0;
    }

    private static IReadOnlyDictionary<TableKey, EntityIdentity> BuildEntityIdentityLookup(OsmModel model)
    {
        var lookup = new Dictionary<TableKey, EntityIdentity>(KeyComparer);

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

    private static IReadOnlyDictionary<TableKey, EntityModel> BuildEntityLookup(OsmModel model)
    {
        var lookup = new Dictionary<TableKey, EntityModel>(KeyComparer);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var key = TableKey.From(entity.Schema.Value, entity.PhysicalName.Value);
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = entity;
                }
            }
        }

        return lookup;
    }

    private static Dictionary<TableKey, HashSet<TableKey>> CloneEdges(
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges)
    {
        var clone = new Dictionary<TableKey, HashSet<TableKey>>(KeyComparer);
        foreach (var (key, neighbors) in edges)
        {
            clone[key] = new HashSet<TableKey>(neighbors, KeyComparer);
        }

        return clone;
    }

    private static List<(TableKey Target, TableKey Dependent)> IdentifyEdgesToBreakFromManualOrdering(
        List<HashSet<TableKey>> stronglyConnectedComponents,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        CircularDependencyOptions circularDependencyOptions,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        var edgesToBreak = new List<(TableKey Target, TableKey Dependent)>();

        foreach (var component in stronglyConnectedComponents)
        {
            // Strategy 1: If manual positions exist, use them to identify backward edges
            var hasManualPositions = component.Any(n => circularDependencyOptions.GetManualPosition(n.Table).HasValue);
            
            if (hasManualPositions)
            {
                // Use position-based approach
                var positionMap = BuildEffectivePositionMap(component, edges, circularDependencyOptions, entityLookup);
                
                foreach (var node in component)
                {
                    if (!positionMap.TryGetValue(node, out var sourcePosition))
                    {
                        continue;
                    }

                    if (!edges.TryGetValue(node, out var dependents))
                    {
                        continue;
                    }

                    foreach (var dependent in dependents)
                    {
                        if (!component.Contains(dependent))
                        {
                            continue;
                        }

                        if (!positionMap.TryGetValue(dependent, out var targetPosition))
                        {
                            continue;
                        }

                        if (sourcePosition > targetPosition)
                        {
                            edgesToBreak.Add((node, dependent));
                        }
                    }
                }
            }
            else
            {
                // Strategy 2: No manual positions - break weak edges preferentially
                // This is the intelligent auto-resolution path
                var componentSet = component.ToImmutableHashSet(KeyComparer);
                var weakEdges = FindWeakEdgesInComponent(componentSet, edges, entityLookup, nodes);
                edgesToBreak.AddRange(weakEdges);
            }
        }

        return edgesToBreak;
    }

    /// <summary>
    /// Builds position map for cycle members using manual config where available,
    /// and auto-generating positions for unconfigured tables based on relationship analysis.
    /// 
    /// Strategy:
    /// 1. Use manual positions where provided
    /// 2. For unconfigured tables, analyze relationship strength (nullable = weak)
    /// 3. Compute dependency depth (how many strong dependencies does each table have?)
    /// 4. Position tables with fewer strong dependencies earlier (they're more "foundational")
    /// </summary>
    private static Dictionary<TableKey, int> BuildEffectivePositionMap(
        HashSet<TableKey> component,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        CircularDependencyOptions circularDependencyOptions,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup)
    {
        var positionMap = new Dictionary<TableKey, int>(KeyComparer);

        // First pass: collect all manual positions
        var manualPositions = new List<(TableKey Key, int Position)>();
        var maxManualPosition = -1;

        foreach (var node in component)
        {
            var manualPos = circularDependencyOptions.GetManualPosition(node.Table);
            if (manualPos.HasValue)
            {
                manualPositions.Add((node, manualPos.Value));
                maxManualPosition = Math.Max(maxManualPosition, manualPos.Value);
            }
        }

        // Add manual positions to map
        foreach (var (key, position) in manualPositions)
        {
            positionMap[key] = position;
        }

        // For tables without manual positions, compute relationship-based ordering
        var unconfiguredTables = component.Where(n => !positionMap.ContainsKey(n)).ToList();
        
        if (unconfiguredTables.Count > 0)
        {
            // Analyze relationship strength for each table
            var tableScores = new Dictionary<TableKey, TableDependencyScore>(KeyComparer);
            
            foreach (var table in unconfiguredTables)
            {
                var score = ComputeDependencyScore(table, component, edges, entityLookup);
                tableScores[table] = score;
            }

            // Sort by dependency score: tables with fewer/weaker dependencies come first
            var sortedTables = unconfiguredTables
                .OrderBy(t => tableScores[t].StrongDependencyCount)
                .ThenBy(t => tableScores[t].TotalDependencyCount)
                .ThenBy(t => t.Table, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Assign positions starting after manual positions
            var nextAutoPosition = maxManualPosition >= 0 ? maxManualPosition + 100 : 0;
            
            foreach (var table in sortedTables)
            {
                positionMap[table] = nextAutoPosition;
                nextAutoPosition += 100;
            }
        }

        return positionMap;
    }

    /// <summary>
    /// Scores a table based on its dependencies within the cycle.
    /// Tables with fewer/weaker dependencies should load first.
    /// </summary>
    private static TableDependencyScore ComputeDependencyScore(
        TableKey table,
        HashSet<TableKey> component,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup)
    {
        var strongDependencies = 0;
        var weakDependencies = 0;

        // edges[target] contains nodes that depend on target
        // We need to find what THIS table depends on (reverse edges)
        foreach (var (target, dependents) in edges)
        {
            if (!component.Contains(target))
            {
                continue; // Only care about dependencies within the cycle
            }

            if (!dependents.Contains(table))
            {
                continue; // This table doesn't depend on this target
            }

            // This table depends on 'target' - classify the relationship strength
            if (entityLookup.TryGetValue(table, out var entity))
            {
                // Find the relationship from this entity to the target
                var relationship = FindRelationshipToTarget(entity, target, entityLookup);
                
                if (relationship != null && TryExtractRelationshipMetadata(relationship, entity, table, out var metadata))
                {
                    var strength = ClassifyRelationship(metadata);
                    
                    if (strength == RelationshipStrength.Weak || strength == RelationshipStrength.CascadeWeak)
                    {
                        weakDependencies++;
                    }
                    else
                    {
                        strongDependencies++;
                    }
                }
                else
                {
                    // Can't extract relationship metadata - assume strong to be conservative
                    strongDependencies++;
                }
            }
            else
            {
                // Can't find entity - assume strong to be conservative
                strongDependencies++;
            }
        }

        return new TableDependencyScore(strongDependencies, weakDependencies);
    }

    /// <summary>
    /// Finds the relationship from source entity to a target table.
    /// </summary>
    private static RelationshipModel? FindRelationshipToTarget(
        EntityModel sourceEntity,
        TableKey targetKey,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup)
    {
        if (!entityLookup.TryGetValue(targetKey, out var targetEntity))
        {
            return null;
        }

        return sourceEntity.Relationships.FirstOrDefault(r =>
            r.TargetEntity.Value.Equals(targetEntity.LogicalName.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts relationship metadata needed for strength classification.
    /// </summary>
    private static bool TryExtractRelationshipMetadata(
        RelationshipModel relationship,
        EntityModel entity,
        TableKey source,
        out RelationshipMetadata metadata)
    {
        if (relationship.ActualConstraints.IsDefaultOrEmpty)
        {
            metadata = default!;
            return false;
        }

        var viaAttribute = entity.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.LogicalName.Value, relationship.ViaAttribute.Value, StringComparison.OrdinalIgnoreCase));

        if (viaAttribute is null)
        {
            metadata = default!;
            return false;
        }

        var constraint = relationship.ActualConstraints[0];
        
        metadata = new RelationshipMetadata(
            source.Table,
            constraint.OnDeleteAction,
            viaAttribute.OnDisk.IsNullable ?? false);
        
        return true;
    }

    private readonly record struct TableDependencyScore(int StrongDependencyCount, int WeakDependencyCount)
    {
        public int TotalDependencyCount => StrongDependencyCount + WeakDependencyCount;
    }

    private static (ImmutableArray<AllowedCycle> AllowedCycles, List<(TableKey Target, TableKey Dependent)> EdgesToRemove)
        DetectAsymmetricCycles(
            IReadOnlyCollection<TableKey> remainingKeys,
            IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
            IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
            IReadOnlyDictionary<TableKey, EntityModel> entityLookup)
    {
        if (remainingKeys.Count == 0 || entityLookup.Count == 0)
        {
            return (ImmutableArray<AllowedCycle>.Empty, new List<(TableKey, TableKey)>());
        }

        var components = TarjanScc.FindStronglyConnectedComponents(remainingKeys, edges, KeyComparer);
        var detectedCycles = ImmutableArray.CreateBuilder<AllowedCycle>();
        var edgesToRemove = new List<(TableKey Target, TableKey Dependent)>();

        foreach (var component in components.Where(component => component.Count >= 2))
        {
            if (component.Count == 2)
            {
                // Handle simple 2-node cycles with existing logic
                var pair = component.ToArray();

                if (!TryGetRelationshipMetadata(pair[0], pair[1], entityLookup, nodes, out var firstToSecond) ||
                    !TryGetRelationshipMetadata(pair[1], pair[0], entityLookup, nodes, out var secondToFirst))
                {
                    continue;
                }

                var firstStrength = ClassifyRelationship(firstToSecond);
                var secondStrength = ClassifyRelationship(secondToFirst);

                // Auto-resolve if one edge is weak/cascadeWeak and the other is cascade/other
                var firstIsBreakable = firstStrength == RelationshipStrength.Weak || firstStrength == RelationshipStrength.CascadeWeak;
                var secondIsBreakable = secondStrength == RelationshipStrength.Weak || secondStrength == RelationshipStrength.CascadeWeak;
                var firstIsStrong = firstStrength == RelationshipStrength.Cascade || firstStrength == RelationshipStrength.Other;
                var secondIsStrong = secondStrength == RelationshipStrength.Cascade || secondStrength == RelationshipStrength.Other;

                if ((firstIsBreakable && secondIsStrong) || (firstIsStrong && secondIsBreakable))
                {
                    var parentKey = firstIsBreakable ? pair[0] : pair[1];
                    var childKey = firstIsStrong ? pair[0] : pair[1];

                    var allowedCycle = CreateAutoCycle(parentKey, childKey, nodes);
                    if (allowedCycle is not null)
                    {
                        detectedCycles.Add(allowedCycle);
                        // Remove the weak edge: parentKey depends on childKey (the back-reference)
                        // Edge semantics: edges[target] contains dependents, so edges[childKey] contains parentKey
                        // We want to remove "parentKey depends on childKey", so remove from edges[childKey]
                        edgesToRemove.Add((childKey, parentKey));
                    }
                }
            }
            else
            {
                // Handle large multi-node cycles
                var result = ResolveMultiNodeCycle(component, edges, nodes, entityLookup);
                if (result.HasValue)
                {
                    detectedCycles.Add(result.Value.AllowedCycle);
                    edgesToRemove.AddRange(result.Value.EdgesToRemove);
                }
            }
        }

        return (detectedCycles.ToImmutable(), edgesToRemove);
    }

    private static (AllowedCycle AllowedCycle, ImmutableArray<(TableKey Source, TableKey Target)> EdgesToRemove)? 
        ResolveMultiNodeCycle(
            ImmutableHashSet<TableKey> component,
            IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
            IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
            IReadOnlyDictionary<TableKey, EntityModel> entityLookup)
    {
        // First, try to detect and break 2-node cycles within this large SCC
        var twoNodeCycleEdges = DetectTwoNodeCyclesInComponent(component, edges, entityLookup, nodes);
        if (twoNodeCycleEdges.Count > 0)
        {
            // Try breaking 2-node cycles first, then check if that resolves the entire SCC
            var allEdgesToBreak = new List<(TableKey Source, TableKey Target)>();
            
            foreach (var edge in twoNodeCycleEdges)
            {
                allEdgesToBreak.Add(edge);
            }
            
            // Check if breaking these edges resolves the cycle
            if (IsAcyclicAfterRemoval(component, allEdgesToBreak, edges))
            {
                var twoNodeOrdering = GenerateTopologicalOrdering(component, allEdgesToBreak.ToImmutableArray(), edges, nodes);
                if (!twoNodeOrdering.IsDefaultOrEmpty)
                {
                    var cycleForTwoNodes = AllowedCycle.Create(twoNodeOrdering);
                    if (cycleForTwoNodes.IsSuccess)
                    {
                        return (cycleForTwoNodes.Value, allEdgesToBreak.ToImmutableArray());
                    }
                }
            }
        }
        
        // If 2-node resolution didn't work, try weak edges with CASCADE prioritization
        var weakEdges = FindWeakEdgesInComponent(component, edges, entityLookup, nodes, ScoreEdgeForBreaking);
        
        if (weakEdges.Count == 0)
        {
            return null;
        }

        // Find minimum set of weak edges to break the cycle
        var edgesToBreak = FindMinimumFeedbackArcSet(component, weakEdges, edges);
        
        if (edgesToBreak.IsDefaultOrEmpty)
        {
            return null;
        }

        // Generate topological ordering for the cycle
        var ordering = GenerateTopologicalOrdering(component, edgesToBreak, edges, nodes);
        
        if (ordering.IsDefaultOrEmpty)
        {
            return null;
        }

        var allowedCycle = AllowedCycle.Create(ordering);
        if (!allowedCycle.IsSuccess)
        {
            return null;
        }

        return (allowedCycle.Value, edgesToBreak);
    }

    private static List<(TableKey Source, TableKey Target)> DetectTwoNodeCyclesInComponent(
        ImmutableHashSet<TableKey> component,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        var edgesToBreak = new List<(TableKey Source, TableKey Target)>();
        var processed = new HashSet<(TableKey, TableKey)>();

        foreach (var nodeA in component)
        {
            if (!edges.TryGetValue(nodeA, out var aTargets)) continue;

            foreach (var nodeB in aTargets.Where(component.Contains))
            {
                // Check if B also has an edge back to A (forming a 2-node cycle)
                if (!edges.TryGetValue(nodeB, out var bTargets) || !bTargets.Contains(nodeA)) continue;
                
                // Avoid processing the same cycle twice
                if (processed.Contains((nodeB, nodeA))) continue;
                processed.Add((nodeA, nodeB));
                processed.Add((nodeB, nodeA));

                // Classify both edges
                if (!TryGetRelationshipMetadata(nodeA, nodeB, entityLookup, nodes, out var aToBMeta) ||
                    !TryGetRelationshipMetadata(nodeB, nodeA, entityLookup, nodes, out var bToAMeta))
                {
                    continue;
                }

                var aToBStrength = ClassifyRelationship(aToBMeta);
                var bToAStrength = ClassifyRelationship(bToAMeta);

                // If one edge is breakable and the other is strong, break the weak one
                var aToBBreakable = aToBStrength == RelationshipStrength.Weak || aToBStrength == RelationshipStrength.CascadeWeak;
                var bToABreakable = bToAStrength == RelationshipStrength.Weak || bToAStrength == RelationshipStrength.CascadeWeak;

                if (aToBBreakable && !bToABreakable)
                {
                    edgesToBreak.Add((nodeA, nodeB));
                }
                else if (bToABreakable && !aToBBreakable)
                {
                    edgesToBreak.Add((nodeB, nodeA));
                }
                else if (aToBBreakable && bToABreakable)
                {
                    // Both breakable - prefer breaking Weak over CascadeWeak
                    if (aToBStrength == RelationshipStrength.Weak)
                    {
                        edgesToBreak.Add((nodeA, nodeB));
                    }
                    else
                    {
                        edgesToBreak.Add((nodeB, nodeA));
                    }
                }
            }
        }

        return edgesToBreak;
    }

    private static int ScoreEdgeForBreaking(
        TableKey source,
        TableKey target,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        var score = 0;
        
        // Prefer breaking edges where target has "History", "Audit", "Log", "Archive" in name
        var targetName = target.Table.ToUpperInvariant();
        if (targetName.Contains("HISTORY")) score += 100;
        if (targetName.Contains("AUDIT")) score += 90;
        if (targetName.Contains("LOG")) score += 80;
        if (targetName.Contains("ARCHIVE")) score += 70;
        if (targetName.Contains("VERSION")) score += 60;
        
        // Prefer breaking edges where source references target (source has FK to target)
        // This is the "back-reference" pattern we want to break
        if (nodes.ContainsKey(source) && nodes.ContainsKey(target) &&
            source.Table.Contains(target.Table, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        
        return score;
    }

    private static List<(TableKey Source, TableKey Target)> FindWeakEdgesInComponent(
        ImmutableHashSet<TableKey> component,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        Func<TableKey, TableKey, IReadOnlyDictionary<TableKey, StaticEntityTableData>, int>? edgePriorityFunc = null)
    {
        var weakEdges = new List<(TableKey Source, TableKey Target)>();

        foreach (var source in component)
        {
            if (!edges.TryGetValue(source, out var targets))
            {
                continue;
            }

            foreach (var target in targets.Where(component.Contains))
            {
                if (TryGetRelationshipMetadata(source, target, entityLookup, nodes, out var metadata))
                {
                    var strength = ClassifyRelationship(metadata);
                    // Include Weak and CascadeWeak edges (but not Cascade or Other)
                    if (strength == RelationshipStrength.Weak || strength == RelationshipStrength.CascadeWeak)
                    {
                        weakEdges.Add((source, target));
                    }
                }
            }
        }

        // Apply optional prioritization heuristic (e.g., domain-specific naming patterns)
        if (edgePriorityFunc is not null)
        {
            weakEdges.Sort((a, b) =>
            {
                var aScore = edgePriorityFunc(a.Source, a.Target, nodes);
                var bScore = edgePriorityFunc(b.Source, b.Target, nodes);
                return bScore.CompareTo(aScore); // Higher score = higher priority
            });
        }

        return weakEdges;
    }

    private static ImmutableArray<(TableKey Source, TableKey Target)> FindMinimumFeedbackArcSet(
        ImmutableHashSet<TableKey> component,
        List<(TableKey Source, TableKey Target)> weakEdges,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges)
    {
        // Safety limit: for ~260 entities, trying 50k combinations is reasonable
        const int MaxCombinationsToTry = 50_000;
        var combinationsTried = 0;
        
        // Try progressively larger subsets until we find one that breaks all cycles
        for (var subsetSize = 1; subsetSize <= weakEdges.Count; subsetSize++)
        {
            foreach (var subset in GetCombinations(weakEdges, subsetSize))
            {
                if (++combinationsTried > MaxCombinationsToTry)
                {
                    // Bailout: just remove all weak edges
                    // This is safe because if we can't find a minimal set within 50k tries,
                    // the cycle is complex enough that removing all weak edges is prudent
                    return weakEdges.ToImmutableArray();
                }
                
                if (IsAcyclicAfterRemoval(component, subset, edges))
                {
                    return subset.ToImmutableArray();
                }
            }
        }

        // Fallback: remove all weak edges (guaranteed to work if component was only held together by weak edges)
        return weakEdges.ToImmutableArray();
    }

    private static IEnumerable<List<(TableKey Source, TableKey Target)>> GetCombinations(
        List<(TableKey Source, TableKey Target)> items,
        int size)
    {
        if (size == 0)
        {
            yield return new List<(TableKey, TableKey)>();
            yield break;
        }

        if (size > items.Count)
        {
            yield break;
        }

        // Generate combinations recursively
        for (var i = 0; i <= items.Count - size; i++)
        {
            var item = items[i];
            if (size == 1)
            {
                yield return new List<(TableKey, TableKey)> { item };
            }
            else
            {
                foreach (var rest in GetCombinations(items.Skip(i + 1).ToList(), size - 1))
                {
                    var combination = new List<(TableKey, TableKey)> { item };
                    combination.AddRange(rest);
                    yield return combination;
                }
            }
        }
    }

    private static bool IsAcyclicAfterRemoval(
        ImmutableHashSet<TableKey> component,
        List<(TableKey Source, TableKey Target)> edgesToRemove,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges)
    {
        // Clone edges and remove the candidate edges
        var testEdges = new Dictionary<TableKey, HashSet<TableKey>>(KeyComparer);
        foreach (var node in component)
        {
            if (edges.TryGetValue(node, out var targets))
            {
                testEdges[node] = new HashSet<TableKey>(targets.Where(component.Contains), KeyComparer);
            }
            else
            {
                testEdges[node] = new HashSet<TableKey>(KeyComparer);
            }
        }

        foreach (var (source, target) in edgesToRemove)
        {
            if (testEdges.TryGetValue(source, out var targets))
            {
                targets.Remove(target);
            }
        }

        // Check if the result is acyclic using SCC
        var sccs = TarjanScc.FindStronglyConnectedComponents(component, testEdges, KeyComparer);
        return sccs.All(static scc => scc.Count == 1);
    }

    private static ImmutableArray<TableOrdering> GenerateTopologicalOrdering(
        ImmutableHashSet<TableKey> component,
        ImmutableArray<(TableKey Source, TableKey Target)> edgesToBreak,
        IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        // Create modified graph with weak edges removed
        var adjustedEdges = new Dictionary<TableKey, HashSet<TableKey>>(KeyComparer);
        foreach (var node in component)
        {
            if (edges.TryGetValue(node, out var targets))
            {
                adjustedEdges[node] = new HashSet<TableKey>(targets.Where(component.Contains), KeyComparer);
            }
            else
            {
                adjustedEdges[node] = new HashSet<TableKey>(KeyComparer);
            }
        }

        foreach (var (source, target) in edgesToBreak)
        {
            if (adjustedEdges.TryGetValue(source, out var targets))
            {
                targets.Remove(target);
            }
        }

        // Compute topological levels using BFS
        var levels = new Dictionary<TableKey, int>(KeyComparer);
        var inDegree = new Dictionary<TableKey, int>(KeyComparer);
        
        foreach (var node in component)
        {
            inDegree[node] = 0;
        }

        foreach (var (node, targets) in adjustedEdges)
        {
            foreach (var target in targets)
            {
                inDegree[target] = inDegree[target] + 1;
            }
        }

        var queue = new Queue<TableKey>();
        foreach (var node in component.Where(node => inDegree[node] == 0))
        {
            queue.Enqueue(node);
            levels[node] = 0;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentLevel = levels[current];

            if (!adjustedEdges.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                inDegree[target] = inDegree[target] - 1;
                if (inDegree[target] == 0)
                {
                    queue.Enqueue(target);
                    levels[target] = currentLevel + 1;
                }
            }
        }

        // If not all nodes were assigned levels, we still have a cycle (shouldn't happen)
        if (levels.Count != component.Count)
        {
            return ImmutableArray<TableOrdering>.Empty;
        }

        // Generate TableOrdering with positions based on levels
        var orderings = ImmutableArray.CreateBuilder<TableOrdering>();
        const int positionBase = 100;
        const int positionStep = 100;

        foreach (var (table, level) in levels.OrderBy(static kvp => kvp.Value).ThenBy(kvp => kvp.Key.Table))
        {
            var tableName = nodes[table].Definition.PhysicalName ?? 
                           nodes[table].Definition.EffectiveName ?? 
                           table.Table;
            var position = positionBase + (level * positionStep);
            var ordering = TableOrdering.Create(tableName, position);
            if (ordering.IsSuccess)
            {
                orderings.Add(ordering.Value);
            }
        }

        return orderings.ToImmutable();
    }

    /// <summary>
    /// Tarjan's strongly connected components algorithm - pure graph utility.
    /// Extracted for reusability and testability.
    /// </summary>
    private static class TarjanScc
    {
        public static ImmutableArray<ImmutableHashSet<TableKey>> FindStronglyConnectedComponents(
            IReadOnlyCollection<TableKey> nodes,
            IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
            TableKeyComparer comparer)
        {
            var state = new TarjanState(comparer, new HashSet<TableKey>(nodes, comparer));
            var components = ImmutableArray.CreateBuilder<ImmutableHashSet<TableKey>>();

            foreach (var node in nodes)
            {
                if (!state.NodeIndex.ContainsKey(node))
                {
                    StrongConnect(node, edges, state, components, comparer);
                }
            }

            return components.ToImmutable();
        }

        private static void StrongConnect(
            TableKey node,
            IReadOnlyDictionary<TableKey, HashSet<TableKey>> edges,
            TarjanState state,
            ImmutableArray<ImmutableHashSet<TableKey>>.Builder components,
            TableKeyComparer comparer)
        {
            state.NodeIndex[node] = state.Index;
            state.LowLink[node] = state.Index;
            state.Index++;
            state.Stack.Push(node);
            state.OnStack.Add(node);

            if (edges.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors.Where(state.NodeSet.Contains))
                {
                    if (!state.NodeIndex.ContainsKey(neighbor))
                    {
                        StrongConnect(neighbor, edges, state, components, comparer);
                        state.LowLink[node] = Math.Min(state.LowLink[node], state.LowLink[neighbor]);
                    }
                    else if (state.OnStack.Contains(neighbor))
                    {
                        state.LowLink[node] = Math.Min(state.LowLink[node], state.NodeIndex[neighbor]);
                    }
                }
            }

            if (state.LowLink[node] != state.NodeIndex[node])
            {
                return;
            }

            var componentBuilder = ImmutableHashSet.CreateBuilder(comparer);
            TableKey current;
            do
            {
                current = state.Stack.Pop();
                state.OnStack.Remove(current);
                componentBuilder.Add(current);
            }
            while (!comparer.Equals(current, node));

            components.Add(componentBuilder.ToImmutable());
        }

        private sealed class TarjanState
        {
            public int Index;
            public readonly Dictionary<TableKey, int> NodeIndex;
            public readonly Dictionary<TableKey, int> LowLink;
            public readonly Stack<TableKey> Stack = new();
            public readonly HashSet<TableKey> OnStack;
            public readonly HashSet<TableKey> NodeSet;

            public TarjanState(TableKeyComparer comparer, HashSet<TableKey> nodeSet)
            {
                NodeIndex = new Dictionary<TableKey, int>(comparer);
                LowLink = new Dictionary<TableKey, int>(comparer);
                OnStack = new HashSet<TableKey>(comparer);
                NodeSet = nodeSet;
            }
        }
    }

    private static bool TryGetRelationshipMetadata(
        TableKey source,
        TableKey target,
        IReadOnlyDictionary<TableKey, EntityModel> entityLookup,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes,
        out RelationshipMetadata metadata)
    {
        if (!entityLookup.TryGetValue(source, out var entity))
        {
            metadata = default!;
            return false;
        }

        var relationship = entity.Relationships
            .FirstOrDefault(rel => string.Equals(rel.TargetPhysicalName.Value, target.Table, StringComparison.OrdinalIgnoreCase));

        if (relationship is null || relationship.ActualConstraints.IsDefaultOrEmpty)
        {
            metadata = default!;
            return false;
        }

        var viaAttribute = entity.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.LogicalName.Value, relationship.ViaAttribute.Value, StringComparison.OrdinalIgnoreCase));

        if (viaAttribute is null)
        {
            metadata = default!;
            return false;
        }

        var constraint = relationship.ActualConstraints[0];
        var sourceName = !string.IsNullOrWhiteSpace(nodes[source].Definition.PhysicalName)
            ? nodes[source].Definition.PhysicalName!
            : nodes[source].Definition.EffectiveName ?? source.Table;

        metadata = new RelationshipMetadata(
            sourceName,
            constraint.OnDeleteAction,
            viaAttribute.OnDisk.IsNullable ?? false);
        return true;
    }

    private static RelationshipStrength ClassifyRelationship(RelationshipMetadata metadata)
    {
        if (metadata.IsNullable && IsDeleteRule(metadata.DeleteRule, "NO_ACTION", "SET_NULL"))
        {
            return RelationshipStrength.Weak;
        }

        if (IsDeleteRule(metadata.DeleteRule, "CASCADE"))
        {
            return RelationshipStrength.Cascade;
        }

        return RelationshipStrength.Other;
    }

    private static bool IsDeleteRule(string deleteRule, params string[] expected)
    {
        return expected.Any(rule => string.Equals(deleteRule, rule, StringComparison.OrdinalIgnoreCase));
    }

    private static AllowedCycle? CreateAutoCycle(
        TableKey parentKey,
        TableKey childKey,
        IReadOnlyDictionary<TableKey, StaticEntityTableData> nodes)
    {
        var parentName = nodes[parentKey].Definition.PhysicalName ?? nodes[parentKey].Definition.EffectiveName ?? parentKey.Table;
        var childName = nodes[childKey].Definition.PhysicalName ?? nodes[childKey].Definition.EffectiveName ?? childKey.Table;

        var parentOrdering = TableOrdering.Create(parentName, 100);
        var childOrdering = TableOrdering.Create(childName, 200);

        if (!parentOrdering.IsSuccess || !childOrdering.IsSuccess)
        {
            return null;
        }

        var allowedCycle = AllowedCycle.Create(ImmutableArray.Create(parentOrdering.Value, childOrdering.Value));
        return allowedCycle.IsSuccess ? allowedCycle.Value : null;
    }

    private static CircularDependencyOptions MergeCircularDependencyOptions(
        CircularDependencyOptions? existing,
        ImmutableArray<AllowedCycle> detectedCycles)
    {
        var existingCycles = existing?.AllowedCycles ?? ImmutableArray<AllowedCycle>.Empty;
        var strictMode = existing?.StrictMode ?? false;
        var mergedCycles = existingCycles.AddRange(detectedCycles);
        var mergedResult = CircularDependencyOptions.Create(mergedCycles, strictMode);

        if (mergedResult.IsSuccess)
        {
            return mergedResult.Value;
        }

        return existing ?? CircularDependencyOptions.Empty;
    }

    private sealed record RelationshipMetadata(
        string SourceTable,
        string DeleteRule,
        bool IsNullable);

    private enum RelationshipStrength
    {
        Weak,         // Nullable + NO_ACTION or SET_NULL
        CascadeWeak,  // Nullable + CASCADE (can be broken but risky)
        Cascade,      // NOT NULL + CASCADE delete (strong)
        Other         // Everything else (NOT NULL + NO_ACTION)
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
            var effectiveLookup = new Dictionary<TableKey, TableKey>(KeyComparer);
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

            var junctions = new HashSet<TableKey>(KeyComparer);

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
        EntityDependencyOrderingMode Mode,
        ImmutableArray<ImmutableArray<string>>? StronglyConnectedComponents = null)
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
