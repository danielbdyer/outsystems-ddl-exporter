using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public static class StaticSeedForeignKeyPreflight
{
    public static StaticSeedForeignKeyPreflightResult Analyze(
        IReadOnlyList<StaticEntityTableData> orderedTables,
        OsmModel? model)
    {
        if (orderedTables is null)
        {
            throw new ArgumentNullException(nameof(orderedTables));
        }

        if (orderedTables.Count == 0 || model is null || model.Modules.IsDefaultOrEmpty)
        {
            return StaticSeedForeignKeyPreflightResult.Empty;
        }

        var comparer = new TableKeyComparer();
        var orderLookup = new Dictionary<TableKey, int>(comparer);

        for (var i = 0; i < orderedTables.Count; i++)
        {
            var table = orderedTables[i];
            var key = TableKey.From(table.Definition);
            orderLookup[key] = i;
        }

        var orphanBuilder = ImmutableArray.CreateBuilder<StaticSeedForeignKeyIssue>();
        var orderingBuilder = ImmutableArray.CreateBuilder<StaticSeedForeignKeyIssue>();

        foreach (var module in model.Modules)
        {
            if (module.Entities.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var entity in module.Entities)
            {
                if (!entity.IsStatic || !entity.IsActive)
                {
                    continue;
                }

                var childKey = TableKey.From(entity.Schema.Value, entity.PhysicalName.Value);
                if (!orderLookup.TryGetValue(childKey, out var childOrder))
                {
                    continue;
                }

                if (entity.Relationships.IsDefaultOrEmpty)
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
                        var referencedTable = constraint.ReferencedTable.Trim();
                        var parentKey = TableKey.From(referencedSchema, referencedTable);

                        if (comparer.Equals(childKey, parentKey))
                        {
                            continue;
                        }

                        var issue = new StaticSeedForeignKeyIssue(
                            module.Name.Value,
                            entity.LogicalName.Value,
                            entity.Schema.Value,
                            entity.PhysicalName.Value,
                            string.IsNullOrWhiteSpace(constraint.Name) ? string.Empty : constraint.Name,
                            referencedSchema,
                            referencedTable,
                            childOrder,
                            ParentOrder: null,
                            StaticSeedForeignKeyIssueKind.MissingParent);

                        if (!orderLookup.TryGetValue(parentKey, out var parentOrder))
                        {
                            orphanBuilder.Add(issue);
                            continue;
                        }

                        if (parentOrder >= childOrder)
                        {
                            orderingBuilder.Add(issue with
                            {
                                ParentOrder = parentOrder,
                                Kind = StaticSeedForeignKeyIssueKind.ParentAfterChild
                            });
                        }
                    }
                }
            }
        }

        return new StaticSeedForeignKeyPreflightResult(
            orphanBuilder.ToImmutable(),
            orderingBuilder.ToImmutable());
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
}

public sealed record StaticSeedForeignKeyPreflightResult(
    ImmutableArray<StaticSeedForeignKeyIssue> MissingParents,
    ImmutableArray<StaticSeedForeignKeyIssue> OrderingViolations)
{
    public static StaticSeedForeignKeyPreflightResult Empty { get; } = new(
        ImmutableArray<StaticSeedForeignKeyIssue>.Empty,
        ImmutableArray<StaticSeedForeignKeyIssue>.Empty);

    public bool HasFindings =>
        !MissingParents.IsDefaultOrEmpty || !OrderingViolations.IsDefaultOrEmpty;
}

public enum StaticSeedForeignKeyIssueKind
{
    MissingParent,
    ParentAfterChild
}

public sealed record StaticSeedForeignKeyIssue(
    string Module,
    string ChildLogicalName,
    string ChildSchema,
    string ChildTable,
    string ConstraintName,
    string ReferencedSchema,
    string ReferencedTable,
    int ChildOrder,
    int? ParentOrder,
    StaticSeedForeignKeyIssueKind Kind);
