using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Domain.Model.Artifacts;

public sealed record TableArtifactSnapshot(
    TableArtifactIdentity Identity,
    ImmutableArray<TableColumnSnapshot> Columns,
    ImmutableArray<TableIndexSnapshot> Indexes,
    ImmutableArray<TableForeignKeySnapshot> ForeignKeys,
    ImmutableArray<TableTriggerSnapshot> Triggers,
    TableArtifactMetadata Metadata,
    TableArtifactProfilingMetadata? Profiling = null,
    TableArtifactEmissionMetadata? Emission = null)
{
    public static TableArtifactSnapshot Create(
        TableArtifactIdentity identity,
        IEnumerable<TableColumnSnapshot> columns,
        IEnumerable<TableIndexSnapshot> indexes,
        IEnumerable<TableForeignKeySnapshot> foreignKeys,
        IEnumerable<TableTriggerSnapshot> triggers,
        TableArtifactMetadata metadata,
        TableArtifactProfilingMetadata? profiling = null,
        TableArtifactEmissionMetadata? emission = null)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var columnArray = Normalize(columns);
        var indexArray = Normalize(indexes);
        var foreignKeyArray = Normalize(foreignKeys);
        var triggerArray = Normalize(triggers);

        ValidateColumnNames(columnArray);
        ValidateIndexColumns(indexArray);
        ValidateForeignKeyColumns(foreignKeyArray);

        return new TableArtifactSnapshot(
            identity,
            columnArray,
            indexArray,
            foreignKeyArray,
            triggerArray,
            metadata,
            profiling,
            emission);
    }

    public TableArtifactSnapshot WithEmission(TableArtifactEmissionMetadata emission)
    {
        return this with { Emission = emission ?? throw new ArgumentNullException(nameof(emission)) };
    }

    public TableArtifactSnapshot WithProfiling(TableArtifactProfilingMetadata profiling)
    {
        return this with { Profiling = profiling ?? throw new ArgumentNullException(nameof(profiling)) };
    }

    private static ImmutableArray<T> Normalize<T>(IEnumerable<T> source)
    {
        if (source is ImmutableArray<T> immutable)
        {
            return immutable.IsDefault ? ImmutableArray<T>.Empty : immutable;
        }

        if (source is null)
        {
            return ImmutableArray<T>.Empty;
        }

        return source.ToImmutableArray();
    }

    private static void ValidateColumnNames(ImmutableArray<TableColumnSnapshot> columns)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return;
        }

        var duplicates = columns
            .GroupBy(static column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new ArgumentException($"Duplicate column names detected: {string.Join(", ", duplicates)}", nameof(columns));
        }
    }

    private static void ValidateIndexColumns(ImmutableArray<TableIndexSnapshot> indexes)
    {
        if (indexes.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var index in indexes)
        {
            if (index.Columns.IsDefaultOrEmpty)
            {
                throw new ArgumentException($"Index '{index.Name}' must include at least one column.", nameof(indexes));
            }
        }
    }

    private static void ValidateForeignKeyColumns(ImmutableArray<TableForeignKeySnapshot> foreignKeys)
    {
        if (foreignKeys.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var foreignKey in foreignKeys)
        {
            if (foreignKey.Columns.IsDefaultOrEmpty || foreignKey.ReferencedColumns.IsDefaultOrEmpty)
            {
                throw new ArgumentException($"Foreign key '{foreignKey.Name}' must include column mappings.", nameof(foreignKeys));
            }

            if (foreignKey.Columns.Length != foreignKey.ReferencedColumns.Length)
            {
                throw new ArgumentException(
                    $"Foreign key '{foreignKey.Name}' must have matching source and target columns.",
                    nameof(foreignKeys));
            }
        }
    }
}

public sealed record TableArtifactIdentity(
    string Module,
    string OriginalModule,
    string Schema,
    string Name,
    string LogicalName,
    string? Catalog)
{
    public static TableArtifactIdentity Create(
        string module,
        string originalModule,
        string schema,
        string name,
        string logicalName,
        string? catalog)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            throw new ArgumentException("Module must be provided.", nameof(module));
        }

        if (string.IsNullOrWhiteSpace(originalModule))
        {
            throw new ArgumentException("Original module must be provided.", nameof(originalModule));
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new ArgumentException("Schema must be provided.", nameof(schema));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table name must be provided.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Logical name must be provided.", nameof(logicalName));
        }

        return new TableArtifactIdentity(module, originalModule, schema, name, logicalName, catalog);
    }
}

public sealed record TableArtifactMetadata(string? Description)
{
    public static TableArtifactMetadata Create(string? description)
        => new(description?.Trim());
}

