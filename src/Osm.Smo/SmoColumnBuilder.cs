using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Smo;

internal static class SmoColumnBuilder
{
    public static ImmutableArray<SmoColumnDefinition> BuildColumns(SmoEntityEmitter emitter)
    {
        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        var context = emitter.Context;
        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in context.EmittableAttributes)
        {
            var dataType = emitter.ResolveAttributeDataType(attribute);
            var nullable = !emitter.ShouldEnforceNotNull(attribute);
            var onDisk = attribute.OnDisk;
            var isIdentity = onDisk.IsIdentity ?? attribute.IsAutoNumber;
            var identitySeed = isIdentity ? 1 : 0;
            var identityIncrement = isIdentity ? 1 : 0;
            var isComputed = onDisk.IsComputed ?? false;
            var computed = isComputed ? onDisk.ComputedDefinition : null;
            var coordinate = emitter.CreateCoordinate(attribute);
            var defaultExpression = SmoNormalization.NormalizeSqlExpression(
                emitter.ResolveDefaultExpression(attribute, coordinate));

            if (defaultExpression is not null && dataType.SqlDataType == SqlDataType.Bit)
            {
                defaultExpression = NormalizeBitDefaultExpression(defaultExpression);
            }
            var collation = SmoNormalization.NormalizeWhitespace(onDisk.Collation);
            var description = MsDescriptionResolver.Resolve(attribute.Metadata);
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
                emitter.ResolveEmissionColumnName(attribute),
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

    private static string NormalizeBitDefaultExpression(string expression)
    {
        var normalizedLiteral = TryNormalizeBooleanLiteral(expression);
        if (normalizedLiteral is null)
        {
            return expression;
        }

        return normalizedLiteral.Value ? "(1)" : "(0)";
    }

    private static bool? TryNormalizeBooleanLiteral(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var candidate = expression.Trim();
        for (var i = 0; i < 4 && candidate.Length > 0; i++)
        {
            if (IsBooleanLiteral(candidate, out var value))
            {
                return value;
            }

            if (candidate.Length < 2 || candidate[0] != '(' || candidate[^1] != ')')
            {
                break;
            }

            candidate = candidate[1..^1].Trim();
        }

        return null;
    }

    private static bool IsBooleanLiteral(string value, out bool result)
    {
        if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.Ordinal))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.Ordinal))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
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
