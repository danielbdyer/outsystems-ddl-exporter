using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

internal abstract class ResultSetProcessor<T> : IResultSetProcessor
{
    private readonly ResultSetDescriptor<T>? _descriptor;

    protected ResultSetProcessor(string name, int order)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(name));
        }

        Name = name;
        Order = order;
    }

    protected ResultSetProcessor(ResultSetDescriptor<T> descriptor)
        : this(descriptor?.Name ?? throw new ArgumentNullException(nameof(descriptor)), descriptor.Order)
    {
        _descriptor = descriptor;
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

    protected virtual ResultSetReader<T> CreateReader(ResultSetProcessingContext context)
    {
        if (_descriptor is not null)
        {
            return _descriptor.CreateReader(context);
        }

        throw new InvalidOperationException($"Derived result set processor '{GetType().Name}' must override CreateReader when not configured with a descriptor.");
    }

    protected virtual void Assign(MetadataAccumulator accumulator, List<T> rows)
    {
        if (_descriptor is not null)
        {
            _descriptor.Assign(accumulator, rows);
            return;
        }

        throw new InvalidOperationException($"Derived result set processor '{GetType().Name}' must override Assign when not configured with a descriptor.");
    }
}
