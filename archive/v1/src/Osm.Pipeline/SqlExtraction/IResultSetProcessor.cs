using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

internal interface IResultSetProcessor
{
    int Order { get; }

    string Name { get; }

    Task<int> ProcessAsync(ResultSetProcessingContext context, CancellationToken cancellationToken);
}

internal sealed class ResultSetProcessingContext
{
    public ResultSetProcessingContext(DbDataReader reader, MetadataAccumulator accumulator)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        Accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
    }

    public DbDataReader Reader { get; }

    public MetadataAccumulator Accumulator { get; }
}
