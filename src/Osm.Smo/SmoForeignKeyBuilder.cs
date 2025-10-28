using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using EntityEmissionContext = Osm.Smo.SmoModelFactory.EntityEmissionContext;
using EntityEmissionIndex = Osm.Smo.SmoModelFactory.EntityEmissionIndex;

namespace Osm.Smo;

internal static class SmoForeignKeyBuilder
{
    public static ImmutableArray<SmoForeignKeyDefinition> BuildForeignKeys(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        EntityEmissionIndex entityLookup,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        SmoFormatOptions format)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (entityLookup is null)
        {
            throw new ArgumentNullException(nameof(entityLookup));
        }

        if (foreignKeyReality is null)
        {
            throw new ArgumentNullException(nameof(foreignKeyReality));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var builder = ImmutableArray.CreateBuilder<SmoForeignKeyDefinition>();
        var relationshipsByAttribute = context.Entity.Relationships
            .GroupBy(static relationship => relationship.ViaAttribute.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static grouping => grouping.Key, static grouping => grouping.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        var attributesByLogicalName = context.Entity.Attributes
            .ToDictionary(static attribute => attribute.LogicalName.Value, StringComparer.OrdinalIgnoreCase);
        var processedConstraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in context.EmittableAttributes)
        {
            if (!attribute.Reference.IsReference)
            {
                continue;
            }

            var coordinate = new ColumnCoordinate(context.Entity.Schema, context.Entity.PhysicalName, attribute.ColumnName);
            if (!decisions.ForeignKeys.TryGetValue(coordinate, out var decision) || !decision.CreateConstraint)
            {
                continue;
            }

            if (!entityLookup.TryResolveReference(attribute.Reference, context, out var targetEntity))
            {
                var targetName = attribute.Reference.TargetEntity?.Value ?? attribute.Reference.TargetPhysicalName?.Value ?? "<unknown>";
                throw new InvalidOperationException($"Target entity '{targetName}' not found for foreign key on '{context.Entity.PhysicalName.Value}.{attribute.ColumnName.Value}'.");
            }

            var referencedColumn = targetEntity.GetPreferredIdentifier();
            if (referencedColumn is null)
            {
                throw new InvalidOperationException($"Target entity '{targetEntity.Entity.PhysicalName.Value}' is missing an identifier column for foreign key creation.");
            }

            var emittedFromEvidence = false;
            if (relationshipsByAttribute.TryGetValue(attribute.LogicalName.Value, out var relationships))
            {
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

                        var resolvedOwnerColumns = ResolveOwnerColumns(
                            orderedColumns,
                            attribute,
                            attributesByLogicalName);

                        if (resolvedOwnerColumns.Length == 0)
                        {
                            continue;
                        }

                        var coordinates = resolvedOwnerColumns
                            .Select(column =>
                            {
                                if (context.AttributeLookup.TryGetValue(column, out var ownerAttribute))
                                {
                                    return new ColumnCoordinate(
                                        context.Entity.Schema,
                                        context.Entity.PhysicalName,
                                        ownerAttribute.ColumnName);
                                }

                                return new ColumnCoordinate(
                                    context.Entity.Schema,
                                    context.Entity.PhysicalName,
                                    new ColumnName(column));
                            })
                            .ToImmutableArray();

                        if (!AllColumnsApproved(coordinates, decisions))
                        {
                            continue;
                        }

                        var resolvedReferencedColumns = ResolveReferencedColumns(
                            orderedColumns,
                            targetEntity,
                            referencedColumn.ColumnName.Value);

                        var key = BuildConstraintKey(relationship, constraint, resolvedOwnerColumns, resolvedReferencedColumns);
                        if (!processedConstraints.Add(key))
                        {
                            continue;
                        }

                        var isNoCheck = coordinates.Any(coord =>
                            foreignKeyReality.TryGetValue(coord, out var reality) && reality.IsNoCheck);

                        var referencedTable = string.IsNullOrWhiteSpace(constraint.ReferencedTable)
                            ? targetEntity.Entity.PhysicalName.Value
                            : constraint.ReferencedTable;
                        var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                            ? targetEntity.Entity.Schema.Value
                            : constraint.ReferencedSchema;

                        var referencedAttributes = resolvedOwnerColumns
                            .Select(column => context.AttributeLookup.TryGetValue(column, out var ownerAttribute)
                                ? ownerAttribute
                                : null)
                            .Where(static attribute => attribute is not null)
                            .Cast<AttributeModel>()
                            .ToImmutableArray();

                        if (referencedAttributes.IsDefault)
                        {
                            referencedAttributes = ImmutableArray.Create(attribute);
                        }

                        var deleteAction = !string.IsNullOrWhiteSpace(constraint.OnDeleteAction)
                            ? MapDeleteRule(constraint.OnDeleteAction)
                            : MapDeleteRule(attribute.Reference.DeleteRuleCode);

                        var referencedTableSegment = string.IsNullOrWhiteSpace(constraint.ReferencedTable)
                            ? targetEntity.Entity.PhysicalName.Value
                            : constraint.ReferencedTable;

                        var ownerColumnsSegment = string.Join('_', resolvedOwnerColumns);
                        var baseName = $"FK_{context.Entity.PhysicalName.Value}_{referencedTableSegment}_{ownerColumnsSegment}";

                        var name = ConstraintNameNormalizer.Normalize(
                            baseName,
                            context.Entity,
                            referencedAttributes,
                            ConstraintNameKind.ForeignKey,
                            format);

                        var ownerColumnNames = NormalizeColumnNames(resolvedOwnerColumns, context.AttributeLookup);
                        var referencedColumnNames = NormalizeColumnNames(resolvedReferencedColumns, targetEntity.AttributeLookup);

                        builder.Add(new SmoForeignKeyDefinition(
                            name,
                            ownerColumnNames,
                            targetEntity.ModuleName,
                            referencedTable,
                            referencedSchema,
                            referencedColumnNames,
                            targetEntity.Entity.LogicalName.Value,
                            deleteAction,
                            isNoCheck));

                        emittedFromEvidence = true;
                    }
                }
            }

