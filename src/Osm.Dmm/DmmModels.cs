using System.Collections.Generic;
using System.Linq;

namespace Osm.Dmm;

public sealed record DmmTable(
    string Schema,
    string Name,
    IReadOnlyList<DmmColumn> Columns,
    IReadOnlyList<string> PrimaryKeyColumns);

public sealed record DmmColumn(
    string Name,
    string DataType,
    bool IsNullable);

public sealed record DmmComparisonResult(
    bool IsMatch,
    IReadOnlyList<string> ModelDifferences,
    IReadOnlyList<string> SsdtDifferences)
{
    public IReadOnlyList<string> Differences => _differences ??= ModelDifferences.Concat(SsdtDifferences).ToArray();

    private IReadOnlyList<string>? _differences;
}
