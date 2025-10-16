using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Profiling;

internal interface ITableMetadataLoader
{
    Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken);

    Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(
        DbConnection connection,
        IReadOnlyCollection<(string Schema, string Table)> tables,
        CancellationToken cancellationToken);
}

internal interface IProfilingPlanBuilder
{
    Dictionary<(string Schema, string Table), TableProfilingPlan> BuildPlans(
        IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
        IReadOnlyDictionary<(string Schema, string Table), long> rowCounts);
}

internal interface IProfilingQueryExecutor
{
    Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken);
}
