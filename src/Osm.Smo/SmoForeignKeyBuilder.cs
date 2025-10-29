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
        var relationshipsByAttribute = BuildRelationshipLookup(context.Entity);
        var attributesByLogicalName = BuildAttributeLookup(context.Entity);
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

            var evidenceMatches = ForeignKeyEvidenceResolver.Resolve(
                context,
                targetEntity,
                attribute,
                referencedColumn,
                decisions,
                foreignKeyReality,
                relationshipsByAttribute,
                attributesByLogicalName,
                processedConstraints,
                MapDeleteRule);

            if (evidenceMatches.Count > 0)
            {
                foreach (var match in evidenceMatches)
                {
                    var name = ForeignKeyNameFactory.CreateEvidenceName(
                        context,
                        targetEntity,
                        match.ProvidedConstraintName,
                        match.OwnerColumns,
                        match.OwnerAttributesForNaming,
                        match.ReferencedTable,
                        format);

                    var ownerColumnNames = ForeignKeyColumnNormalizer.Normalize(match.OwnerColumns, context.AttributeLookup);
                    var referencedColumnNames = ForeignKeyColumnNormalizer.Normalize(match.ReferencedColumns, targetEntity.AttributeLookup);

                    builder.Add(new SmoForeignKeyDefinition(
                        name,
                        ownerColumnNames,
                        targetEntity.ModuleName,
                        match.ReferencedTable,
                        match.ReferencedSchema,
                        referencedColumnNames,
                        targetEntity.Entity.LogicalName.Value,
                        match.DeleteAction,
                        match.IsNoCheck));
                }

                continue;
            }

            var fallbackDefinition = ForeignKeyFallbackFactory.CreateDefinition(
                context,
                targetEntity,
                attribute,
                referencedColumn,
                coordinate,
                foreignKeyReality,
                format,
                MapDeleteRule);

            builder.Add(fallbackDefinition);
        }

        return builder.ToImmutable();
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

    private static IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>> BuildRelationshipLookup(EntityModel entity)
    {
        if (entity.Relationships.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, ImmutableArray<RelationshipModel>>.Empty;
        }

        return entity.Relationships
            .GroupBy(static relationship => relationship.ViaAttribute.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, AttributeModel> BuildAttributeLookup(EntityModel entity)
    {
        if (entity.Attributes.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, AttributeModel>.Empty;
        }

        return entity.Attributes
            .GroupBy(static attribute => attribute.LogicalName.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.First(),
                StringComparer.OrdinalIgnoreCase);
    }
}
