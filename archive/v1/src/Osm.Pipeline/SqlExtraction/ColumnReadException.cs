using System;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class ColumnReadException : Exception
{
    public ColumnReadException(
        string columnName,
        int ordinal,
        Type expectedClrType,
        Type? providerFieldType,
        string? resultSetName,
        int? rowIndex,
        Exception? innerException)
        : base(FormatMessage(columnName, ordinal, expectedClrType, providerFieldType, resultSetName, rowIndex), innerException)
    {
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        Ordinal = ordinal;
        ExpectedClrType = expectedClrType ?? throw new ArgumentNullException(nameof(expectedClrType));
        ProviderFieldType = providerFieldType;
        ResultSetName = resultSetName;
        RowIndex = rowIndex;
    }

    public string ColumnName { get; }

    public int Ordinal { get; }

    public Type ExpectedClrType { get; }

    public Type? ProviderFieldType { get; }

    public string? ResultSetName { get; }

    public int? RowIndex { get; }

    public ColumnReadException WithContext(string resultSetName, int rowIndex)
    {
        if (resultSetName is null)
        {
            throw new ArgumentNullException(nameof(resultSetName));
        }

        if (string.Equals(ResultSetName, resultSetName, StringComparison.Ordinal) && RowIndex == rowIndex)
        {
            return this;
        }

        return new ColumnReadException(
            ColumnName,
            Ordinal,
            ExpectedClrType,
            ProviderFieldType,
            resultSetName,
            rowIndex,
            this);
    }

    private static string FormatMessage(
        string columnName,
        int ordinal,
        Type expectedClrType,
        Type? providerFieldType,
        string? resultSetName,
        int? rowIndex)
    {
        var providerDisplay = providerFieldType?.FullName ?? "unknown";
        var expectedDisplay = expectedClrType?.FullName ?? "unknown";

        var message = $"Unable to read column '{columnName}' (ordinal {ordinal}) as type '{expectedDisplay}'. Provider type: {providerDisplay}.";

        if (!string.IsNullOrWhiteSpace(resultSetName) && rowIndex.HasValue)
        {
            message += $" Result set: '{resultSetName}', row index: {rowIndex.Value}.";
        }

        return message;
    }
}
