using System.Collections.Generic;
using System.Linq;

namespace Osm.Dmm;

public sealed record DmmTable(
    string Schema,
    string Name,
    IReadOnlyList<DmmColumn> Columns,
    IReadOnlyList<string> PrimaryKeyColumns,
    IReadOnlyList<DmmIndex> Indexes,
    IReadOnlyList<DmmForeignKey> ForeignKeys,
    string? Description);

public sealed record DmmColumn(
    string Name,
    string DataType,
    bool IsNullable,
    string? DefaultExpression,
    string? Collation,
    string? Description);

public sealed record DmmIndex(
    string Name,
    bool IsUnique,
    IReadOnlyList<DmmIndexColumn> KeyColumns,
    IReadOnlyList<DmmIndexColumn> IncludedColumns,
    string? FilterDefinition,
    bool IsDisabled,
    DmmIndexOptions Options);

public sealed record DmmIndexColumn(string Name, bool IsDescending);

public sealed record DmmIndexOptions(
    bool? PadIndex,
    int? FillFactor,
    bool? IgnoreDuplicateKey,
    bool? AllowRowLocks,
    bool? AllowPageLocks,
    bool? StatisticsNoRecompute);

public sealed record DmmForeignKey(
    string Name,
    IReadOnlyList<DmmForeignKeyColumn> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    string DeleteAction,
    bool IsNotTrusted);

public sealed record DmmForeignKeyColumn(string Column, string ReferencedColumn);

public sealed record DmmComparisonResult(
    bool IsMatch,
    IReadOnlyList<DmmDifference> ModelDifferences,
    IReadOnlyList<DmmDifference> SsdtDifferences)
{
    public IReadOnlyList<DmmDifference> Differences
        => _differences ??= ModelDifferences.Concat(SsdtDifferences).ToArray();

    private IReadOnlyList<DmmDifference>? _differences;
}

public sealed record DmmDifference(
    string Schema,
    string Table,
    string Property,
    string? Column = null,
    string? Index = null,
    string? ForeignKey = null,
    string? Expected = null,
    string? Actual = null,
    string? ArtifactPath = null);
