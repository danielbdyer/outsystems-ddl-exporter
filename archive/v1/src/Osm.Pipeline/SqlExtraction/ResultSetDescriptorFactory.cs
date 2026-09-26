using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Osm.Pipeline.SqlExtraction;

internal static class ResultSetDescriptorFactory
{
    public static ResultSetDescriptor<T> Create<T>(
        string name,
        int order,
        Action<ResultSetDescriptorBuilder<T>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var builder = new ResultSetDescriptorBuilder<T>(name, order);
        configure(builder);
        return builder.Build();
    }
}

internal sealed class ResultSetDescriptorBuilder<T>
{
    private readonly string _name;
    private readonly int _order;
    private readonly List<IResultSetColumn> _columns = new();
    private Func<ResultSetProcessingContext, ResultSetReader<T>>? _readerFactory;
    private Action<MetadataAccumulator, List<T>>? _assign;

    public ResultSetDescriptorBuilder(string name, int order)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(name));
        }

        _name = name;
        _order = order;
    }

    public ResultSetDescriptorBuilder<T> Column(IResultSetColumn column)
    {
        if (column is null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        _columns.Add(column);
        return this;
    }

    public ResultSetDescriptorBuilder<T> Columns(IEnumerable<IResultSetColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        foreach (var column in columns)
        {
            Column(column);
        }

        return this;
    }

    public ResultSetDescriptorBuilder<T> Columns(params IResultSetColumn[] columns)
        => Columns((IEnumerable<IResultSetColumn>)columns);

    public ResultSetDescriptorBuilder<T> Map(Func<DbRow, T> projector)
    {
        if (projector is null)
        {
            throw new ArgumentNullException(nameof(projector));
        }

        var reader = ResultSetReader<T>.Create(projector);
        _readerFactory = _ => reader;
        return this;
    }

    public ResultSetDescriptorBuilder<T> Reader(Func<ResultSetProcessingContext, ResultSetReader<T>> factory)
    {
        _readerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public ResultSetDescriptorBuilder<T> Assign(Action<MetadataAccumulator, List<T>> assign)
    {
        _assign = assign ?? throw new ArgumentNullException(nameof(assign));
        return this;
    }

    internal ResultSetDescriptor<T> Build()
    {
        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("At least one column definition must be provided before building a descriptor.");
        }

        if (_readerFactory is null)
        {
            throw new InvalidOperationException("A row reader must be provided before building a descriptor.");
        }

        if (_assign is null)
        {
            throw new InvalidOperationException("An accumulator assignment must be provided before building a descriptor.");
        }

        return new ResultSetDescriptor<T>(
            _name,
            _order,
            CreateColumnSnapshot(_columns),
            _readerFactory,
            _assign);
    }

    private static IReadOnlyList<IResultSetColumn> CreateColumnSnapshot(IReadOnlyList<IResultSetColumn> columns)
    {
        if (columns.Count == 0)
        {
            throw new InvalidOperationException("At least one column definition must be provided.");
        }

        var buffer = new IResultSetColumn[columns.Count];
        var ordinals = new HashSet<int>();

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index] ?? throw new InvalidOperationException("Column definitions cannot be null.");

            if (!ordinals.Add(column.Ordinal))
            {
                throw new InvalidOperationException($"Duplicate column ordinal detected: {column.Ordinal}.");
            }

            buffer[index] = column;
        }

        return new ReadOnlyCollection<IResultSetColumn>(buffer);
    }
}

internal sealed class ResultSetDescriptor<T>
{
    private readonly Func<ResultSetProcessingContext, ResultSetReader<T>> _readerFactory;
    private readonly Action<MetadataAccumulator, List<T>> _assign;

    internal ResultSetDescriptor(
        string name,
        int order,
        IReadOnlyList<IResultSetColumn> columns,
        Func<ResultSetProcessingContext, ResultSetReader<T>> readerFactory,
        Action<MetadataAccumulator, List<T>> assign)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(name));
        }

        Name = name;
        Order = order;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    public string Name { get; }

    public int Order { get; }

    public IReadOnlyList<IResultSetColumn> Columns { get; }

    internal ResultSetReader<T> CreateReader(ResultSetProcessingContext context)
        => _readerFactory(context ?? throw new ArgumentNullException(nameof(context)));

    internal void Assign(MetadataAccumulator accumulator, List<T> rows)
    {
        if (accumulator is null)
        {
            throw new ArgumentNullException(nameof(accumulator));
        }

        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        _assign(accumulator, rows);
    }
}
