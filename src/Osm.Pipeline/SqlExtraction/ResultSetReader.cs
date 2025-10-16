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

    public async Task<List<T>> ReadAllAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var results = new List<T>();
        var rowIndex = 0;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                results.Add(_rowFactory(new DbRow(reader)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new MetadataRowReadException(rowIndex, ex);
            }

            rowIndex++;
        }

        return results;
    }
}

internal readonly struct DbRow
{
    private readonly DbDataReader _reader;

    public DbRow(DbDataReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public bool IsDBNull(int ordinal) => _reader.IsDBNull(ordinal);

    public Type? TryGetFieldType(int ordinal)
    {
        try
        {
            return _reader.GetFieldType(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool TryGetValue(int ordinal, out object? value)
    {
        try
        {
            value = _reader.GetValue(ordinal);
            if (value is DBNull)
            {
                value = null;
                return false;
            }

            return true;
        }
        catch (IndexOutOfRangeException)
        {
            value = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            value = null;
            return false;
        }
    }

    public int GetInt32(int ordinal) => _reader.GetInt32(ordinal);

    public int? GetInt32OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);

    public int GetInt32Flexible(int ordinal)
    {
        var value = _reader.GetValue(ordinal);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public int GetInt32FlexibleRequired(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(int), TryGetFieldType(ordinal));
        }

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

    public string GetRequiredString(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(string), TryGetFieldType(ordinal));
        }

        return _reader.GetString(ordinal);
    }

    public string? GetStringOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

    public bool GetRequiredBoolean(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(bool), TryGetFieldType(ordinal));
        }

        return _reader.GetBoolean(ordinal);
    }

    public bool GetBoolean(int ordinal) => _reader.GetBoolean(ordinal);

    public bool? GetBooleanOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetBoolean(ordinal);

    public Guid GetRequiredGuid(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(Guid), TryGetFieldType(ordinal));
        }

        return _reader.GetGuid(ordinal);
    }

    public Guid GetGuid(int ordinal) => _reader.GetGuid(ordinal);

    public Guid? GetGuidOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetGuid(ordinal);

    public byte GetRequiredByte(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(byte), TryGetFieldType(ordinal));
        }

        return _reader.GetByte(ordinal);
    }

    public byte GetByte(int ordinal) => _reader.GetByte(ordinal);

    public byte? GetByteOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetByte(ordinal);

    public short GetRequiredInt16(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(short), TryGetFieldType(ordinal));
        }

        return _reader.GetInt16(ordinal);
    }

    public short GetInt16(int ordinal) => _reader.GetInt16(ordinal);

    public short? GetInt16OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt16(ordinal);

    public long GetRequiredInt64(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(long), TryGetFieldType(ordinal));
        }

        return _reader.GetInt64(ordinal);
    }

    public long GetInt64(int ordinal) => _reader.GetInt64(ordinal);

    public long? GetInt64OrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt64(ordinal);

    public decimal GetRequiredDecimal(int ordinal, string columnName)
    {
        if (_reader.IsDBNull(ordinal))
        {
            throw MetadataColumnReadException.Null(columnName, ordinal, typeof(decimal), TryGetFieldType(ordinal));
        }

        return _reader.GetDecimal(ordinal);
    }

    public decimal GetDecimal(int ordinal) => _reader.GetDecimal(ordinal);

    public decimal? GetDecimalOrNull(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetDecimal(ordinal);
}

internal sealed class MetadataRowReadException : Exception
{
    public MetadataRowReadException(int rowIndex, Exception innerException)
        : base($"Failed to materialize row {rowIndex}: {innerException.Message}", innerException)
    {
        RowIndex = rowIndex;
    }

    public int RowIndex { get; }

    public MetadataColumnReadException? ColumnError => InnerException as MetadataColumnReadException;
}

