using System;
using System.Collections.Generic;

namespace Osm.Pipeline.Profiling;

internal sealed class TableKeyComparer : IEqualityComparer<(string Schema, string Table)>
{
    public static TableKeyComparer Instance { get; } = new();

    public bool Equals((string Schema, string Table) x, (string Schema, string Table) y)
    {
        return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string Schema, string Table) obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

internal sealed class ColumnKeyComparer : IEqualityComparer<(string Schema, string Table, string Column)>
{
    public static ColumnKeyComparer Instance { get; } = new();

    public bool Equals((string Schema, string Table, string Column) x, (string Schema, string Table, string Column) y)
    {
        return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string Schema, string Table, string Column) obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Column, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}