public sealed record TableColumnSnapshot(
    string PhysicalName,
    string Name,
    string LogicalName,
    TableColumnTypeSnapshot DataType,
    bool Nullable,
    bool IsIdentity,
    int IdentitySeed,
    int IdentityIncrement,
    bool IsComputed,
    string? ComputedExpression,
    string? DefaultExpression,
    string? Collation,
    string? Description,
    TableDefaultConstraintSnapshot? DefaultConstraint,
    ImmutableArray<TableCheckConstraintSnapshot> CheckConstraints)
{
    public static TableColumnSnapshot Create(
        string physicalName,
        string name,
        string logicalName,
        TableColumnTypeSnapshot dataType,
        bool nullable,
        bool isIdentity,
        int identitySeed,
        int identityIncrement,
        bool isComputed,
        string? computedExpression,
        string? defaultExpression,
        string? collation,
        string? description,
        TableDefaultConstraintSnapshot? defaultConstraint,
        IEnumerable<TableCheckConstraintSnapshot>? checkConstraints = null)
    {
        if (string.IsNullOrWhiteSpace(physicalName))
        {
            throw new ArgumentException("Physical name must be provided.", nameof(physicalName));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be provided.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Logical name must be provided.", nameof(logicalName));
        }

        if (dataType is null)
        {
            throw new ArgumentNullException(nameof(dataType));
        }

        var constraints = checkConstraints is null
            ? ImmutableArray<TableCheckConstraintSnapshot>.Empty
            : checkConstraints.ToImmutableArray();

        return new TableColumnSnapshot(
            physicalName,
            name,
            logicalName,
            dataType,
            nullable,
            isIdentity,
            identitySeed,
            identityIncrement,
            isComputed,
            string.IsNullOrWhiteSpace(computedExpression) ? null : computedExpression,
            string.IsNullOrWhiteSpace(defaultExpression) ? null : defaultExpression,
            string.IsNullOrWhiteSpace(collation) ? null : collation,
            string.IsNullOrWhiteSpace(description) ? null : description,
            defaultConstraint,
            constraints.IsDefault ? ImmutableArray<TableCheckConstraintSnapshot>.Empty : constraints);
    }
}

public sealed record TableColumnTypeSnapshot(
    string SqlType,
    string? Name,
    string? Schema,
    int? MaximumLength,
    int? NumericPrecision,
    int? NumericScale)
{
    public static TableColumnTypeSnapshot Create(
        string sqlType,
        string? name,
        string? schema,
        int? maximumLength,
        int? numericPrecision,
        int? numericScale)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            throw new ArgumentException("SQL type must be provided.", nameof(sqlType));
        }

        return new TableColumnTypeSnapshot(
            sqlType,
            string.IsNullOrWhiteSpace(name) ? null : name,
            string.IsNullOrWhiteSpace(schema) ? null : schema,
            maximumLength,
            numericPrecision,
            numericScale);
    }
}

public sealed record TableDefaultConstraintSnapshot(string? Name, string Expression, bool IsNotTrusted)
{
    public static TableDefaultConstraintSnapshot Create(string? name, string expression, bool isNotTrusted)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("Constraint expression must be provided.", nameof(expression));
        }

        return new TableDefaultConstraintSnapshot(string.IsNullOrWhiteSpace(name) ? null : name, expression, isNotTrusted);
    }
}

public sealed record TableCheckConstraintSnapshot(string? Name, string Expression, bool IsNotTrusted)
{
    public static TableCheckConstraintSnapshot Create(string? name, string expression, bool isNotTrusted)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("Constraint expression must be provided.", nameof(expression));
        }

        return new TableCheckConstraintSnapshot(string.IsNullOrWhiteSpace(name) ? null : name, expression, isNotTrusted);
    }
}

public sealed record TableIndexSnapshot(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsPlatformAuto,
    string? Description,
    ImmutableArray<TableIndexColumnSnapshot> Columns,
    TableIndexMetadataSnapshot Metadata)
{
    public static TableIndexSnapshot Create(
        string name,
        bool isUnique,
        bool isPrimaryKey,
        bool isPlatformAuto,
        string? description,
        IEnumerable<TableIndexColumnSnapshot> columns,
        TableIndexMetadataSnapshot metadata)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Index name must be provided.", nameof(name));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var columnArray = columns is null
            ? ImmutableArray<TableIndexColumnSnapshot>.Empty
            : columns.ToImmutableArray();

        if (columnArray.IsDefaultOrEmpty)
        {
            throw new ArgumentException($"Index '{name}' must include at least one column.", nameof(columns));
        }

        return new TableIndexSnapshot(
            name,
            isUnique,
            isPrimaryKey,
            isPlatformAuto,
            string.IsNullOrWhiteSpace(description) ? null : description,
            columnArray,
            metadata);
    }
}

