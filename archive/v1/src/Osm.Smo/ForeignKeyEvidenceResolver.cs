using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Smo;

internal static class ForeignKeyEvidenceResolver
{
    public static IReadOnlyList<ForeignKeyEvidenceMatch> Resolve(
        EntityEmissionContext ownerContext,
        EntityEmissionContext targetContext,
        AttributeModel attribute,
        AttributeModel referencedColumn,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>> relationshipsByAttribute,
        IReadOnlyDictionary<string, AttributeModel> attributesByLogicalName,
        HashSet<string> processedConstraints,
        Func<string?, ForeignKeyAction> deleteRuleMapper)
    {
        if (ownerContext is null)
        {
            throw new ArgumentNullException(nameof(ownerContext));
        }

        if (targetContext is null)
        {
            throw new ArgumentNullException(nameof(targetContext));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (referencedColumn is null)
        {
            throw new ArgumentNullException(nameof(referencedColumn));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (foreignKeyReality is null)
        {
            throw new ArgumentNullException(nameof(foreignKeyReality));
        }

        if (relationshipsByAttribute is null)
        {
            throw new ArgumentNullException(nameof(relationshipsByAttribute));
        }

        if (attributesByLogicalName is null)
        {
            throw new ArgumentNullException(nameof(attributesByLogicalName));
        }

        if (processedConstraints is null)
        {
            throw new ArgumentNullException(nameof(processedConstraints));
        }

        if (deleteRuleMapper is null)
        {
            throw new ArgumentNullException(nameof(deleteRuleMapper));
        }

        if (!relationshipsByAttribute.TryGetValue(attribute.LogicalName.Value, out var relationships) ||
            relationships.IsDefaultOrEmpty)
        {
            return Array.Empty<ForeignKeyEvidenceMatch>();
        }

        var matches = new List<ForeignKeyEvidenceMatch>();
        foreach (var relationship in relationships)
        {
            if (relationship.ActualConstraints.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var constraint in relationship.ActualConstraints)
            {
                var orderedColumns = constraint.Columns
                    .Where(static column =>
                        !string.IsNullOrWhiteSpace(column.OwnerColumn) ||
                        !string.IsNullOrWhiteSpace(column.OwnerAttribute))
                    .OrderBy(static column => column.Ordinal)
                    .ToImmutableArray();

                if (orderedColumns.Length == 0)
                {
                    continue;
                }

                if (!orderedColumns.Any(column => ColumnMatches(column, attribute)))
                {
                    continue;
                }

                var ownerColumns = ResolveOwnerColumns(orderedColumns, attribute, attributesByLogicalName);
                if (ownerColumns.Length == 0)
                {
                    continue;
                }

                var ownerCoordinates = BuildOwnerCoordinates(ownerContext, ownerColumns);
                if (!AllColumnsApproved(ownerCoordinates, decisions))
                {
                    continue;
                }

                var referencedColumns = ResolveReferencedColumns(orderedColumns, targetContext, referencedColumn.ColumnName.Value);
                var key = BuildConstraintKey(relationship, constraint, ownerColumns, referencedColumns);
                if (!processedConstraints.Add(key))
                {
                    continue;
                }

                var isNoCheck = ownerCoordinates.Any(coord =>
                    foreignKeyReality.TryGetValue(coord, out var reality) && reality.IsNoCheck);

                var referencedTable = string.IsNullOrWhiteSpace(constraint.ReferencedTable)
                    ? targetContext.Entity.PhysicalName.Value
                    : constraint.ReferencedTable;

                var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                    ? targetContext.Entity.Schema.Value
                    : constraint.ReferencedSchema;

                var ownerAttributes = BuildOwnerAttributesForNaming(ownerContext, attribute, ownerColumns);
                var deleteAction = !string.IsNullOrWhiteSpace(constraint.OnDeleteAction)
                    ? deleteRuleMapper(constraint.OnDeleteAction)
                    : deleteRuleMapper(attribute.Reference.DeleteRuleCode);

                matches.Add(new ForeignKeyEvidenceMatch(
                    ownerColumns,
                    referencedColumns,
                    ownerAttributes,
                    constraint.Name,
                    referencedTable,
                    referencedSchema,
                    deleteAction,
                    isNoCheck));
            }
        }

        return matches;
    }

    private static bool ColumnMatches(RelationshipActualConstraintColumn column, AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(column.OwnerColumn) &&
            column.OwnerColumn.Equals(attribute.ColumnName.Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(column.OwnerAttribute) &&
            column.OwnerAttribute.Equals(attribute.LogicalName.Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static ImmutableArray<string> ResolveOwnerColumns(
        ImmutableArray<RelationshipActualConstraintColumn> columns,
        AttributeModel attribute,
        IReadOnlyDictionary<string, AttributeModel> attributesByLogicalName)
    {
        var builder = ImmutableArray.CreateBuilder<string>(columns.Length);
        foreach (var column in columns)
        {
            var resolved = ResolveOwnerColumn(column, attribute, attributesByLogicalName);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return ImmutableArray<string>.Empty;
            }

            builder.Add(resolved);
        }

        return builder.MoveToImmutable();
    }

    private static string ResolveOwnerColumn(
        RelationshipActualConstraintColumn column,
        AttributeModel attribute,
        IReadOnlyDictionary<string, AttributeModel> attributesByLogicalName)
    {
        if (!string.IsNullOrWhiteSpace(column.OwnerAttribute) &&
            attributesByLogicalName.TryGetValue(column.OwnerAttribute, out var ownerByLogical))
        {
            return ownerByLogical.ColumnName.Value;
        }

        if (!string.IsNullOrWhiteSpace(column.OwnerColumn))
        {
            return column.OwnerColumn;
        }

        if (!string.IsNullOrWhiteSpace(column.ReferencedAttribute) &&
            attributesByLogicalName.TryGetValue(column.ReferencedAttribute, out var referencedAttribute))
        {
            return referencedAttribute.ColumnName.Value;
        }

        return attribute.ColumnName.Value;
    }

    private static ImmutableArray<ColumnCoordinate> BuildOwnerCoordinates(
        EntityEmissionContext ownerContext,
        ImmutableArray<string> ownerColumns)
    {
        return ownerColumns
            .Select(column =>
            {
                if (ownerContext.AttributeLookup.TryGetValue(column, out var ownerAttribute))
                {
                    return new ColumnCoordinate(
                        ownerContext.Entity.Schema,
                        ownerContext.Entity.PhysicalName,
                        ownerAttribute.ColumnName);
                }

                return new ColumnCoordinate(
                    ownerContext.Entity.Schema,
                    ownerContext.Entity.PhysicalName,
                    new ColumnName(column));
            })
            .ToImmutableArray();
    }

    private static bool AllColumnsApproved(
        ImmutableArray<ColumnCoordinate> coordinates,
        PolicyDecisionSet decisions)
    {
        var hasDecision = false;
        foreach (var coordinate in coordinates)
        {
            if (!decisions.ForeignKeys.TryGetValue(coordinate, out var decision))
            {
                continue;
            }

            hasDecision = true;

            if (!decision.CreateConstraint)
            {
                return false;
            }
        }

        return hasDecision;
    }

    private static ImmutableArray<string> ResolveReferencedColumns(
        ImmutableArray<RelationshipActualConstraintColumn> columns,
        EntityEmissionContext targetContext,
        string fallbackColumn)
    {
        var builder = ImmutableArray.CreateBuilder<string>(columns.Length);
        foreach (var column in columns)
        {
            if (!string.IsNullOrWhiteSpace(column.ReferencedAttribute))
            {
                var matched = targetContext.Entity.Attributes.FirstOrDefault(attr =>
                    attr.LogicalName.Value.Equals(column.ReferencedAttribute, StringComparison.OrdinalIgnoreCase));
                if (matched is not null)
                {
                    builder.Add(matched.ColumnName.Value);
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(column.ReferencedColumn))
            {
                builder.Add(column.ReferencedColumn);
                continue;
            }

            builder.Add(fallbackColumn);
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<AttributeModel> BuildOwnerAttributesForNaming(
        EntityEmissionContext ownerContext,
        AttributeModel attribute,
        ImmutableArray<string> ownerColumns)
    {
        var attributes = ownerColumns
            .Select(column => ownerContext.AttributeLookup.TryGetValue(column, out var ownerAttribute)
                ? ownerAttribute
                : null)
            .Where(static candidate => candidate is not null)
            .Cast<AttributeModel>()
            .ToImmutableArray();

        return attributes.IsDefault ? ImmutableArray.Create(attribute) : attributes;
    }

    private static string BuildConstraintKey(
        RelationshipModel relationship,
        RelationshipActualConstraint constraint,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<string> referencedColumns)
    {
        var namePart = string.IsNullOrWhiteSpace(constraint.Name) ? relationship.ViaAttribute.Value : constraint.Name;
        return $"{relationship.ViaAttribute.Value}|{namePart}|{string.Join('|', ownerColumns)}|{string.Join('|', referencedColumns)}";
    }
}

internal sealed record ForeignKeyEvidenceMatch(
    ImmutableArray<string> OwnerColumns,
    ImmutableArray<string> ReferencedColumns,
    ImmutableArray<AttributeModel> OwnerAttributesForNaming,
    string? ProvidedConstraintName,
    string ReferencedTable,
    string ReferencedSchema,
    ForeignKeyAction DeleteAction,
    bool IsNoCheck);
