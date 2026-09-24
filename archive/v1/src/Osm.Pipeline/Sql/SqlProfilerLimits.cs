using System;

namespace Osm.Pipeline.Sql;

public sealed record SqlProfilerLimits
{
    public SqlProfilerLimits(long? maxRowsPerTable, TimeSpan? tableTimeout)
    {
        if (maxRowsPerTable.HasValue && maxRowsPerTable.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRowsPerTable), "Maximum rows per table must be positive when specified.");
        }

        if (tableTimeout.HasValue && tableTimeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tableTimeout), "Table timeout must be positive when specified.");
        }

        MaxRowsPerTable = maxRowsPerTable;
        TableTimeout = tableTimeout;
    }

    public long? MaxRowsPerTable { get; init; }

    public TimeSpan? TableTimeout { get; init; }

    public static SqlProfilerLimits Default { get; } = new(1_000_000, TimeSpan.FromMinutes(2));
}
