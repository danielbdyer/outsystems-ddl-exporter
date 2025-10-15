using System;

namespace Osm.Pipeline.SqlExtraction;

public sealed class MetadataResultSetMissingException : InvalidOperationException
{
    public MetadataResultSetMissingException(
        string completedResultSetName,
        int completedRowCount,
        string expectedNextResultSetName,
        int expectedNextResultSetIndex)
        : base($"Metadata script did not return the '{expectedNextResultSetName}' result set (index {expectedNextResultSetIndex}). Last completed '{completedResultSetName}' contained {completedRowCount} row(s).")
    {
        if (string.IsNullOrWhiteSpace(completedResultSetName))
        {
            throw new ArgumentException("Completed result set name must be provided.", nameof(completedResultSetName));
        }

        if (string.IsNullOrWhiteSpace(expectedNextResultSetName))
        {
            throw new ArgumentException("Expected result set name must be provided.", nameof(expectedNextResultSetName));
        }

        CompletedResultSetName = completedResultSetName;
        CompletedRowCount = completedRowCount;
        ExpectedNextResultSetName = expectedNextResultSetName;
        ExpectedNextResultSetIndex = expectedNextResultSetIndex;
    }

    public string CompletedResultSetName { get; }

    public int CompletedRowCount { get; }

    public string ExpectedNextResultSetName { get; }

    public int ExpectedNextResultSetIndex { get; }
}
