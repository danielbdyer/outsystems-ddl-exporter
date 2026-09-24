using System;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataRowMappingException : Exception
{
    public MetadataRowMappingException(
        string resultSetName,
        int rowIndex,
        Exception innerException,
        MetadataRowSnapshot? rowSnapshot)
        : base(FormatMessage(resultSetName, rowIndex, innerException, rowSnapshot), innerException)
    {
        ResultSetName = resultSetName ?? throw new ArgumentNullException(nameof(resultSetName));
        RowIndex = rowIndex;
        RowSnapshot = rowSnapshot;

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

    public MetadataRowSnapshot? RowSnapshot { get; }

    public MetadataColumnSnapshot? HighlightedColumn
        => RowSnapshot?.FindColumn(ColumnName);

    private static string FormatMessage(
        string resultSetName,
        int rowIndex,
        Exception innerException,
        MetadataRowSnapshot? rowSnapshot)
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

            var rootCause = GetInnermostMessage(columnReadException.InnerException);
            if (!string.IsNullOrWhiteSpace(rootCause))
            {
                message += $" Root cause: {rootCause}";
            }

            if (rowSnapshot is not null)
            {
                var column = rowSnapshot.FindColumn(columnReadException.ColumnName);
                if (column is not null)
                {
                    var preview = column.IsNull ? "<NULL>" : column.ValuePreview ?? "<unavailable>";
                    message += $" Column snapshot preview: {preview}.";
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(innerException.Message))
        {
            message += $" {innerException.Message}";
        }

        return message;
    }

    private static string? GetInnermostMessage(Exception? exception)
    {
        Exception? current = exception;
        string? message = null;

        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                message = current.Message;
            }

            current = current.InnerException;
        }

        return message;
    }
}
