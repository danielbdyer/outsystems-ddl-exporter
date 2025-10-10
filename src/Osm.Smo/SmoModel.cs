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
    string? Description);

public sealed record SmoIndexDefinition(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsPlatformAuto,
    ImmutableArray<SmoIndexColumnDefinition> Columns);

public sealed record SmoIndexColumnDefinition(string Name, int Ordinal, bool IsIncluded, bool IsDescending);

public sealed record SmoForeignKeyDefinition(
    string Name,
    string Column,
    string ReferencedModule,
    string ReferencedTable,
    string ReferencedSchema,
    string ReferencedColumn,
    string ReferencedLogicalTable,
    ForeignKeyAction DeleteAction);
