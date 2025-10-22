using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using EntityEmissionContext = Osm.Smo.SmoModelFactory.EntityEmissionContext;

namespace Osm.Smo;

internal static class SmoIndexBuilder
{
    public static ImmutableArray<SmoIndexDefinition> BuildIndexes(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        bool includePlatformAuto,
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

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var builder = ImmutableArray.CreateBuilder<SmoIndexDefinition>();
        var uniqueDecisions = decisions.UniqueIndexes;

        var domainPrimaryIndex = context.Entity.Indexes.FirstOrDefault(i => i.IsPrimary);
        var primaryMetadata = domainPrimaryIndex is not null
            ? MapIndexMetadata(domainPrimaryIndex)
            : SmoIndexMetadata.Empty;

        var primaryColumns = BuildPrimaryKeyColumns(context, domainPrimaryIndex, out var primaryAttributes);
        if (!primaryColumns.IsDefaultOrEmpty)
        {
            var pkName = ConstraintNameNormalizer.Normalize(
                $"PK_{context.Entity.PhysicalName.Value}",
                context.Entity,
                primaryAttributes.IsDefaultOrEmpty ? context.IdentifierAttributes : primaryAttributes,
                ConstraintNameKind.PrimaryKey,
                format);

            builder.Add(new SmoIndexDefinition(
                pkName,
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                primaryColumns,
                primaryMetadata));
        }

        foreach (var index in context.Entity.Indexes)
        {
            if (index.IsPrimary)
            {
                continue;
            }

            if (!includePlatformAuto && index.IsPlatformAuto && !index.IsUnique)
            {
                continue;
            }

            var referencedAttributes = new List<AttributeModel>(index.Columns.Length);
            var orderedColumns = index.Columns.ToBuilder();
            orderedColumns.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));

            var columnsBuilder = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(orderedColumns.Count);
            foreach (var column in orderedColumns)
            {
                if (!context.AttributeLookup.TryGetValue(column.Column.Value, out var attribute))
                {
                    referencedAttributes.Clear();
                    columnsBuilder.Clear();
                    break;
                }

                if (!column.IsIncluded)
                {
                    referencedAttributes.Add(attribute);
                }

                var isDescending = column.Direction == IndexColumnDirection.Descending;
                var emittedName = ResolveEmissionColumnName(attribute);
                columnsBuilder.Add(new SmoIndexColumnDefinition(emittedName, column.Ordinal, column.IsIncluded, isDescending));
            }

            if (columnsBuilder.Count == 0)
            {
                continue;
            }

            var columns = columnsBuilder.ToImmutable();
            if (!columns.Any(c => !c.IsIncluded))
            {
                continue;
            }

            var normalizedName = ConstraintNameNormalizer.Normalize(
                index.Name.Value,
                context.Entity,
                referencedAttributes,
                index.IsUnique ? ConstraintNameKind.UniqueIndex : ConstraintNameKind.NonUniqueIndex,
                format);

            var indexCoordinate = new IndexCoordinate(context.Entity.Schema, context.Entity.PhysicalName, index.Name);
            var enforceUnique = index.IsUnique;
            if (index.IsUnique && uniqueDecisions.TryGetValue(indexCoordinate, out var decision))
            {
                enforceUnique = decision.EnforceUnique;
            }

            var metadata = MapIndexMetadata(index);
            builder.Add(new SmoIndexDefinition(
                normalizedName,
                enforceUnique,
                IsPrimaryKey: false,
                index.IsPlatformAuto,
                columns,
                metadata));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmoIndexColumnDefinition> BuildPrimaryKeyColumns(
        EntityEmissionContext context,
        IndexModel? domainPrimaryIndex,
        out ImmutableArray<AttributeModel> referencedAttributes)
    {
        if (domainPrimaryIndex is not null && !domainPrimaryIndex.Columns.IsDefaultOrEmpty)
        {
            var orderedColumns = domainPrimaryIndex.Columns
                .Where(static column => !column.IsIncluded)
                .OrderBy(static column => column.Ordinal)
                .ToImmutableArray();

            if (!orderedColumns.IsDefaultOrEmpty)
            {
                var columnBuilder = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(orderedColumns.Length);
                var attributeBuilder = ImmutableArray.CreateBuilder<AttributeModel>(orderedColumns.Length);
                var ordinal = 1;
                var missingAttribute = false;

                foreach (var column in orderedColumns)
                {
                    if (!context.AttributeLookup.TryGetValue(column.Column.Value, out var attribute))
                    {
                        missingAttribute = true;
                        break;
                    }

                    var isDescending = column.Direction == IndexColumnDirection.Descending;
                    var emittedName = ResolveEmissionColumnName(attribute);
                    columnBuilder.Add(new SmoIndexColumnDefinition(emittedName, ordinal++, IsIncluded: false, isDescending));
                    attributeBuilder.Add(attribute);
                }

                if (!missingAttribute && columnBuilder.Count > 0)
                {
                    referencedAttributes = attributeBuilder.ToImmutable();
                    return columnBuilder.ToImmutable();
                }
            }
        }

        referencedAttributes = context.IdentifierAttributes.IsDefaultOrEmpty
            ? ImmutableArray<AttributeModel>.Empty
            : context.IdentifierAttributes;

        if (referencedAttributes.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoIndexColumnDefinition>.Empty;
        }

        var fallback = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(referencedAttributes.Length);
        for (var i = 0; i < referencedAttributes.Length; i++)
        {
            var attribute = referencedAttributes[i];
            var emittedName = ResolveEmissionColumnName(attribute);
            fallback.Add(new SmoIndexColumnDefinition(emittedName, i + 1, IsIncluded: false, IsDescending: false));
        }

        return fallback.ToImmutable();
    }

    private static string ResolveEmissionColumnName(AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.LogicalName.Value))
        {
            return attribute.LogicalName.Value;
        }

        return attribute.ColumnName.Value;
    }

    private static SmoIndexMetadata MapIndexMetadata(IndexModel index)
    {
        var onDisk = index.OnDisk;
        var partitionColumns = onDisk.PartitionColumns.IsDefaultOrEmpty
            ? ImmutableArray<SmoIndexPartitionColumn>.Empty
            : onDisk.PartitionColumns
                .OrderBy(static c => c.Ordinal)
                .Select(c => new SmoIndexPartitionColumn(c.Column.Value, c.Ordinal))
                .ToImmutableArray();

        var compression = onDisk.DataCompression.IsDefaultOrEmpty
            ? ImmutableArray<SmoIndexCompressionSetting>.Empty
            : onDisk.DataCompression
                .OrderBy(static c => c.PartitionNumber)
                .Select(c => new SmoIndexCompressionSetting(c.PartitionNumber, c.Compression))
                .ToImmutableArray();

        SmoIndexDataSpace? dataSpace = null;
        if (onDisk.DataSpace is not null)
        {
            dataSpace = new SmoIndexDataSpace(onDisk.DataSpace.Name, onDisk.DataSpace.Type);
        }

        return new SmoIndexMetadata(
            onDisk.IsDisabled,
            onDisk.IsPadded,
            onDisk.FillFactor,
            onDisk.IgnoreDuplicateKey,
            onDisk.AllowRowLocks,
            onDisk.AllowPageLocks,
            onDisk.NoRecomputeStatistics,
            onDisk.FilterDefinition,
            dataSpace,
            partitionColumns,
            compression);
    }
}
