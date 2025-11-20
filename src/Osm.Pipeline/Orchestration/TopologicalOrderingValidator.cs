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
    /// <param name="circularDependencyOptions">Configuration for allowed cycles and ordering overrides</param>
    /// <returns>Validation result with any violations detected</returns>
    public TopologicalValidationResult Validate(
        ImmutableArray<StaticEntityTableData> orderedTables,
        OsmModel? model,
        NamingOverrideOptions? namingOverrides = null,
        CircularDependencyOptions? circularDependencyOptions = null)
    {
        if (orderedTables.IsDefaultOrEmpty)
        {
            return new TopologicalValidationResult(
                IsValid: true,
                Violations: ImmutableArray<OrderingViolation>.Empty,
                TotalEntities: 0,
                TotalForeignKeys: 0,
                MissingEdges: 0,
                CycleDetected: false,
                Cycles: ImmutableArray<CycleDiagnostic>.Empty);
        }

        if (model is null || model.Modules.IsDefaultOrEmpty)
        {
            return new TopologicalValidationResult(
                IsValid: true,
                Violations: ImmutableArray<OrderingViolation>.Empty,
                TotalEntities: orderedTables.Length,
                TotalForeignKeys: 0,
                MissingEdges: 0,
                CycleDetected: false,
                Cycles: ImmutableArray<CycleDiagnostic>.Empty);
        }

        namingOverrides ??= NamingOverrideOptions.Empty;

        // Build entity lookup: EffectiveName -> Entity
        // Apply naming overrides to match what EntityDependencySorter uses
        var entityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(
                e => namingOverrides.GetEffectiveTableName(e.Schema.Value, e.PhysicalName.Value, e.LogicalName.Value, e.Module.Value),
                e => e,
                StringComparer.OrdinalIgnoreCase);

        // Build position lookup: EffectiveName -> Index
        // Use EffectiveName which has naming overrides already applied by the sorter
        var positions = orderedTables
            .Select((table, index) => (table.Definition.EffectiveName, Index: index))
            .ToDictionary(x => x.EffectiveName, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var violations = ImmutableArray.CreateBuilder<OrderingViolation>();
        var totalFks = 0;
        var missingEdges = 0;

        // For each table, verify all FK parents appear BEFORE it
        foreach (var (table, childIndex) in orderedTables.Select((t, i) => (t, i)))
        {
            if (!entityLookup.TryGetValue(table.Definition.EffectiveName, out var entity))
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

                // Get parent schema and physical name from ActualConstraints
                var constraint = relationship.ActualConstraints[0];
                var fkName = constraint.Name;
                var displayName = string.IsNullOrWhiteSpace(fkName) ? "<unnamed>" : fkName;

                var parentSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                    ? entity.Schema.Value
                    : constraint.ReferencedSchema.Trim();

                var parentPhysicalName = constraint.ReferencedTable.Trim();
                var parentLogicalName = relationship.TargetEntity.Value;

                // Apply naming overrides to match what EntityDependencySorter produces
                var effectiveParentName = namingOverrides.GetEffectiveTableName(
                    parentSchema,
                    parentPhysicalName,
                    parentLogicalName,
                    null); // Module can be null - GetEffectiveTableName handles it

                if (!positions.TryGetValue(effectiveParentName, out var parentIndex))
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

        // Extract cycle diagnostics if cycles were detected
        circularDependencyOptions ??= CircularDependencyOptions.Empty;
        var cycles = cycleDetected
            ? ExtractCycleDiagnostics(violations, entityLookup, orderedTables, circularDependencyOptions)
            : ImmutableArray<CycleDiagnostic>.Empty;

        return new TopologicalValidationResult(
            IsValid: violations.Count == 0 || violations.All(v => v.ViolationType == "MissingParent"),
            Violations: violations.ToImmutable(),
            TotalEntities: orderedTables.Length,
            TotalForeignKeys: totalFks,
            MissingEdges: missingEdges,
            CycleDetected: cycleDetected,
            Cycles: cycles);
    }

    private static ImmutableArray<CycleDiagnostic> ExtractCycleDiagnostics(
        ImmutableArray<OrderingViolation>.Builder violations,
        IReadOnlyDictionary<string, EntityModel> entityLookup,
        ImmutableArray<StaticEntityTableData> orderedTables,
        CircularDependencyOptions circularDependencyOptions)
    {
        // For now, return a simple diagnostic showing which tables have violations
        // This is a starting point - we'll enhance to show actual cycle paths
        var cycleViolations = violations.Where(v => v.ViolationType == "ChildBeforeParent").ToArray();

        if (cycleViolations.Length == 0)
        {
            return ImmutableArray<CycleDiagnostic>.Empty;
        }

        // Group violations into potential cycles
        var tablesInCycle = cycleViolations
            .SelectMany(v => new[] { v.ChildTable, v.ParentTable })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        // Check if this cycle is allowed
        var isAllowed = circularDependencyOptions.IsCycleAllowed(tablesInCycle);

        // Extract FK metadata
        var fkInfo = cycleViolations
            .Select(v =>
            {
                // Extract FK metadata from entity model
                if (!entityLookup.TryGetValue(v.ChildTable, out var entity))
                {
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: v.ChildTable,
                        TargetTable: v.ParentTable,
                        SourceColumn: "Unknown",
                        TargetColumn: "Unknown",
                        IsNullable: false,
                        DeleteRule: "Unknown");
                }

                // Find the relationship that matches this FK
                var relationship = entity.Relationships.FirstOrDefault(r =>
                    r.ActualConstraints.Any(c => c.Name == v.ForeignKeyName));

                if (relationship == null)
                {
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: v.ChildTable,
                        TargetTable: v.ParentTable,
                        SourceColumn: "Unknown",
                        TargetColumn: "Unknown",
                        IsNullable: false,
                        DeleteRule: "Unknown");
                }

                var constraint = relationship.ActualConstraints.First(c => c.Name == v.ForeignKeyName);
                var sourceAttr = entity.Attributes.FirstOrDefault(a =>
                    a.LogicalName.Value == relationship.ViaAttribute.Value);

                return new ForeignKeyInCycle(
                    ConstraintName: v.ForeignKeyName,
                    SourceTable: v.ChildTable,
                    TargetTable: v.ParentTable,
                    SourceColumn: sourceAttr?.ColumnName.Value ?? "Unknown",
                    TargetColumn: constraint.Columns.FirstOrDefault()?.ReferencedColumn ?? "Unknown",
                    IsNullable: sourceAttr?.IsMandatory == false,
                    DeleteRule: constraint.OnDeleteAction);
            })
            .ToImmutableArray();

        // Build cycle path (simplified - just show tables involved)
        var cyclePath = string.Join(" → ", tablesInCycle) + " → " + tablesInCycle[0];

        // Validate that allowed cycles have nullable FKs for phased loading
        string? allowanceReason = null;
        if (isAllowed)
        {
            var hasNullableFk = fkInfo.Any(fk => fk.IsNullable);
            if (hasNullableFk)
            {
                allowanceReason = "Manual ordering configured with nullable FK support for phased loading";
            }
            else
            {
                // Allowed cycle but no nullable FKs - this is a configuration warning
                allowanceReason = "WARNING: Manual ordering configured but no nullable FKs found - phased loading may fail";
            }
        }

        return ImmutableArray.Create(new CycleDiagnostic(
            TablesInCycle: tablesInCycle,
            CyclePath: cyclePath,
            ForeignKeys: fkInfo,
            IsAllowed: isAllowed,
            AllowanceReason: allowanceReason));
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
    bool CycleDetected,
    ImmutableArray<CycleDiagnostic> Cycles);

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

/// <summary>
/// Diagnostic information about a detected circular dependency cycle.
/// </summary>
public sealed record CycleDiagnostic(
    ImmutableArray<string> TablesInCycle,
    string CyclePath,
    ImmutableArray<ForeignKeyInCycle> ForeignKeys,
    bool IsAllowed,
    string? AllowanceReason);

/// <summary>
/// Foreign key metadata within a circular dependency.
/// </summary>
public sealed record ForeignKeyInCycle(
    string ConstraintName,
    string SourceTable,
    string TargetTable,
    string SourceColumn,
    string TargetColumn,
    bool IsNullable,
    string DeleteRule);
