using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model.Artifacts;

namespace Osm.Smo;

public static class TableArtifactSnapshotExtensions
{
    public static TableArtifactSnapshot ToSnapshot(this SmoTableDefinition table)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        var identity = TableArtifactIdentity.Create(
            table.Module,
            table.OriginalModule,
            table.Schema,
            table.Name,
            table.LogicalName,
            table.Catalog);
        var metadata = TableArtifactMetadata.Create(table.Description);

        var columns = table.Columns.Select(ToSnapshot).ToImmutableArray();
        var indexes = table.Indexes.Select(ToSnapshot).ToImmutableArray();
        var foreignKeys = table.ForeignKeys.Select(ToSnapshot).ToImmutableArray();
        var triggers = table.Triggers.Select(ToSnapshot).ToImmutableArray();

        return TableArtifactSnapshot.Create(
            identity,
            columns,
            indexes,
            foreignKeys,
            triggers,
            metadata);
    }

    private static TableColumnSnapshot ToSnapshot(SmoColumnDefinition column)
    {
        var type = column.DataType;
        var dataType = TableColumnTypeSnapshot.Create(
            type.SqlDataType.ToString(),
            type.Name,
            type.Schema,
            type.MaximumLength,
            type.NumericPrecision,
            type.NumericScale);

        var defaultConstraint = column.DefaultConstraint is null
            ? null
            : TableDefaultConstraintSnapshot.Create(
                column.DefaultConstraint.Name,
                column.DefaultConstraint.Expression,
                column.DefaultConstraint.IsNotTrusted);

        var checkConstraints = column.CheckConstraints.IsDefaultOrEmpty
            ? Array.Empty<TableCheckConstraintSnapshot>()
            : column.CheckConstraints.Select(
                constraint => TableCheckConstraintSnapshot.Create(
                    constraint.Name,
                    constraint.Expression,
                    constraint.IsNotTrusted));

        return TableColumnSnapshot.Create(
            column.PhysicalName,
            column.Name,
            column.LogicalName,
            dataType,
            column.Nullable,
            column.IsIdentity,
            column.IdentitySeed,
            column.IdentityIncrement,
            column.IsComputed,
            column.ComputedExpression,
            column.DefaultExpression,
            column.Collation,
            column.Description,
            defaultConstraint,
            checkConstraints);
    }

    private static TableIndexSnapshot ToSnapshot(SmoIndexDefinition index)
    {
        var columns = index.Columns.Select(
            column => TableIndexColumnSnapshot.Create(
                column.Name,
                column.Ordinal,
                column.IsIncluded,
                column.IsDescending));

        var partitionColumns = index.Metadata.PartitionColumns.Select(
            column => TableIndexPartitionColumnSnapshot.Create(column.Name, column.Ordinal));

        var compression = index.Metadata.DataCompression.Select(
            setting => TableIndexCompressionSnapshot.Create(setting.PartitionNumber, setting.Compression));

        var metadata = TableIndexMetadataSnapshot.Create(
            index.Metadata.IsDisabled,
            index.Metadata.IsPadded,
            index.Metadata.FillFactor,
            index.Metadata.IgnoreDuplicateKey,
            index.Metadata.AllowRowLocks,
            index.Metadata.AllowPageLocks,
            index.Metadata.StatisticsNoRecompute,
            index.Metadata.FilterDefinition,
            index.Metadata.DataSpace is null
                ? null
                : TableIndexDataSpaceSnapshot.Create(
                    index.Metadata.DataSpace.Name,
                    index.Metadata.DataSpace.Type),
            partitionColumns,
            compression);

        return TableIndexSnapshot.Create(
            index.Name,
            index.IsUnique,
            index.IsPrimaryKey,
            index.IsPlatformAuto,
            index.Description,
            columns,
            metadata);
    }

    private static TableForeignKeySnapshot ToSnapshot(SmoForeignKeyDefinition foreignKey)
    {
        return TableForeignKeySnapshot.Create(
            foreignKey.Name,
            foreignKey.Columns,
            foreignKey.ReferencedModule,
            foreignKey.ReferencedTable,
            foreignKey.ReferencedSchema,
            foreignKey.ReferencedColumns,
            foreignKey.ReferencedLogicalTable,
            foreignKey.DeleteAction.ToString(),
            foreignKey.IsNoCheck);
    }

    private static TableTriggerSnapshot ToSnapshot(SmoTriggerDefinition trigger)
    {
        return TableTriggerSnapshot.Create(
            trigger.Name,
            trigger.Schema,
            trigger.Table,
            trigger.IsDisabled,
            trigger.Definition);
    }
}
