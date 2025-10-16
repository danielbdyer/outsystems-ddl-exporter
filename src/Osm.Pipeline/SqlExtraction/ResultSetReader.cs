using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

/// <summary>
/// Provides a reusable way to materialize strongly typed rows from a <see cref="DbDataReader"/>.
/// </summary>
/// <typeparam name="T">The row type to materialize.</typeparam>
internal sealed class ResultSetReader<T>
{
    private readonly Func<Row, T> _rowFactory;

    private ResultSetReader(Func<Row, T> rowFactory)
    {
        _rowFactory = rowFactory ?? throw new ArgumentNullException(nameof(rowFactory));
    }

    public static ResultSetReader<T> Create(Func<Row, T> rowFactory)
        => new(rowFactory);

    public async Task<List<T>> ReadAllAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var results = new List<T>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(_rowFactory(new Row(reader)));
        }

        return results;
    }

    internal readonly struct Row
    {
        private readonly DbDataReader _reader;

        public Row(DbDataReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public int GetInt32(int ordinal) => _reader.GetInt32(ordinal);

        public int? GetInt32OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);

        public string GetString(int ordinal) => _reader.GetString(ordinal);

        public string? GetStringOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

        public bool GetBoolean(int ordinal) => _reader.GetBoolean(ordinal);

        public bool? GetBooleanOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetBoolean(ordinal);

        public Guid GetGuid(int ordinal) => _reader.GetGuid(ordinal);

        public Guid? GetGuidOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetGuid(ordinal);
    }
}