            if (emittedFromEvidence)
            {
                continue;
            }

            var ownerColumnsFallback = ImmutableArray.Create(attribute.ColumnName.Value);
            var referencedColumnsFallback = ImmutableArray.Create(referencedColumn.ColumnName.Value);
            var isNoCheckFallback = foreignKeyReality.TryGetValue(coordinate, out var realityFallback) && realityFallback.IsNoCheck;
            var nameFallback = ConstraintNameNormalizer.Normalize(
                $"FK_{context.Entity.PhysicalName.Value}_{targetEntity.Entity.PhysicalName.Value}_{attribute.ColumnName.Value}",
                context.Entity,
                new[] { attribute },
                ConstraintNameKind.ForeignKey,
                format);

            var friendlyOwnerColumns = NormalizeColumnNames(ownerColumnsFallback, context.AttributeLookup);
            var friendlyReferencedColumns = NormalizeColumnNames(referencedColumnsFallback, targetEntity.AttributeLookup);

            builder.Add(new SmoForeignKeyDefinition(
                nameFallback,
                friendlyOwnerColumns,
                targetEntity.ModuleName,
                targetEntity.Entity.PhysicalName.Value,
                targetEntity.Entity.Schema.Value,
                friendlyReferencedColumns,
                targetEntity.Entity.LogicalName.Value,
                MapDeleteRule(attribute.Reference.DeleteRuleCode),
                isNoCheckFallback));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> NormalizeColumnNames(
        ImmutableArray<string> columns,
        IReadOnlyDictionary<string, AttributeModel> attributeLookup)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return columns;
        }

        var builder = ImmutableArray.CreateBuilder<string>(columns.Length);
        foreach (var column in columns)
        {
            if (!string.IsNullOrWhiteSpace(column) &&
                attributeLookup.TryGetValue(column, out var attribute) &&
                !string.IsNullOrWhiteSpace(attribute.LogicalName.Value))
            {
                builder.Add(attribute.LogicalName.Value);
            }
            else
            {
                builder.Add(column);
            }
        }

        return builder.ToImmutable();
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

    private static ImmutableArray<string> ResolveReferencedColumns(
        ImmutableArray<RelationshipActualConstraintColumn> columns,
        EntityEmissionContext targetEntity,
        string fallbackColumn)
    {
        var builder = ImmutableArray.CreateBuilder<string>(columns.Length);
        foreach (var column in columns)
        {
            if (!string.IsNullOrWhiteSpace(column.ReferencedAttribute))
            {
                var matched = targetEntity.Entity.Attributes.FirstOrDefault(attr =>
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

    private static string BuildConstraintKey(
        RelationshipModel relationship,
        RelationshipActualConstraint constraint,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<string> referencedColumns)
    {
        var namePart = string.IsNullOrWhiteSpace(constraint.Name) ? relationship.ViaAttribute.Value : constraint.Name;
        return $"{relationship.ViaAttribute.Value}|{namePart}|{string.Join('|', ownerColumns)}|{string.Join('|', referencedColumns)}";
    }

    private static ForeignKeyAction MapDeleteRule(string? deleteRule)
    {
        if (string.IsNullOrWhiteSpace(deleteRule))
        {
            return ForeignKeyAction.NoAction;
        }

        return deleteRule.Trim() switch
        {
            "Cascade" => ForeignKeyAction.Cascade,
            "Delete" => ForeignKeyAction.Cascade,
            "Protect" => ForeignKeyAction.NoAction,
            "Ignore" => ForeignKeyAction.NoAction,
            "SetNull" => ForeignKeyAction.SetNull,
            _ => ForeignKeyAction.NoAction,
        };
    }
}
