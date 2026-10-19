using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Smo;

internal static class ForeignKeyFallbackFactory
{
    public static SmoForeignKeyDefinition CreateDefinition(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        AttributeModel attribute,
        AttributeModel referencedIdentifier,
        ColumnCoordinate coordinate,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        SmoFormatOptions format,
        Func<string?, ForeignKeyAction> deleteRuleMapper,
        bool scriptWithNoCheck)
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

        if (referencedIdentifier is null)
        {
            throw new ArgumentNullException(nameof(referencedIdentifier));
        }

        if (foreignKeyReality is null)
        {
            throw new ArgumentNullException(nameof(foreignKeyReality));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        if (deleteRuleMapper is null)
        {
            throw new ArgumentNullException(nameof(deleteRuleMapper));
        }

        var ownerColumns = ImmutableArray.Create(attribute.ColumnName.Value);
        var referencedColumns = ImmutableArray.Create(referencedIdentifier.ColumnName.Value);
        var friendlyOwnerColumns = ForeignKeyColumnNormalizer.Normalize(ownerColumns, ownerContext.AttributeLookup);
        var friendlyReferencedColumns = ForeignKeyColumnNormalizer.Normalize(referencedColumns, referencedContext.AttributeLookup);
        var name = ForeignKeyNameFactory.CreateFallbackName(ownerContext, referencedContext, attribute, format);
        var deleteAction = deleteRuleMapper(attribute.Reference.DeleteRuleCode);
        var isNoCheck = scriptWithNoCheck ||
            (foreignKeyReality.TryGetValue(coordinate, out var reality) && reality.IsNoCheck);

        return new SmoForeignKeyDefinition(
            name,
            friendlyOwnerColumns,
            referencedContext.ModuleName,
            referencedContext.Entity.PhysicalName.Value,
            referencedContext.Entity.Schema.Value,
            friendlyReferencedColumns,
            referencedContext.Entity.LogicalName.Value,
            deleteAction,
            isNoCheck);
    }
}
