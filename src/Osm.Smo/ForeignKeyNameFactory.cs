using System;
using System.Collections.Immutable;
using Osm.Domain.Model;

namespace Osm.Smo;

internal static class ForeignKeyNameFactory
{
    public static string CreateEvidenceName(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        string? providedConstraintName,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<AttributeModel> ownerAttributes,
        string referencedTable,
        SmoFormatOptions format)
    {
        if (ownerContext is null)
        {
            throw new ArgumentNullException(nameof(ownerContext));
        }

        if (referencedContext is null)
        {
            throw new ArgumentNullException(nameof(referencedContext));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var baseName = string.IsNullOrWhiteSpace(providedConstraintName)
            ? $"FK_{ownerContext.Entity.PhysicalName.Value}_{referencedTable}_{string.Join('_', ownerColumns)}"
            : AppendColumnSegment(providedConstraintName!, ownerColumns);

        return ConstraintNameNormalizer.Normalize(
            baseName,
            ownerContext.Entity,
            ownerAttributes,
            ConstraintNameKind.ForeignKey,
            format,
            referencedEntity: referencedContext.Entity);
    }

    public static string CreateFallbackName(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        AttributeModel attribute,
        SmoFormatOptions format)
    {
        if (ownerContext is null)
        {
            throw new ArgumentNullException(nameof(ownerContext));
        }

        if (referencedContext is null)
        {
            throw new ArgumentNullException(nameof(referencedContext));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var baseName = $"FK_{ownerContext.Entity.PhysicalName.Value}_{referencedContext.Entity.PhysicalName.Value}_{attribute.ColumnName.Value}";
        return ConstraintNameNormalizer.Normalize(
            baseName,
            ownerContext.Entity,
            new[] { attribute },
            ConstraintNameKind.ForeignKey,
            format,
            referencedEntity: referencedContext.Entity);
    }

    private static string AppendColumnSegment(string name, ImmutableArray<string> ownerColumns)
    {
        if (string.IsNullOrWhiteSpace(name) || ownerColumns.IsDefaultOrEmpty)
        {
            return name;
        }

        var needsAppend = false;
        foreach (var column in ownerColumns)
        {
            if (!name.Contains(column, StringComparison.OrdinalIgnoreCase))
            {
                needsAppend = true;
                break;
            }
        }

        if (!needsAppend)
        {
            return name;
        }

        var segment = string.Join('_', ownerColumns);
        return $"{name}_{segment}";
    }
}
