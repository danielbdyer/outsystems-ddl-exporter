using System.Collections.Immutable;
using Microsoft.SqlServer.Management.Smo;

namespace Osm.Smo;

public sealed record SmoModel(ImmutableArray<SmoTableDefinition> Tables)
{
    public static SmoModel Create(ImmutableArray<SmoTableDefinition> tables)
    {
        if (tables.IsDefault)
        {
            tables = ImmutableArray<SmoTableDefinition>.Empty;
        }

        return new SmoModel(tables);
    }

    public IEnumerable<SmoTableDefinition> EnumerateTables() => Tables;
}

public sealed record SmoTableDefinition(
    string Module,
    string OriginalModule,
    string Name,
    string Schema,
    string Catalog,
    string LogicalName,
    string? Description,
    ImmutableArray<SmoColumnDefinition> Columns,
    ImmutableArray<SmoIndexDefinition> Indexes,
    ImmutableArray<SmoForeignKeyDefinition> ForeignKeys);

public sealed record SmoColumnDefinition(
    string Name,
    string LogicalName,
    DataType DataType,
    bool Nullable,
    bool IsIdentity,
    int IdentitySeed,
    int IdentityIncrement,
    bool IsComputed,
    string? ComputedExpression,
    string? DefaultExpression,
    string? Collation,
    string? Description,
    SmoDefaultConstraintDefinition? DefaultConstraint,
    ImmutableArray<SmoCheckConstraintDefinition> CheckConstraints);

public sealed record SmoDefaultConstraintDefinition(string? Name, string Expression, bool IsNotTrusted);

public sealed record SmoCheckConstraintDefinition(string? Name, string Expression, bool IsNotTrusted);

public sealed record SmoIndexDefinition(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsPlatformAuto,
    ImmutableArray<SmoIndexColumnDefinition> Columns,
    SmoIndexMetadata Metadata);

public sealed record SmoIndexColumnDefinition(string Name, int Ordinal, bool IsIncluded, bool IsDescending);

public sealed record SmoIndexMetadata(
    bool IsDisabled,
    bool IsPadded,
    int? FillFactor,
    bool IgnoreDuplicateKey,
    bool AllowRowLocks,
    bool AllowPageLocks,
    bool StatisticsNoRecompute,
    string? FilterDefinition,
    SmoIndexDataSpace? DataSpace,
    ImmutableArray<SmoIndexPartitionColumn> PartitionColumns,
    ImmutableArray<SmoIndexCompressionSetting> DataCompression)
{
    public static readonly SmoIndexMetadata Empty = new(
        IsDisabled: false,
        IsPadded: false,
        FillFactor: null,
        IgnoreDuplicateKey: false,
        AllowRowLocks: true,
        AllowPageLocks: true,
        StatisticsNoRecompute: false,
        FilterDefinition: null,
        DataSpace: null,
        ImmutableArray<SmoIndexPartitionColumn>.Empty,
        ImmutableArray<SmoIndexCompressionSetting>.Empty);
}

public sealed record SmoIndexDataSpace(string Name, string Type);

public sealed record SmoIndexPartitionColumn(string Name, int Ordinal);

public sealed record SmoIndexCompressionSetting(int PartitionNumber, string Compression);

public sealed record SmoForeignKeyDefinition(
    string Name,
    string Column,
    string ReferencedModule,
    string ReferencedTable,
    string ReferencedSchema,
    string ReferencedColumn,
    string ReferencedLogicalTable,
    ForeignKeyAction DeleteAction);
