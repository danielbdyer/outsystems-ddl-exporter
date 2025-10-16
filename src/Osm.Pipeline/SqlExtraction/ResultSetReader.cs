using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.SqlExtraction;

/// <summary>
/// Provides a reusable way to materialize strongly typed rows from a <see cref="DbDataReader"/>.
/// </summary>
/// <typeparam name="T">The row type to materialize.</typeparam>
internal sealed class ResultSetReader<T>
{
    private readonly Func<DbRow, T> _rowFactory;

    private ResultSetReader(Func<DbRow, T> rowFactory)
    {
        _rowFactory = rowFactory ?? throw new ArgumentNullException(nameof(rowFactory));
    }

    public static ResultSetReader<T> Create(Func<DbRow, T> rowFactory)
        => new(rowFactory);

    public async Task<List<T>> ReadAllAsync(DbDataReader reader, string resultSetName, CancellationToken cancellationToken)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (string.IsNullOrWhiteSpace(resultSetName))
        {
            throw new ArgumentException("Result set name must be provided.", nameof(resultSetName));
        }

        var results = new List<T>();
        var rowIndex = 0;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                results.Add(_rowFactory(new DbRow(reader, resultSetName, rowIndex)));
            }
            catch (Exception ex)
            {
                var failure = ex is ColumnReadException columnReadException
                    ? columnReadException.WithContext(resultSetName, rowIndex)
                    : ex;

                throw new MetadataRowMappingException(resultSetName, rowIndex, failure);
            }

            rowIndex++;
        }

        return results;
    }
}

internal readonly struct DbRow
{
    private readonly DbDataReader _reader;
    private readonly string _resultSetName;
    private readonly int _rowIndex;

    public DbRow(DbDataReader reader, string resultSetName, int rowIndex)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _resultSetName = resultSetName ?? throw new ArgumentNullException(nameof(resultSetName));
        _rowIndex = rowIndex;
    }

    public string ResultSetName => _resultSetName;

    public int RowIndex => _rowIndex;

    public int GetInt32(int ordinal) => _reader.GetInt32(ordinal);

    public int? GetInt32OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);

    public int GetRequiredInt32Flexible(int ordinal, string columnName)
    {
        EnsureNotDbNull(ordinal, columnName);
        return GetInt32Flexible(ordinal);
    }

    public int GetInt32Flexible(int ordinal)
    {
        var value = _reader.GetValue(ordinal);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public int? GetInt32FlexibleOrNull(int ordinal)
    {
        if (_reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = _reader.GetValue(ordinal);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public string GetString(int ordinal) => _reader.GetString(ordinal);

    public string GetRequiredString(int ordinal, string columnName)
    {
        EnsureNotDbNull(ordinal, columnName);
        return _reader.GetString(ordinal);
    }

    public string? GetStringOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

    public bool GetBoolean(int ordinal) => _reader.GetBoolean(ordinal);

    public bool GetRequiredBoolean(int ordinal, string columnName)
    {
        EnsureNotDbNull(ordinal, columnName);
        return _reader.GetBoolean(ordinal);
    }

    public bool? GetBooleanOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetBoolean(ordinal);

    public Guid GetGuid(int ordinal) => _reader.GetGuid(ordinal);

    public Guid GetRequiredGuid(int ordinal, string columnName)
    {
        EnsureNotDbNull(ordinal, columnName);
        return _reader.GetGuid(ordinal);
    }

    public Guid? GetGuidOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetGuid(ordinal);

    public byte GetByte(int ordinal) => _reader.GetByte(ordinal);

    public byte? GetByteOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetByte(ordinal);

    public short GetInt16(int ordinal) => _reader.GetInt16(ordinal);

    public short? GetInt16OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt16(ordinal);

    public long GetInt64(int ordinal) => _reader.GetInt64(ordinal);

    public long? GetInt64OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt64(ordinal);

    public decimal GetDecimal(int ordinal) => _reader.GetDecimal(ordinal);

    public decimal? GetDecimalOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetDecimal(ordinal);

    public Type GetFieldType(int ordinal) => _reader.GetFieldType(ordinal);

    private void EnsureNotDbNull(int ordinal, string columnName)
    {
        if (columnName is null)
        {
            throw new ArgumentNullException(nameof(columnName));
        }

        if (_reader.IsDBNull(ordinal))
        {
            throw new InvalidOperationException(
                $"Column '{columnName}' (ordinal {ordinal}) contained NULL but a non-null value was required.");
        }
    }
}
