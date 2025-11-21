using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Emission.Seeds;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// Validates topological ordering of entities after FK-based sorting.
/// Detects child-before-parent violations, missing edges, and cycles.
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
                Cycles: ImmutableArray<CycleDiagnostic>.Empty,
                ValidatedConstraints: 0,
                SkippedConstraints: 0);
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
                Cycles: ImmutableArray<CycleDiagnostic>.Empty,
                ValidatedConstraints: 0,
                SkippedConstraints: 0);
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

        // Build a secondary lookup keyed by physical table name so cycle diagnostics can
        // resolve entities when violations store physical identifiers instead of effective names.
        var physicalEntityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .SelectMany(e => new[]
            {
                new { Key = e.PhysicalName.Value, Entity = e },
                new { Key = $"{e.Schema.Value}.{e.PhysicalName.Value}", Entity = e }
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Entity, StringComparer.OrdinalIgnoreCase);

        // Build position lookup: EffectiveName -> Index
        // Use EffectiveName which has naming overrides already applied by the sorter
        var positions = orderedTables
            .Select((table, index) => (table.Definition.EffectiveName, Index: index))
            .ToDictionary(x => x.EffectiveName, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var violations = ImmutableArray.CreateBuilder<OrderingViolation>();
        var validatedConstraints = 0;
        var missingEdges = 0;
        var skippedConstraints = 0;

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

                var validConstraints = relationship.ActualConstraints
                    .Where(HasValidConstraint)
                    .ToArray();

                skippedConstraints += relationship.ActualConstraints.Length - validConstraints.Length;

                if (validConstraints.Length == 0)
                {
                    continue;
                }

                foreach (var constraint in validConstraints)
                {
                    validatedConstraints++;

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
        }

        // Extract cycle diagnostics if cycles were detected
        circularDependencyOptions ??= CircularDependencyOptions.Empty;
        var cycles = ExtractCycleDiagnostics(
            violations,
            entityLookup,
            physicalEntityLookup,
            orderedTables,
            circularDependencyOptions,
            out var resolvedCycle);

        var cycleDetected = resolvedCycle;

        return new TopologicalValidationResult(
            IsValid: violations.Count == 0 || violations.All(v => v.ViolationType == "MissingParent"),
            Violations: violations.ToImmutable(),
            TotalEntities: orderedTables.Length,
            TotalForeignKeys: validatedConstraints,
            MissingEdges: missingEdges,
            CycleDetected: cycleDetected,
            Cycles: cycles,
            ValidatedConstraints: validatedConstraints,
            SkippedConstraints: skippedConstraints);
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

    private static ImmutableArray<CycleDiagnostic> ExtractCycleDiagnostics(
        ImmutableArray<OrderingViolation>.Builder violations,
        IReadOnlyDictionary<string, EntityModel> entityLookup,
        IReadOnlyDictionary<string, EntityModel> physicalEntityLookup,
        ImmutableArray<StaticEntityTableData> orderedTables,
        CircularDependencyOptions circularDependencyOptions,
        out bool resolvedCycle)
    {
        const string unhydratedColumn = "UnhydratedColumn";
        const string lookupFailed = "LookupFailed";

        // For now, return a simple diagnostic showing which tables have violations
        // This is a starting point - we'll enhance to show actual cycle paths
        var cycleViolations = violations.Where(v => v.ViolationType == "ChildBeforeParent").ToArray();

        if (cycleViolations.Length == 0)
        {
            resolvedCycle = false;
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
                if (!TryResolveEntity(v.ChildTable, entityLookup, physicalEntityLookup, out var entity))
                {
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: v.ChildTable,
                        TargetTable: v.ParentTable,
                        SourceColumn: lookupFailed,
                        TargetColumn: lookupFailed,
                        IsNullable: false,
                        DeleteRule: lookupFailed);
                }

                // Find the relationship that matches this FK
                var relationship = entity.Relationships.FirstOrDefault(r =>
                    r.ActualConstraints.Any(c => NamesMatch(c.Name, v.ForeignKeyName) && HasValidConstraint(c)));

                if (relationship == null)
                {
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: v.ChildTable,
                        TargetTable: v.ParentTable,
                        SourceColumn: unhydratedColumn,
                        TargetColumn: unhydratedColumn,
                        IsNullable: false,
                        DeleteRule: unhydratedColumn);
                }

                var constraint = relationship.ActualConstraints.First(c =>
                    NamesMatch(c.Name, v.ForeignKeyName) && HasValidConstraint(c));
                var firstColumn = constraint.Columns.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.OwnerColumn) &&
                    !string.IsNullOrWhiteSpace(c.ReferencedColumn));

                var sourceColumnName = firstColumn?.OwnerColumn?.Trim();
                var targetColumnName = firstColumn?.ReferencedColumn?.Trim();
                var sourceAttr = string.IsNullOrWhiteSpace(sourceColumnName)
                    ? null
                    : entity.Attributes.FirstOrDefault(a =>
                        string.Equals(a.ColumnName.Value, sourceColumnName, StringComparison.OrdinalIgnoreCase));

                return new ForeignKeyInCycle(
                    ConstraintName: v.ForeignKeyName,
                    SourceTable: v.ChildTable,
                    TargetTable: v.ParentTable,
                    SourceColumn: sourceColumnName ?? sourceAttr?.ColumnName.Value ?? unhydratedColumn,
                    TargetColumn: targetColumnName ?? unhydratedColumn,
                    IsNullable: sourceAttr?.IsMandatory == false,
                    DeleteRule: constraint.OnDeleteAction ?? string.Empty);
            })
            .ToImmutableArray();

        var hasResolvedConstraint = fkInfo.Any(fk =>
            !string.Equals(fk.SourceColumn, lookupFailed, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fk.TargetColumn, lookupFailed, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fk.SourceColumn, unhydratedColumn, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fk.TargetColumn, unhydratedColumn, StringComparison.OrdinalIgnoreCase));

        if (!hasResolvedConstraint)
        {
            resolvedCycle = false;
            return ImmutableArray<CycleDiagnostic>.Empty;
        }

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

        resolvedCycle = true;

        return ImmutableArray.Create(new CycleDiagnostic(
            TablesInCycle: tablesInCycle,
            CyclePath: cyclePath,
            ForeignKeys: fkInfo,
            IsAllowed: isAllowed,
            AllowanceReason: allowanceReason));
    }

    private static bool TryResolveEntity(
        string key,
        IReadOnlyDictionary<string, EntityModel> entityLookup,
        IReadOnlyDictionary<string, EntityModel> physicalEntityLookup,
        [NotNullWhen(true)] out EntityModel? entity)
    {
        var trimmed = key?.Trim() ?? string.Empty;

        if (entityLookup.TryGetValue(trimmed, out entity))
        {
            return true;
        }

        if (physicalEntityLookup.TryGetValue(trimmed, out entity))
        {
            return true;
        }

        entity = null!;
        return false;
    }

    private static bool NamesMatch(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
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
    ImmutableArray<CycleDiagnostic> Cycles,
    int ValidatedConstraints,
    int SkippedConstraints);

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
