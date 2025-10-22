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

internal static class SmoColumnBuilder
{
    public static ImmutableArray<SmoColumnDefinition> BuildColumns(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        TypeMappingPolicy typeMappingPolicy,
        EntityEmissionIndex entityLookup)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (profileDefaults is null)
        {
            throw new ArgumentNullException(nameof(profileDefaults));
        }

        if (typeMappingPolicy is null)
        {
            throw new ArgumentNullException(nameof(typeMappingPolicy));
        }

        if (entityLookup is null)
        {
            throw new ArgumentNullException(nameof(entityLookup));
        }

        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in context.EmittableAttributes)
        {
            var dataType = typeMappingPolicy.Resolve(attribute);

            if (attribute.Reference.IsReference &&
                entityLookup.TryResolveReference(attribute.Reference, context, out var targetContext))
            {
                var referencedIdentifier = targetContext.GetPreferredIdentifier();
                if (referencedIdentifier is not null)
                {
                    dataType = typeMappingPolicy.Resolve(referencedIdentifier);
                }
            }

            var nullable = !ShouldEnforceNotNull(context.Entity, attribute, decisions);
            var onDisk = attribute.OnDisk;
            var isIdentity = onDisk.IsIdentity ?? attribute.IsAutoNumber;
            var identitySeed = isIdentity ? 1 : 0;
            var identityIncrement = isIdentity ? 1 : 0;
            var isComputed = onDisk.IsComputed ?? false;
            var computed = isComputed ? onDisk.ComputedDefinition : null;
            var coordinate = new ColumnCoordinate(context.Entity.Schema, context.Entity.PhysicalName, attribute.ColumnName);
            var defaultExpression = SmoNormalization.NormalizeSqlExpression(
                ResolveDefaultExpression(onDisk.DefaultDefinition, profileDefaults, coordinate, attribute));
            var collation = SmoNormalization.NormalizeWhitespace(onDisk.Collation);
            var description = SmoNormalization.NormalizeWhitespace(attribute.Metadata.Description);
            var defaultConstraint = CreateDefaultConstraint(attribute.OnDisk.DefaultConstraint);

            var checkConstraints = ImmutableArray<SmoCheckConstraintDefinition>.Empty;
            if (!attribute.OnDisk.CheckConstraints.IsDefaultOrEmpty)
            {
                var checkBuilder = ImmutableArray.CreateBuilder<SmoCheckConstraintDefinition>(attribute.OnDisk.CheckConstraints.Length);
                foreach (var constraint in attribute.OnDisk.CheckConstraints)
                {
                    if (constraint is null)
                    {
                        continue;
                    }

                    var expression = SmoNormalization.NormalizeSqlExpression(constraint.Definition);
                    if (expression is null)
                    {
                        continue;
                    }

                    checkBuilder.Add(new SmoCheckConstraintDefinition(
                        SmoNormalization.NormalizeWhitespace(constraint.Name),
                        expression,
                        constraint.IsNotTrusted));
                }

                if (checkBuilder.Count > 0)
                {
                    checkConstraints = checkBuilder.ToImmutable().Sort(SmoCheckConstraintComparer.Instance);
                }
            }

            builder.Add(new SmoColumnDefinition(
                attribute.ColumnName.Value,
                ResolveEmissionColumnName(attribute),
                attribute.LogicalName.Value,
                dataType,
                nullable,
                isIdentity,
                identitySeed,
                identityIncrement,
                isComputed,
                computed,
                defaultExpression,
                collation,
                description,
                defaultConstraint,
                checkConstraints.IsDefaultOrEmpty ? ImmutableArray<SmoCheckConstraintDefinition>.Empty : checkConstraints));
        }

        return builder.ToImmutable();
    }

    private static string ResolveEmissionColumnName(AttributeModel attribute)
    {
        var logical = attribute.LogicalName.Value;
        if (!string.IsNullOrWhiteSpace(logical))
        {
            return logical;
        }

        return attribute.ColumnName.Value;
    }

    private static SmoDefaultConstraintDefinition? CreateDefaultConstraint(AttributeOnDiskDefaultConstraint? constraint)
    {
        if (constraint is not { Definition: { } definition })
        {
            return null;
        }

        var expression = SmoNormalization.NormalizeSqlExpression(definition);
        if (expression is null)
        {
            return null;
        }

        return new SmoDefaultConstraintDefinition(
            SmoNormalization.NormalizeWhitespace(constraint.Name),
            expression,
            constraint.IsNotTrusted);
    }

    private static string? ResolveDefaultExpression(
        string? onDiskDefault,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        ColumnCoordinate coordinate,
        AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(onDiskDefault))
        {
            return onDiskDefault;
        }

        if (profileDefaults.TryGetValue(coordinate, out var profileDefault) &&
            !string.IsNullOrWhiteSpace(profileDefault))
        {
            return profileDefault;
        }

        return string.IsNullOrWhiteSpace(attribute.DefaultValue) ? null : attribute.DefaultValue;
    }

    private static bool ShouldEnforceNotNull(EntityModel entity, AttributeModel attribute, PolicyDecisionSet decisions)
    {
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
        if (decisions.Nullability.TryGetValue(coordinate, out var decision))
        {
            return decision.MakeNotNull;
        }

        return attribute.IsMandatory;
    }

    private sealed class SmoCheckConstraintComparer : IComparer<SmoCheckConstraintDefinition>
    {
        public static readonly SmoCheckConstraintComparer Instance = new();

        public int Compare(SmoCheckConstraintDefinition? x, SmoCheckConstraintDefinition? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var leftName = x.Name ?? string.Empty;
            var rightName = y.Name ?? string.Empty;
            var comparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.Expression, y.Expression);
        }
    }
}
