using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Smo;

public static class IndexNameGenerator
{
    public static string Generate(
        EntityModel entity,
        ImmutableArray<AttributeModel> keyAttributes,
        bool isUnique,
        SmoFormatOptions format)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var attributes = keyAttributes.IsDefaultOrEmpty
            ? ImmutableArray<AttributeModel>.Empty
            : keyAttributes;

        var prefix = isUnique ? "UIX" : "IX";
        var columnSegment = attributes.IsDefaultOrEmpty
            ? entity.PhysicalName.Value
            : string.Join('_', attributes.Select(ResolveEmissionColumnName));

        var baseName = string.Concat(prefix, "_", entity.PhysicalName.Value, "_", columnSegment);

        return ConstraintNameNormalizer.Normalize(
            baseName,
            entity,
            attributes,
            isUnique ? ConstraintNameKind.UniqueIndex : ConstraintNameKind.NonUniqueIndex,
            format);
    }

    private static string ResolveEmissionColumnName(AttributeModel attribute)
    {
        if (attribute is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(attribute.LogicalName.Value))
        {
            return attribute.LogicalName.Value;
        }

        return attribute.ColumnName.Value;
    }
}
