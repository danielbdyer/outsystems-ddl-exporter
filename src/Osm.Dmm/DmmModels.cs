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
    string Column,
    string ReferencedSchema,
    string ReferencedTable,
    string ReferencedColumn,
    string DeleteAction,
    bool IsNotTrusted);

public sealed record DmmComparisonResult(
    bool IsMatch,
    IReadOnlyList<string> ModelDifferences,
    IReadOnlyList<string> SsdtDifferences)
{
    public IReadOnlyList<string> Differences => _differences ??= ModelDifferences.Concat(SsdtDifferences).ToArray();

    private IReadOnlyList<string>? _differences;
}
