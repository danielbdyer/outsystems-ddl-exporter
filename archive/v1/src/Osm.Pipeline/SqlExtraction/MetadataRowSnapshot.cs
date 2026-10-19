using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataRowSnapshot
{
    public MetadataRowSnapshot(string resultSetName, int rowIndex, IReadOnlyList<MetadataColumnSnapshot> columns)
    {
        ResultSetName = resultSetName ?? throw new ArgumentNullException(nameof(resultSetName));
        RowIndex = rowIndex;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
    }

    public string ResultSetName { get; }

    public int RowIndex { get; }

    public IReadOnlyList<MetadataColumnSnapshot> Columns { get; }

    public MetadataColumnSnapshot? FindColumn(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        return Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStartObject();
        writer.WriteString("resultSet", ResultSetName);
        writer.WriteNumber("rowIndex", RowIndex);
        writer.WriteStartArray("columns");

        foreach (var column in Columns)
        {
            column.WriteTo(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

internal sealed class MetadataColumnSnapshot
{
    private readonly object? _rawValue;
    private string? _serializationError;

    public MetadataColumnSnapshot(int ordinal, string name, string providerType, bool isNull, object? rawValue, string? valuePreview, string? serializationError)
    {
        Ordinal = ordinal;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ProviderType = providerType ?? throw new ArgumentNullException(nameof(providerType));
        IsNull = isNull;
        _rawValue = rawValue;
        ValuePreview = valuePreview;
        _serializationError = serializationError;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string ProviderType { get; }

    public bool IsNull { get; }

    public string? ValuePreview { get; }

    public string? SerializationError => _serializationError;

    public void WriteTo(Utf8JsonWriter writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStartObject();
        writer.WriteNumber("ordinal", Ordinal);
        writer.WriteString("name", Name);
        writer.WriteString("providerType", ProviderType);
        writer.WriteBoolean("isNull", IsNull);

        if (IsNull)
        {
            writer.WriteNull("value");
        }
        else if (!TryWriteRawValue(writer))
        {
            writer.WritePropertyName("value");
            writer.WriteStringValue(ValuePreview);
        }

        if (!string.IsNullOrEmpty(ValuePreview))
        {
            writer.WriteString("textPreview", ValuePreview);
        }

        if (!string.IsNullOrEmpty(SerializationError))
        {
            writer.WriteString("serializationError", SerializationError);
        }

        writer.WriteEndObject();
    }

    private bool TryWriteRawValue(Utf8JsonWriter writer)
    {
        if (_rawValue is null)
        {
            return false;
        }

        try
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, _rawValue, _rawValue.GetType());
            return true;
        }
        catch (NotSupportedException ex)
        {
            _serializationError ??= ex.Message;
            return false;
        }
        catch (JsonException ex)
        {
            _serializationError ??= ex.Message;
            return false;
        }
    }

    public static string FormatValuePreview(object value, int maxLength)
    {
        if (value is null)
        {
            return string.Empty;
        }

        string preview = value switch
        {
            string text => text,
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };

        if (preview.Length <= maxLength)
        {
            return preview;
        }

        return preview.Substring(0, maxLength) + "…";
    }
}
