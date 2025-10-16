using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

            var isNoCheck = foreignKeyReality.TryGetValue(coordinate, out var reality) && reality.IsNoCheck;
            var referencedAttributes = new[] { attribute };
            var name = ConstraintNameNormalizer.Normalize(
                $"FK_{context.Entity.PhysicalName.Value}_{attribute.ColumnName.Value}",
                context.Entity,
                referencedAttributes,
                ConstraintNameKind.ForeignKey,
                format);
            builder.Add(new SmoForeignKeyDefinition(
                name,
                attribute.LogicalName.Value,
                targetEntity.ModuleName,
                targetEntity.Entity.PhysicalName.Value,
                targetEntity.Entity.Schema.Value,
                referencedColumn.LogicalName.Value,
                targetEntity.Entity.LogicalName.Value,
                MapDeleteRule(attribute.Reference.DeleteRuleCode),
                isNoCheck));
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
}
