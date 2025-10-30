using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.Profiling;

internal interface ITableMetadataLoader
{
    Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(
        DbConnection connection,
        IReadOnlyCollection<TableCoordinate> tables,
        CancellationToken cancellationToken);

    Task<Dictionary<TableCoordinate, long>> LoadRowCountsAsync(
        DbConnection connection,
        IReadOnlyCollection<TableCoordinate> tables,
        CancellationToken cancellationToken);
}

internal interface IProfilingPlanBuilder
{
    Dictionary<TableCoordinate, TableProfilingPlan> BuildPlans(
        IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
        IReadOnlyDictionary<TableCoordinate, long> rowCounts);
}

internal interface IProfilingQueryExecutor
{
    Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken);
}
