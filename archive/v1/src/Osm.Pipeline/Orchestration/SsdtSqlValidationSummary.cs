using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Pipeline.Orchestration;

public sealed record SsdtSqlValidationSummary(
    int TotalFiles,
    int FilesWithErrors,
    int ErrorCount,
    ImmutableArray<SsdtSqlValidationIssue> Issues)
{
    public static SsdtSqlValidationSummary Empty { get; } = new(
        TotalFiles: 0,
        FilesWithErrors: 0,
        ErrorCount: 0,
        Issues: ImmutableArray<SsdtSqlValidationIssue>.Empty);

    public static SsdtSqlValidationSummary Create(
        int totalFiles,
        IReadOnlyList<SsdtSqlValidationIssue> issues)
    {
        if (totalFiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalFiles));
        }

        if (issues is null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        var materialized = issues.ToImmutableArray();
        if (materialized.IsDefault)
        {
            materialized = ImmutableArray<SsdtSqlValidationIssue>.Empty;
        }

        var filesWithErrors = materialized.Length;
        var errorCount = materialized.Sum(static issue => issue.Errors.Length);
        return new SsdtSqlValidationSummary(totalFiles, filesWithErrors, errorCount, materialized);
    }
}

public sealed record SsdtSqlValidationIssue(
    string Path,
    ImmutableArray<SsdtSqlValidationError> Errors)
{
    public static SsdtSqlValidationIssue Create(
        string path,
        IEnumerable<SsdtSqlValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File path must be provided.", nameof(path));
        }

        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        var materialized = errors.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one validation error must be provided.", nameof(errors));
        }

        return new SsdtSqlValidationIssue(path, materialized);
    }
}

public sealed record SsdtSqlValidationError(
    int Number,
    int State,
    int Severity,
    int Line,
    int Column,
    string Message)
{
    public static SsdtSqlValidationError Create(
        int number,
        int state,
        int severity,
        int line,
        int column,
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message must be provided.", nameof(message));
        }

        return new SsdtSqlValidationError(number, state, severity, line, column, message);
    }
}
