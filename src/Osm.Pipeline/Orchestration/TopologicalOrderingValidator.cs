using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Emission.Seeds;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// Validates topological ordering of entities after FK-based sorting.
/// Detects child-before-parent violations, missing edges, and cycles.
/// Part of M1.2: Topological Ordering Validation.
/// </summary>
public sealed class TopologicalOrderingValidator
{
    /// <summary>
    /// Validates that the given ordered tables satisfy topological ordering constraints.
    /// </summary>
    /// <param name="orderedTables">Tables in sorted order to validate</param>
    /// <param name="model">OSM model containing entity relationships</param>
    /// <param name="namingOverrides">Naming override options for table lookups</param>
    /// <returns>Validation result with any violations detected</returns>
    public TopologicalValidationResult Validate(
        ImmutableArray<StaticEntityTableData> orderedTables,
        OsmModel? model,
        NamingOverrideOptions? namingOverrides = null)
    {
        if (orderedTables.IsDefaultOrEmpty)
        {
            return new TopologicalValidationResult(
                IsValid: true,
                Violations: ImmutableArray<OrderingViolation>.Empty,
                TotalEntities: 0,
                TotalForeignKeys: 0,
                MissingEdges: 0,
                CycleDetected: false);
        }

        if (model is null || model.Modules.IsDefaultOrEmpty)
        {
            return new TopologicalValidationResult(
                IsValid: true,
                Violations: ImmutableArray<OrderingViolation>.Empty,
                TotalEntities: orderedTables.Length,
                TotalForeignKeys: 0,
                MissingEdges: 0,
                CycleDetected: false);
        }

        // Build entity lookup: PhysicalName -> Entity
        var entityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(e => e.PhysicalName.Value, e => e, StringComparer.OrdinalIgnoreCase);

        // Build position lookup: TableName -> Index
        var positions = orderedTables
            .Select((table, index) => (table.Definition.PhysicalName, Index: index))
            .ToDictionary(x => x.PhysicalName, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var violations = ImmutableArray.CreateBuilder<OrderingViolation>();
        var totalFks = 0;
        var missingEdges = 0;

        // For each table, verify all FK parents appear BEFORE it
        foreach (var (table, childIndex) in orderedTables.Select((t, i) => (t, i)))
        {
            if (!entityLookup.TryGetValue(table.Definition.PhysicalName, out var entity))
            {
                continue;
            }

            foreach (var relationship in entity.Relationships)
            {
                if (!relationship.HasDatabaseConstraint || relationship.ActualConstraints.IsDefaultOrEmpty)
                {
                    continue;
                }

                totalFks++;

                var parentPhysicalName = relationship.TargetPhysicalName.Value;
                var fkName = relationship.ActualConstraints[0].Name;
                var displayName = string.IsNullOrWhiteSpace(fkName) ? "<unnamed>" : fkName;

                if (!positions.TryGetValue(parentPhysicalName, out var parentIndex))
                {
                    // Parent not in sorted list (excluded entity)
                    missingEdges++;
                    violations.Add(new OrderingViolation(
                        ChildTable: table.Definition.PhysicalName,
                        ParentTable: parentPhysicalName,
                        ForeignKeyName: displayName,
                        ChildPosition: childIndex,
                        ParentPosition: -1,
                        ViolationType: "MissingParent"));
                    continue;
                }

                // CRITICAL: For topological ordering, parent must appear BEFORE child in the list
                // Valid: parentIndex < childIndex (parent comes first)
                // Violation: parentIndex > childIndex (child comes before parent - wrong!)
                // Exception: Self-references where parentIndex == childIndex are valid
                if (parentIndex > childIndex)
                {
                    violations.Add(new OrderingViolation(
                        ChildTable: table.Definition.PhysicalName,
                        ParentTable: parentPhysicalName,
                        ForeignKeyName: displayName,
                        ChildPosition: childIndex,
                        ParentPosition: parentIndex,
                        ViolationType: "ChildBeforeParent"));
                }
            }
        }

        var cycleDetected = violations.Any(v => v.ViolationType == "ChildBeforeParent");

        return new TopologicalValidationResult(
            IsValid: violations.Count == 0 || violations.All(v => v.ViolationType == "MissingParent"),
            Violations: violations.ToImmutable(),
            TotalEntities: orderedTables.Length,
            TotalForeignKeys: totalFks,
            MissingEdges: missingEdges,
            CycleDetected: cycleDetected);
    }
}

/// <summary>
/// Result of topological ordering validation.
/// </summary>
public sealed record TopologicalValidationResult(
    bool IsValid,
    ImmutableArray<OrderingViolation> Violations,
    int TotalEntities,
    int TotalForeignKeys,
    int MissingEdges,
    bool CycleDetected);

/// <summary>
/// Represents a single ordering violation (child-before-parent, missing parent, etc).
/// </summary>
public sealed record OrderingViolation(
    string ChildTable,
    string ParentTable,
    string ForeignKeyName,
    int ChildPosition,
    int ParentPosition,
    string ViolationType); // "ChildBeforeParent", "MissingParent", "Cycle"
