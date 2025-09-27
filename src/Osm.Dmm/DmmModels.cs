using System.Collections.Generic;

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

public sealed record DmmComparisonResult(bool IsMatch, IReadOnlyList<string> Differences);
