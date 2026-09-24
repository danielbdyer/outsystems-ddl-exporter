using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Osm.Domain.Profiling;

/// <summary>
/// Represents a sample of orphaned foreign-key rows along with the referencing value.
/// </summary>
public sealed record ForeignKeyOrphanSample(
    ImmutableArray<string> PrimaryKeyColumns,
    string ForeignKeyColumn,
    ImmutableArray<ForeignKeyOrphanIdentifier> SampleRows,
    long TotalOrphans,
    bool IsTruncated)
{
    public static ForeignKeyOrphanSample Empty(string foreignKeyColumn) => new(
        ImmutableArray<string>.Empty,
        foreignKeyColumn,
        ImmutableArray<ForeignKeyOrphanIdentifier>.Empty,
        0,
        false);

    public static ForeignKeyOrphanSample Create(
        ImmutableArray<string> primaryKeyColumns,
        string foreignKeyColumn,
        ImmutableArray<ForeignKeyOrphanIdentifier> sampleRows,
        long totalOrphans)
    {
        if (totalOrphans < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalOrphans), "Total orphan count cannot be negative.");
        }

        var isTruncated = sampleRows.Length < totalOrphans;
        return new ForeignKeyOrphanSample(primaryKeyColumns, foreignKeyColumn, sampleRows, totalOrphans, isTruncated);
    }
}

/// <summary>
/// Represents a single orphaned foreign-key row, including the source primary key values and the orphaned reference value.
/// </summary>
public sealed record ForeignKeyOrphanIdentifier(
    ImmutableArray<object?> PrimaryKeyValues,
    object? ForeignKeyValue)
{
    public override string ToString()
    {
        var pk = PrimaryKeyValues.IsDefaultOrEmpty
            ? "(no PK)"
            : $"({string.Join(", ", PrimaryKeyValues.Select(FormatValue))})";

        return string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", pk, FormatValue(ForeignKeyValue));
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "NULL"
        };
    }
}