public sealed record TableIndexColumnSnapshot(string Name, int Ordinal, bool IsIncluded, bool IsDescending)
{
    public static TableIndexColumnSnapshot Create(string name, int ordinal, bool isIncluded, bool isDescending)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Index column name must be provided.", nameof(name));
        }

        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return new TableIndexColumnSnapshot(name, ordinal, isIncluded, isDescending);
    }
}

public sealed record TableIndexMetadataSnapshot(
    bool IsDisabled,
    bool IsPadded,
    int? FillFactor,
    bool IgnoreDuplicateKey,
    bool AllowRowLocks,
    bool AllowPageLocks,
    bool StatisticsNoRecompute,
    string? FilterDefinition,
    TableIndexDataSpaceSnapshot? DataSpace,
    ImmutableArray<TableIndexPartitionColumnSnapshot> PartitionColumns,
    ImmutableArray<TableIndexCompressionSnapshot> DataCompression)
{
    public static TableIndexMetadataSnapshot Create(
        bool isDisabled,
        bool isPadded,
        int? fillFactor,
        bool ignoreDuplicateKey,
        bool allowRowLocks,
        bool allowPageLocks,
        bool statisticsNoRecompute,
        string? filterDefinition,
        TableIndexDataSpaceSnapshot? dataSpace,
        IEnumerable<TableIndexPartitionColumnSnapshot>? partitionColumns,
        IEnumerable<TableIndexCompressionSnapshot>? dataCompression)
    {
        var partitions = partitionColumns is null
            ? ImmutableArray<TableIndexPartitionColumnSnapshot>.Empty
            : partitionColumns.ToImmutableArray();
        var compression = dataCompression is null
            ? ImmutableArray<TableIndexCompressionSnapshot>.Empty
            : dataCompression.ToImmutableArray();

        return new TableIndexMetadataSnapshot(
            isDisabled,
            isPadded,
            fillFactor,
            ignoreDuplicateKey,
            allowRowLocks,
            allowPageLocks,
            statisticsNoRecompute,
            string.IsNullOrWhiteSpace(filterDefinition) ? null : filterDefinition,
            dataSpace,
            partitions.IsDefault ? ImmutableArray<TableIndexPartitionColumnSnapshot>.Empty : partitions,
            compression.IsDefault ? ImmutableArray<TableIndexCompressionSnapshot>.Empty : compression);
    }
}

public sealed record TableIndexDataSpaceSnapshot(string Name, string Type)
{
    public static TableIndexDataSpaceSnapshot Create(string name, string type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Data space name must be provided.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Data space type must be provided.", nameof(type));
        }

        return new TableIndexDataSpaceSnapshot(name, type);
    }
}

public sealed record TableIndexPartitionColumnSnapshot(string Name, int Ordinal)
{
    public static TableIndexPartitionColumnSnapshot Create(string name, int ordinal)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Partition column name must be provided.", nameof(name));
        }

        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return new TableIndexPartitionColumnSnapshot(name, ordinal);
    }
}

public sealed record TableIndexCompressionSnapshot(int PartitionNumber, string Compression)
{
    public static TableIndexCompressionSnapshot Create(int partitionNumber, string compression)
    {
        if (partitionNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionNumber));
        }

        if (string.IsNullOrWhiteSpace(compression))
        {
            throw new ArgumentException("Compression value must be provided.", nameof(compression));
        }

        return new TableIndexCompressionSnapshot(partitionNumber, compression);
    }
}

public sealed record TableForeignKeySnapshot(
    string Name,
    ImmutableArray<string> Columns,
    string ReferencedModule,
    string ReferencedTable,
    string ReferencedSchema,
    ImmutableArray<string> ReferencedColumns,
    string ReferencedLogicalTable,
    string DeleteAction,
    bool IsNoCheck)
{
    public static TableForeignKeySnapshot Create(
        string name,
        IEnumerable<string> columns,
        string referencedModule,
        string referencedTable,
        string referencedSchema,
        IEnumerable<string> referencedColumns,
        string referencedLogicalTable,
        string deleteAction,
        bool isNoCheck)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Foreign key name must be provided.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(referencedModule))
        {
            throw new ArgumentException("Referenced module must be provided.", nameof(referencedModule));
        }

        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            throw new ArgumentException("Referenced table must be provided.", nameof(referencedTable));
        }

        if (string.IsNullOrWhiteSpace(referencedSchema))
        {
            throw new ArgumentException("Referenced schema must be provided.", nameof(referencedSchema));
        }

        if (string.IsNullOrWhiteSpace(referencedLogicalTable))
        {
            throw new ArgumentException("Referenced logical table must be provided.", nameof(referencedLogicalTable));
        }

        if (string.IsNullOrWhiteSpace(deleteAction))
        {
            throw new ArgumentException("Delete action must be provided.", nameof(deleteAction));
        }

        var columnArray = columns is null ? ImmutableArray<string>.Empty : columns.ToImmutableArray();
        var referencedArray = referencedColumns is null ? ImmutableArray<string>.Empty : referencedColumns.ToImmutableArray();

        if (columnArray.IsDefaultOrEmpty || referencedArray.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Foreign key must include source and target columns.");
        }

        if (columnArray.Length != referencedArray.Length)
        {
            throw new ArgumentException("Foreign key column counts must match between source and target.");
        }

        return new TableForeignKeySnapshot(
            name,
            columnArray,
            referencedModule,
            referencedTable,
            referencedSchema,
            referencedArray,
            referencedLogicalTable,
            deleteAction,
            isNoCheck);
    }
}

