using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

internal abstract class ResultSetProcessor<T> : IResultSetProcessor
{
    protected ResultSetProcessor(string name, int order)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(name));
        }

        Name = name;
        Order = order;
    }

    public int Order { get; }

    public string Name { get; }

    public async Task<int> ProcessAsync(ResultSetProcessingContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var reader = CreateReader(context);
        var rows = await reader.ReadAllAsync(context.Reader, Name, cancellationToken).ConfigureAwait(false);
        Assign(context.Accumulator, rows);
        return rows.Count;
    }

    protected abstract ResultSetReader<T> CreateReader(ResultSetProcessingContext context);

    protected abstract void Assign(MetadataAccumulator accumulator, List<T> rows);
}
