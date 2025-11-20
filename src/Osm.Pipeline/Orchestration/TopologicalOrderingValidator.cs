using System.Collections.Immutable;
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

        // Build position lookup: EffectiveName -> Index
        // Use EffectiveName which has naming overrides already applied by the sorter
        var positions = orderedTables
            .Select((table, index) => (table.Definition.EffectiveName, Index: index))
            .ToDictionary(x => x.EffectiveName, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var tableNameLookup = BuildEffectiveNameLookup(orderedTables, namingOverrides);

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

        var cycleViolationsDetected = violations.Any(v => v.ViolationType == "ChildBeforeParent");

        // Extract cycle diagnostics if cycles were detected
        circularDependencyOptions ??= CircularDependencyOptions.Empty;
        var (cycles, hasResolvedCycleConstraint) = cycleViolationsDetected
            ? ExtractCycleDiagnostics(violations, entityLookup, tableNameLookup, namingOverrides, circularDependencyOptions)
            : (ImmutableArray<CycleDiagnostic>.Empty, false);

        var cycleDetected = cycleViolationsDetected && hasResolvedCycleConstraint;

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

    private static IReadOnlyDictionary<string, string> BuildEffectiveNameLookup(
        ImmutableArray<StaticEntityTableData> orderedTables,
        NamingOverrideOptions namingOverrides)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in orderedTables)
        {
            var definition = table.Definition;
            var effectiveName = namingOverrides.GetEffectiveTableName(
                definition.Schema ?? string.Empty,
                definition.PhysicalName ?? definition.EffectiveName ?? string.Empty,
                definition.LogicalName ?? definition.EffectiveName ?? definition.PhysicalName ?? string.Empty,
                definition.Module);

            AddIfMissing(definition.PhysicalName);
            AddIfMissing(definition.EffectiveName);

            void AddIfMissing(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                {
                    lookup[name] = effectiveName;
                }
            }
        }

        return lookup;
    }

    private static (ImmutableArray<CycleDiagnostic> Cycles, bool HasResolvedConstraints) ExtractCycleDiagnostics(
        ImmutableArray<OrderingViolation>.Builder violations,
        IReadOnlyDictionary<string, EntityModel> entityLookup,
        IReadOnlyDictionary<string, string> tableNameLookup,
        NamingOverrideOptions namingOverrides,
        CircularDependencyOptions circularDependencyOptions)
    {
        // For now, return a simple diagnostic showing which tables have violations
        // This is a starting point - we'll enhance to show actual cycle paths
        var cycleViolations = violations.Where(v => v.ViolationType == "ChildBeforeParent").ToArray();

        if (cycleViolations.Length == 0)
        {
            return (ImmutableArray<CycleDiagnostic>.Empty, false);
        }

        // Group violations into potential cycles
        var tablesInCycle = cycleViolations
            .SelectMany(v => new[]
            {
                NormalizeTableName(v.ChildTable, tableNameLookup),
                NormalizeTableName(v.ParentTable, tableNameLookup)
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        // Check if this cycle is allowed
        var isAllowed = circularDependencyOptions.IsCycleAllowed(tablesInCycle);

        var hasResolvedConstraint = false;

        // Extract FK metadata
        var fkInfo = cycleViolations
            .Select(v =>
            {
                var normalizedChild = NormalizeTableName(v.ChildTable, tableNameLookup);
                var normalizedParent = NormalizeTableName(v.ParentTable, tableNameLookup);
                var resolutionStatus = "Resolved";

                // Extract FK metadata from entity model
                if (!entityLookup.TryGetValue(normalizedChild, out var entity))
                {
                    resolutionStatus = "EntityLookupFailed";
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: normalizedChild,
                        TargetTable: normalizedParent,
                        SourceColumn: string.Empty,
                        TargetColumn: string.Empty,
                        IsNullable: false,
                        DeleteRule: string.Empty,
                        ResolutionStatus: resolutionStatus);
                }

                // Find the relationship that matches this FK or the referenced table
                var relationshipFound = TryResolveRelationship(
                    entity,
                    v.ForeignKeyName,
                    normalizedParent,
                    namingOverrides,
                    tableNameLookup,
                    out var relationship,
                    out var constraint);

                if (!relationshipFound || relationship is null || constraint is null)
                {
                    resolutionStatus = "RelationshipLookupFailed";
                    return new ForeignKeyInCycle(
                        ConstraintName: v.ForeignKeyName,
                        SourceTable: normalizedChild,
                        TargetTable: normalizedParent,
                        SourceColumn: string.Empty,
                        TargetColumn: string.Empty,
                        IsNullable: false,
                        DeleteRule: string.Empty,
                        ResolutionStatus: resolutionStatus);
                }

                var sourceAttr = entity.Attributes.FirstOrDefault(a =>
                    a.LogicalName.Value == relationship.ViaAttribute.Value);

                var columnPair = constraint.Columns.FirstOrDefault(static column =>
                    !string.IsNullOrWhiteSpace(column.OwnerColumn) &&
                    !string.IsNullOrWhiteSpace(column.ReferencedColumn));

                resolutionStatus = columnPair is null
                    ? "ColumnPairMissing"
                    : "Resolved";

                hasResolvedConstraint |= columnPair is not null;

                return new ForeignKeyInCycle(
                    ConstraintName: v.ForeignKeyName,
                    SourceTable: normalizedChild,
                    TargetTable: normalizedParent,
                    SourceColumn: columnPair?.OwnerColumn ?? string.Empty,
                    TargetColumn: columnPair?.ReferencedColumn ?? string.Empty,
                    IsNullable: sourceAttr?.IsMandatory == false,
                    DeleteRule: constraint.OnDeleteAction ?? string.Empty,
                    ResolutionStatus: resolutionStatus);
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

        return (ImmutableArray.Create(new CycleDiagnostic(
            TablesInCycle: tablesInCycle,
            CyclePath: cyclePath,
            ForeignKeys: fkInfo,
            IsAllowed: isAllowed,
            AllowanceReason: allowanceReason)),
            hasResolvedConstraint);
    }

    private static bool TryResolveRelationship(
        EntityModel entity,
        string foreignKeyName,
        string normalizedParent,
        NamingOverrideOptions namingOverrides,
        IReadOnlyDictionary<string, string> tableNameLookup,
        out RelationshipModel? relationship,
        out RelationshipActualConstraint? constraint)
    {
        relationship = null;
        constraint = null;

        relationship = entity.Relationships.FirstOrDefault(r =>
            r.HasDatabaseConstraint &&
            !r.ActualConstraints.IsDefaultOrEmpty &&
            r.ActualConstraints.Any(c =>
                HasValidConstraint(c) &&
                string.Equals(c.Name, foreignKeyName, StringComparison.OrdinalIgnoreCase)));

        if (relationship is not null)
        {
            constraint = relationship.ActualConstraints.First(c =>
                HasValidConstraint(c) &&
                string.Equals(c.Name, foreignKeyName, StringComparison.OrdinalIgnoreCase));
            return constraint is not null;
        }

        relationship = entity.Relationships.FirstOrDefault(r =>
            r.HasDatabaseConstraint &&
            !r.ActualConstraints.IsDefaultOrEmpty &&
            r.ActualConstraints.Any(c =>
                HasValidConstraint(c) &&
                string.Equals(
                    NormalizeConstraintTarget(entity, r, c, namingOverrides, tableNameLookup),
                    normalizedParent,
                    StringComparison.OrdinalIgnoreCase)));

        if (relationship is null)
        {
            return false;
        }

        var resolvedRelationship = relationship;

        constraint = resolvedRelationship.ActualConstraints.FirstOrDefault(c =>
            HasValidConstraint(c) &&
            string.Equals(
                NormalizeConstraintTarget(entity, resolvedRelationship, c, namingOverrides, tableNameLookup),
                normalizedParent,
                StringComparison.OrdinalIgnoreCase));

        return constraint is not null;
    }

    private static string NormalizeConstraintTarget(
        EntityModel entity,
        RelationshipModel relationship,
        RelationshipActualConstraint constraint,
        NamingOverrideOptions namingOverrides,
        IReadOnlyDictionary<string, string> tableNameLookup)
    {
        var parentSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
            ? entity.Schema.Value
            : constraint.ReferencedSchema.Trim();

        var parentPhysicalName = constraint.ReferencedTable.Trim();
        var parentLogicalName = relationship.TargetEntity.Value;

        var effectiveParentName = namingOverrides.GetEffectiveTableName(
            parentSchema,
            parentPhysicalName,
            parentLogicalName,
            entity.Module.Value);

        return NormalizeTableName(effectiveParentName, tableNameLookup);
    }

    private static string NormalizeTableName(
        string? tableName,
        IReadOnlyDictionary<string, string> tableNameLookup)
    {
        if (!string.IsNullOrWhiteSpace(tableName) && tableNameLookup.TryGetValue(tableName, out var effective))
        {
            return effective;
        }

        return tableName ?? string.Empty;
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
    string DeleteRule,
    string ResolutionStatus);
