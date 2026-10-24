using System.Collections.Immutable;
using System.Linq;

namespace Osm.Domain.Profiling;

/// <summary>
/// Represents a sample of rows containing NULL values in a column,
/// identified by their primary key values.
/// </summary>
public sealed record NullRowSample(
    ImmutableArray<string> PrimaryKeyColumns,
    ImmutableArray<NullRowIdentifier> SampleRows,
    long TotalNullRows,
    bool IsTruncated)
{
    public static NullRowSample Empty { get; } = new(
        ImmutableArray<string>.Empty,
        ImmutableArray<NullRowIdentifier>.Empty,
        0,
        false);

    public static NullRowSample Create(
        ImmutableArray<string> primaryKeyColumns,
        ImmutableArray<NullRowIdentifier> sampleRows,
        long totalNullRows)
    {
        var isTruncated = sampleRows.Length < totalNullRows;
        return new NullRowSample(primaryKeyColumns, sampleRows, totalNullRows, isTruncated);
    }
}

/// <summary>
/// Represents a single row with a NULL value, identified by its primary key value(s).
/// </summary>
public sealed record NullRowIdentifier(ImmutableArray<object?> PrimaryKeyValues)
{
    public override string ToString()
    {
        if (PrimaryKeyValues.IsDefaultOrEmpty)
        {
            return "(no PK)";
        }

        return $"({string.Join(", ", PrimaryKeyValues.Select(FormatValue))})";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            _ => value.ToString() ?? "NULL"
        };
    }
}
