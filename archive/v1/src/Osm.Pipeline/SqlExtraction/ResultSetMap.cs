using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ResultSetMap<T>
{
    private readonly ResultSetReader<T> _reader;
    private readonly Action<MetadataAccumulator, List<T>> _assign;

    private ResultSetMap(
        string name,
        int order,
        IReadOnlyList<IResultSetColumn> columns,
        Func<DbRow, T> projector,
        Action<MetadataAccumulator, List<T>> assign)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(name));
        }

        Name = name;
        Order = order;
        Columns = CreateColumnSnapshot(columns);
        _reader = ResultSetReader<T>.Create(projector ?? throw new ArgumentNullException(nameof(projector)));
        _assign = assign ?? throw new ArgumentNullException(nameof(assign));
    }

    public string Name { get; }

    public int Order { get; }

    public IReadOnlyList<IResultSetColumn> Columns { get; }

    internal ResultSetReader<T> Reader => _reader;

    public static ResultSetMap<T> Create(
        string name,
        int order,
        IReadOnlyList<IResultSetColumn> columns,
        Func<DbRow, T> projector,
        Action<MetadataAccumulator, List<T>> assign)
        => new(name, order, columns, projector, assign);

    internal Task<List<T>> ReadAllAsync(DbDataReader reader, CancellationToken cancellationToken)
        => _reader.ReadAllAsync(reader, Name, cancellationToken);

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

    private static IReadOnlyList<IResultSetColumn> CreateColumnSnapshot(IReadOnlyList<IResultSetColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column definition must be provided.", nameof(columns));
        }

        var buffer = new IResultSetColumn[columns.Count];
        var ordinals = new HashSet<int>();

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index] ?? throw new ArgumentException("Column definitions cannot be null.", nameof(columns));

            if (!ordinals.Add(column.Ordinal))
            {
                throw new ArgumentException($"Duplicate column ordinal detected: {column.Ordinal}.", nameof(columns));
            }

            buffer[index] = column;
        }

        return new ReadOnlyCollection<IResultSetColumn>(buffer);
    }
}
