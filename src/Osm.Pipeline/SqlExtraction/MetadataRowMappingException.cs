using System;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataRowMappingException : Exception
{
    public MetadataRowMappingException(string resultSetName, int rowIndex, Exception innerException)
        : base(FormatMessage(resultSetName, rowIndex, innerException), innerException)
    {
        ResultSetName = resultSetName ?? throw new ArgumentNullException(nameof(resultSetName));
        RowIndex = rowIndex;

        if (innerException is ColumnReadException columnReadException)
        {
            ColumnName = columnReadException.ColumnName;
            Ordinal = columnReadException.Ordinal;
            ExpectedClrType = columnReadException.ExpectedClrType;
            ProviderFieldType = columnReadException.ProviderFieldType;
        }
    }

    public string ResultSetName { get; }

    public int RowIndex { get; }

    public string? ColumnName { get; }

    public int? Ordinal { get; }

    public Type? ExpectedClrType { get; }

    public Type? ProviderFieldType { get; }

    private static string FormatMessage(string resultSetName, int rowIndex, Exception innerException)
    {
        if (resultSetName is null)
        {
            throw new ArgumentNullException(nameof(resultSetName));
        }

        var message = $"Failed to map row {rowIndex} in result set '{resultSetName}'.";

        if (innerException is ColumnReadException columnReadException)
        {
            var expectedDisplay = columnReadException.ExpectedClrType?.FullName ?? "unknown";
            var providerDisplay = columnReadException.ProviderFieldType?.FullName ?? "unknown";
            message += $" Column '{columnReadException.ColumnName}' (ordinal {columnReadException.Ordinal}) expected type '{expectedDisplay}' but provider type was '{providerDisplay}'.";
        }
        else if (!string.IsNullOrWhiteSpace(innerException.Message))
        {
            message += $" {innerException.Message}";
        }

        return message;
    }
}