internal sealed class MetadataColumnReadException : Exception
{
    private MetadataColumnReadException(
        string message,
        string columnName,
        int ordinal,
        Type targetType,
        Type? providerType,
        string? rawValueType,
        ColumnFailureKind failureKind,
        Exception? innerException)
        : base(message, innerException)
    {
        ColumnName = columnName;
        Ordinal = ordinal;
        TargetType = targetType;
        ProviderType = providerType;
        RawValueType = rawValueType;
        FailureKind = failureKind;
    }

    public string ColumnName { get; }

    public int Ordinal { get; }

    public Type TargetType { get; }

    public Type? ProviderType { get; }

    public string? RawValueType { get; }

    public ColumnFailureKind FailureKind { get; }

    public static MetadataColumnReadException Null(string columnName, int ordinal, Type targetType, Type? providerType)
    {
        var providerSegment = providerType is null ? string.Empty : $" Provider type: {providerType.FullName}.";
        var message = $"Column '{columnName}' (ordinal {ordinal}) returned NULL while mapping to '{targetType.Name}'.{providerSegment}";
        return new MetadataColumnReadException(message, columnName, ordinal, targetType, providerType, null, ColumnFailureKind.NullValue, null);
    }

    public static MetadataColumnReadException Conversion(
        string columnName,
        int ordinal,
        Type targetType,
        Type? providerType,
        object? rawValue,
        Exception innerException)
    {
        var rawType = rawValue?.GetType();
        var rawSegment = rawType is null ? string.Empty : $" CLR value type: {rawType.FullName}.";
        var providerSegment = providerType is null ? string.Empty : $" Provider type: {providerType.FullName}.";
        var message = $"Column '{columnName}' (ordinal {ordinal}) could not be converted to '{targetType.Name}'.{rawSegment}{providerSegment}";
        return new MetadataColumnReadException(message, columnName, ordinal, targetType, providerType, rawType?.FullName, ColumnFailureKind.Conversion, innerException);
    }

    public static MetadataColumnReadException Unexpected(
        string columnName,
        int ordinal,
        Type targetType,
        Type? providerType,
        Exception innerException)
    {
        var providerSegment = providerType is null ? string.Empty : $" Provider type: {providerType.FullName}.";
        var message = $"Column '{columnName}' (ordinal {ordinal}) threw an unexpected exception while mapping to '{targetType.Name}'.{providerSegment}";
        return new MetadataColumnReadException(message, columnName, ordinal, targetType, providerType, null, ColumnFailureKind.Unexpected, innerException);
    }
}

internal enum ColumnFailureKind
{
    NullValue,
    Conversion,
    Unexpected,
}

internal sealed class MetadataResultSetReadException : Exception
{
    public MetadataResultSetReadException(
        string resultSetName,
        int rowIndex,
        MetadataColumnReadException? columnError,
        Exception innerException)
        : base(BuildMessage(resultSetName, rowIndex, columnError, innerException), innerException)
    {
        ResultSetName = resultSetName;
        RowIndex = rowIndex;
        ColumnError = columnError;
        UserMessage = BuildUserMessage(resultSetName, rowIndex, columnError, innerException);
    }

    public string ResultSetName { get; }

    public int RowIndex { get; }

    public int RowNumber => RowIndex + 1;

    public MetadataColumnReadException? ColumnError { get; }

    public string UserMessage { get; }

    private static string BuildMessage(
        string resultSetName,
        int rowIndex,
        MetadataColumnReadException? columnError,
        Exception innerException)
    {
        if (columnError is not null)
        {
            return $"Result set '{resultSetName}' row {rowIndex + 1} column '{columnError.ColumnName}' failed: {columnError.Message}";
        }

        return $"Result set '{resultSetName}' row {rowIndex + 1} failed: {innerException.Message}";
    }

    private static string BuildUserMessage(
        string resultSetName,
        int rowIndex,
        MetadataColumnReadException? columnError,
        Exception innerException)
    {
        if (columnError is not null)
        {
            return $"Result set '{resultSetName}' row {rowIndex + 1}: {columnError.Message}";
        }

        return $"Result set '{resultSetName}' row {rowIndex + 1} failed to materialize: {innerException.Message}";
    }
}