public sealed record TableTriggerSnapshot(
    string Name,
    string Schema,
    string Table,
    bool IsDisabled,
    string Definition)
{
    public static TableTriggerSnapshot Create(string name, string schema, string table, bool isDisabled, string definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Trigger name must be provided.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            throw new ArgumentException("Trigger schema must be provided.", nameof(schema));
        }

        if (string.IsNullOrWhiteSpace(table))
        {
            throw new ArgumentException("Trigger table must be provided.", nameof(table));
        }

        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new ArgumentException("Trigger definition must be provided.", nameof(definition));
        }

        return new TableTriggerSnapshot(name, schema, table, isDisabled, definition);
    }
}

public sealed record TableArtifactProfilingMetadata(long? RowCount)
{
    public static TableArtifactProfilingMetadata Create(long? rowCount)
        => new(rowCount);
}

public sealed record TableArtifactEmissionMetadata(
    string TableName,
    string? ManifestPath,
    ImmutableArray<string> IndexNames,
    ImmutableArray<string> ForeignKeyNames,
    bool IncludesExtendedProperties)
{
    public static TableArtifactEmissionMetadata Create(
        string tableName,
        string? manifestPath,
        IEnumerable<string>? indexNames,
        IEnumerable<string>? foreignKeyNames,
        bool includesExtendedProperties)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name must be provided.", nameof(tableName));
        }

        var indexes = indexNames is null ? ImmutableArray<string>.Empty : indexNames.ToImmutableArray();
        var foreignKeys = foreignKeyNames is null ? ImmutableArray<string>.Empty : foreignKeyNames.ToImmutableArray();

        return new TableArtifactEmissionMetadata(
            tableName,
            string.IsNullOrWhiteSpace(manifestPath) ? null : manifestPath,
            indexes.IsDefault ? ImmutableArray<string>.Empty : indexes,
            foreignKeys.IsDefault ? ImmutableArray<string>.Empty : foreignKeys,
            includesExtendedProperties);
    }
}

public sealed record EntityEmissionSnapshot(
    TableArtifactIdentity Identity,
    ImmutableArray<TableColumnSnapshot> Columns,
    ImmutableArray<TableIndexSnapshot> Indexes,
    ImmutableArray<TableForeignKeySnapshot> ForeignKeys,
    ImmutableArray<TableTriggerSnapshot> Triggers,
    TableArtifactMetadata Metadata)
{
    public static EntityEmissionSnapshot Create(
        TableArtifactIdentity identity,
        IEnumerable<TableColumnSnapshot> columns,
        IEnumerable<TableIndexSnapshot> indexes,
        IEnumerable<TableForeignKeySnapshot> foreignKeys,
        IEnumerable<TableTriggerSnapshot> triggers,
        TableArtifactMetadata metadata)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        return new EntityEmissionSnapshot(
            identity,
            columns is null ? ImmutableArray<TableColumnSnapshot>.Empty : columns.ToImmutableArray(),
            indexes is null ? ImmutableArray<TableIndexSnapshot>.Empty : indexes.ToImmutableArray(),
            foreignKeys is null ? ImmutableArray<TableForeignKeySnapshot>.Empty : foreignKeys.ToImmutableArray(),
            triggers is null ? ImmutableArray<TableTriggerSnapshot>.Empty : triggers.ToImmutableArray(),
            metadata);
    }

    public TableArtifactSnapshot ToArtifactSnapshot(
        TableArtifactProfilingMetadata? profiling = null,
        TableArtifactEmissionMetadata? emission = null)
    {
        return TableArtifactSnapshot.Create(
            Identity,
            Columns,
            Indexes,
            ForeignKeys,
            Triggers,
            Metadata,
            profiling,
            emission);
    }
}

public static class TableArtifactSnapshotFactory
{
    public static TableArtifactSnapshot FromEntityEmission(EntityEmissionSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return snapshot.ToArtifactSnapshot();
    }
}
