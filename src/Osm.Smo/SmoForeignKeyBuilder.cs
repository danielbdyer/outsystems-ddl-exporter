using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Smo;

internal static class SmoForeignKeyBuilder
{
    public static ImmutableArray<SmoForeignKeyDefinition> BuildForeignKeys(SmoEntityEmitter emitter)
    {
        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        var context = emitter.Context;
        var decisions = emitter.Decisions;
        var builder = ImmutableArray.CreateBuilder<SmoForeignKeyDefinition>();
        var relationshipsByAttribute = emitter.RelationshipsByAttribute;
        var attributesByLogicalName = emitter.AttributesByLogicalName;
        var processedConstraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in context.EmittableAttributes)
        {
            if (!attribute.Reference.IsReference)
            {
                continue;
            }

            var coordinate = emitter.CreateCoordinate(attribute);
            if (!decisions.ForeignKeys.TryGetValue(coordinate, out var decision) || !decision.CreateConstraint)
            {
                continue;
            }

            if (!emitter.TryResolveReference(attribute, out var targetEntity))
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
                emitter.ForeignKeyReality,
                relationshipsByAttribute,
                attributesByLogicalName,
                processedConstraints,
                emitter.MapDeleteRule);

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
                        emitter.Format);

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
                emitter.ForeignKeyReality,
                emitter.Format,
                emitter.MapDeleteRule);

            builder.Add(fallbackDefinition);
        }

        return builder.ToImmutable();
    }
}
