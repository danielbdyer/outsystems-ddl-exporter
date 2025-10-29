using System;

namespace Osm.Pipeline.SqlExtraction;

internal static class Column
{
    public static ColumnDefinition<int> Int32(int ordinal, string name)
        => new(ordinal, name, row => row.GetRequiredInt32Flexible(ordinal, name));

    public static ColumnDefinition<int?> Int32OrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetInt32FlexibleOrNull(ordinal));

    public static ColumnDefinition<string> String(int ordinal, string name)
        => new(ordinal, name, row => row.GetRequiredString(ordinal, name));

    public static ColumnDefinition<string?> StringOrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetStringOrNull(ordinal));

    public static ColumnDefinition<bool> Boolean(int ordinal, string name)
        => new(ordinal, name, row => row.GetRequiredBoolean(ordinal, name));

    public static ColumnDefinition<bool?> BooleanOrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetBooleanOrNull(ordinal));

    public static ColumnDefinition<Guid> Guid(int ordinal, string name)
        => new(ordinal, name, row => row.GetRequiredGuid(ordinal, name));

    public static ColumnDefinition<Guid?> GuidOrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetGuidOrNull(ordinal));

    public static ColumnDefinition<byte> Byte(int ordinal, string name)
        => new(ordinal, name, row => row.GetByte(ordinal));

    public static ColumnDefinition<byte?> ByteOrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetByteOrNull(ordinal));

    public static ColumnDefinition<short> Int16(int ordinal, string name)
        => new(ordinal, name, row => row.GetInt16(ordinal));

    public static ColumnDefinition<short?> Int16OrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetInt16OrNull(ordinal));

    public static ColumnDefinition<long> Int64(int ordinal, string name)
        => new(ordinal, name, row => row.GetInt64(ordinal));

    public static ColumnDefinition<long?> Int64OrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetInt64OrNull(ordinal));

    public static ColumnDefinition<decimal> Decimal(int ordinal, string name)
        => new(ordinal, name, row => row.GetDecimal(ordinal));

    public static ColumnDefinition<decimal?> DecimalOrNull(int ordinal, string name)
        => new(ordinal, name, row => row.GetDecimalOrNull(ordinal));
}

internal sealed class ColumnDefinition<T>
{
    private readonly Func<DbRow, T> _reader;

    public ColumnDefinition(int ordinal, string name, Func<DbRow, T> reader)
    {
        Ordinal = ordinal;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public int Ordinal { get; }

    public string Name { get; }

    public T Read(DbRow row)
    {
        try
        {
            return _reader(row);
        }
        catch (ColumnReadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Type? providerType = null;

            try
            {
                providerType = row.GetFieldType(Ordinal);
            }
            catch
            {
                // Preserve the original exception when the provider type cannot be determined.
            }

            throw new ColumnReadException(Name, Ordinal, typeof(T), providerType, row.ResultSetName, row.RowIndex, ex);
        }
    }
}
