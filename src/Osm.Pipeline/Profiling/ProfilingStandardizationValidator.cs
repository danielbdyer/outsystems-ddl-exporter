using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

/// <summary>
/// Validates profiling results to ensure proper standardization across multi-environment deployments.
/// Checks for data quality issues, consistency problems, and readiness for constraint application.
/// </summary>
public sealed class ProfilingStandardizationValidator
{
    private ProfilingStandardizationValidator()
    {
    }

    public static ProfilingStandardizationValidator Instance { get; } = new();

    /// <summary>
    /// Validates a single environment's profiling snapshot for data quality and consistency.
    /// </summary>
    public Result<ValidationSummary> ValidateSnapshot(
        ProfileSnapshot snapshot,
        string environmentName = "Unknown")
    {
        if (snapshot is null)
        {
            return Result<ValidationSummary>.Failure(
                ValidationError.Create(
                    "profiling.validation.snapshot.null",
                    "Snapshot cannot be null"));
        }

        var issues = new List<ValidationIssue>();

        // Validate columns
        ValidateColumns(snapshot, environmentName, issues);

        // Validate unique candidates
        ValidateUniqueCandidates(snapshot, environmentName, issues);

        // Validate composite unique candidates
        ValidateCompositeUniqueCandidates(snapshot, environmentName, issues);

        // Validate foreign keys
        ValidateForeignKeys(snapshot, environmentName, issues);

        // Check for case inconsistencies
        ValidateCaseConsistency(snapshot, environmentName, issues);

        var summary = new ValidationSummary(
            environmentName,
            snapshot.Columns.Length,
            snapshot.UniqueCandidates.Length + snapshot.CompositeUniqueCandidates.Length,
            snapshot.ForeignKeys.Length,
            issues.ToImmutableArray());

        return Result<ValidationSummary>.Success(summary);
    }

    /// <summary>
    /// Validates profiling results across multiple environments for cross-environment consistency.
    /// </summary>
    public Result<MultiEnvironmentValidationSummary> ValidateMultiEnvironment(
        IEnumerable<ProfilingEnvironmentSnapshot> snapshots)
    {
        if (snapshots is null)
        {
            return Result<MultiEnvironmentValidationSummary>.Failure(
                ValidationError.Create(
                    "profiling.validation.snapshots.null",
                    "Snapshots collection cannot be null"));
        }

        var snapshotList = snapshots.Where(s => s is not null).ToImmutableArray();
        if (snapshotList.IsDefaultOrEmpty || snapshotList.Length == 0)
        {
            return Result<MultiEnvironmentValidationSummary>.Failure(
                ValidationError.Create(
                    "profiling.validation.snapshots.empty",
                    "At least one snapshot must be provided"));
        }

        var issues = new List<ValidationIssue>();
        var environmentSummaries = new List<ValidationSummary>();

        // Validate each environment individually
        foreach (var snapshot in snapshotList)
        {
            var result = ValidateSnapshot(snapshot.Snapshot, snapshot.Name);
            if (result.IsSuccess)
            {
                environmentSummaries.Add(result.Value);
                issues.AddRange(result.Value.Issues);
            }
        }

        // Validate cross-environment consistency
        ValidateSchemaConsistency(snapshotList, issues);
        ValidateDataQualityConsistency(snapshotList, issues);
        ValidateConstraintAgreement(snapshotList, issues);

        var multiSummary = new MultiEnvironmentValidationSummary(
            snapshotList.Length,
            environmentSummaries.ToImmutableArray(),
            issues.ToImmutableArray());

        return Result<MultiEnvironmentValidationSummary>.Success(multiSummary);
    }

    private static void ValidateColumns(
        ProfileSnapshot snapshot,
        string environmentName,
        List<ValidationIssue> issues)
    {
        foreach (var column in snapshot.Columns)
        {
            // Validate NULL count doesn't exceed row count
            if (column.NullCount > column.RowCount)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Error,
                    $"{environmentName}: {column.Schema.Value}.{column.Table.Value}.{column.Column.Value}",
                    $"NULL count ({column.NullCount:N0}) exceeds row count ({column.RowCount:N0})",
                    "profiling.validation.column.nullCount.invalid"));
            }

            // Warn if probe failed or timed out
            if (column.NullCountStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{environmentName}: {column.Schema.Value}.{column.Table.Value}.{column.Column.Value}",
                    $"NULL count probe {column.NullCountStatus.Outcome} (sampled {column.NullCountStatus.SampleSize:N0} rows at {column.NullCountStatus.CapturedAtUtc:yyyy-MM-dd HH:mm:ss})",
                    "profiling.validation.column.probe.failed"));
            }

            // Advisory for physical nullability mismatch with data
            if (!column.IsNullablePhysical && column.NullCount > 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Advisory,
                    $"{environmentName}: {column.Schema.Value}.{column.Table.Value}.{column.Column.Value}",
                    $"Column marked NOT NULL but contains {column.NullCount:N0} NULL values (schema metadata mismatch)",
                    "profiling.validation.column.nullability.mismatch"));
            }
        }
    }

    private static void ValidateUniqueCandidates(
        ProfileSnapshot snapshot,
        string environmentName,
        List<ValidationIssue> issues)
    {
        foreach (var candidate in snapshot.UniqueCandidates)
        {
            if (candidate.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{environmentName}: {candidate.Schema.Value}.{candidate.Table.Value}.{candidate.Column.Value}",
                    $"Unique probe {candidate.ProbeStatus.Outcome} (sampled {candidate.ProbeStatus.SampleSize:N0} rows at {candidate.ProbeStatus.CapturedAtUtc:yyyy-MM-dd HH:mm:ss})",
                    "profiling.validation.unique.probe.failed"));
            }

            if (candidate.HasDuplicate)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Info,
                    $"{environmentName}: {candidate.Schema.Value}.{candidate.Table.Value}.{candidate.Column.Value}",
                    "Column has duplicate values - UNIQUE constraint cannot be applied",
                    "profiling.validation.unique.duplicates"));
            }
        }
    }

    private static void ValidateCompositeUniqueCandidates(
        ProfileSnapshot snapshot,
        string environmentName,
        List<ValidationIssue> issues)
    {
        foreach (var candidate in snapshot.CompositeUniqueCandidates)
        {
            // NOTE: CompositeUniqueCandidateProfile does not have ProbeStatus field
            // (unlike UniqueCandidateProfile), so we can only validate HasDuplicate

            if (candidate.HasDuplicate)
            {
                var columnList = string.Join(", ", candidate.Columns.Select(c => c.Value));
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Info,
                    $"{environmentName}: {candidate.Schema.Value}.{candidate.Table.Value} ({columnList})",
                    "Composite key has duplicate values - UNIQUE constraint cannot be applied",
                    "profiling.validation.compositeUnique.duplicates"));
            }
        }
    }

    private static void ValidateForeignKeys(
        ProfileSnapshot snapshot,
        string environmentName,
        List<ValidationIssue> issues)
    {
        foreach (var fk in snapshot.ForeignKeys)
        {
            if (fk.ProbeStatus.Outcome != ProfilingProbeOutcome.Succeeded)
            {
                var reference = fk.Reference;
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{environmentName}: {reference.FromSchema.Value}.{reference.FromTable.Value}.{reference.FromColumn.Value} -> {reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}",
                    $"Foreign key probe {fk.ProbeStatus.Outcome} (sampled {fk.ProbeStatus.SampleSize:N0} rows at {fk.ProbeStatus.CapturedAtUtc:yyyy-MM-dd HH:mm:ss})",
                    "profiling.validation.foreignKey.probe.failed"));
            }

            if (fk.HasOrphan)
            {
                var reference = fk.Reference;
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{environmentName}: {reference.FromSchema.Value}.{reference.FromTable.Value}.{reference.FromColumn.Value} -> {reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}",
                    "Foreign key has orphaned references - constraint would fail referential integrity",
                    "profiling.validation.foreignKey.orphans"));
            }

            if (fk.IsNoCheck)
            {
                var reference = fk.Reference;
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Advisory,
                    $"{environmentName}: {reference.FromSchema.Value}.{reference.FromTable.Value}.{reference.FromColumn.Value} -> {reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value}",
                    "Foreign key exists but is marked WITH NOCHECK - not enforced",
                    "profiling.validation.foreignKey.nocheck"));
            }
        }
    }

    private static void ValidateCaseConsistency(
        ProfileSnapshot snapshot,
        string environmentName,
        List<ValidationIssue> issues)
    {
        // Check for case variations in table names within the same schema
        var tablesBySchema = snapshot.Columns
            .GroupBy(c => c.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var schemaGroup in tablesBySchema)
        {
            var tablesInSchema = schemaGroup
                .Select(c => c.Table.Value)
                .Distinct(StringComparer.Ordinal)  // Case-sensitive distinct
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var caseVariation in tablesInSchema)
            {
                var variants = string.Join(", ", caseVariation.Select(t => $"'{t}'"));
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Advisory,
                    $"{environmentName}: {schemaGroup.Key}",
                    $"Table name has case variations: {variants}. Standardization recommends consistent casing.",
                    "profiling.validation.case.table.inconsistent"));
            }
        }
    }

    private static void ValidateSchemaConsistency(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        List<ValidationIssue> issues)
    {
        // Find tables that exist in some environments but not others
        var allTables = snapshots
            .SelectMany(env => env.Snapshot.Columns.Select(c => (
                Environment: env.Name,
                Schema: c.Schema.Value,
                Table: c.Table.Value)))
            .GroupBy(tuple => (tuple.Schema, tuple.Table), TableKeyComparer.Instance)
            .ToList();

        foreach (var tableGroup in allTables)
        {
            var environmentsWithTable = tableGroup.Select(t => t.Environment).Distinct().Count();
            if (environmentsWithTable < snapshots.Length)
            {
                var missingFrom = snapshots
                    .Select(s => s.Name)
                    .Except(tableGroup.Select(t => t.Environment))
                    .ToList();

                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{tableGroup.Key.Schema}.{tableGroup.Key.Table}",
                    $"Table missing from {missingFrom.Count} environment(s): {string.Join(", ", missingFrom)}",
                    "profiling.validation.schema.tableMissing"));
            }
        }
    }

    private static void ValidateDataQualityConsistency(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        List<ValidationIssue> issues)
    {
        // Check for significant NULL count variance across environments
        var columnNulls = snapshots
            .SelectMany(env => env.Snapshot.Columns.Select(c => (
                Environment: env.Name,
                Schema: c.Schema.Value,
                Table: c.Table.Value,
                Column: c.Column.Value,
                NullCount: c.NullCount)))
            .GroupBy(tuple => (tuple.Schema, tuple.Table, tuple.Column), ColumnKeyComparer.Instance)
            .Where(g => g.Count() > 1)  // Only check columns in multiple environments
            .ToList();

        foreach (var columnGroup in columnNulls)
        {
            var nullCounts = columnGroup.Select(c => c.NullCount).ToList();
            var min = nullCounts.Min();
            var max = nullCounts.Max();

            // Warn if variance is significant (>10% difference or >1000 rows)
            if (max > min && (max - min > 1000 || (max > 0 && (double)(max - min) / max > 0.1)))
            {
                var envDetails = string.Join(", ", columnGroup.Select(c => $"{c.Environment}: {c.NullCount:N0}"));
                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{columnGroup.Key.Schema}.{columnGroup.Key.Table}.{columnGroup.Key.Column}",
                    $"Significant NULL count variance across environments ({envDetails})",
                    "profiling.validation.dataQuality.nullVariance"));
            }
        }
    }

    private static void ValidateConstraintAgreement(
        ImmutableArray<ProfilingEnvironmentSnapshot> snapshots,
        List<ValidationIssue> issues)
    {
        // Check for unique constraint disagreement across environments
        var uniqueCandidates = snapshots
            .SelectMany(env => env.Snapshot.UniqueCandidates.Select(u => (
                Environment: env.Name,
                Schema: u.Schema.Value,
                Table: u.Table.Value,
                Column: u.Column.Value,
                HasDuplicate: u.HasDuplicate)))
            .GroupBy(tuple => (tuple.Schema, tuple.Table, tuple.Column), ColumnKeyComparer.Instance)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var candidateGroup in uniqueCandidates)
        {
            var hasMixedResults = candidateGroup.Any(c => c.HasDuplicate) && candidateGroup.Any(c => !c.HasDuplicate);
            if (hasMixedResults)
            {
                var safeEnvs = string.Join(", ", candidateGroup.Where(c => !c.HasDuplicate).Select(c => c.Environment));
                var unsafeEnvs = string.Join(", ", candidateGroup.Where(c => c.HasDuplicate).Select(c => c.Environment));

                issues.Add(new ValidationIssue(
                    ValidationIssueSeverity.Warning,
                    $"{candidateGroup.Key.Schema}.{candidateGroup.Key.Table}.{candidateGroup.Key.Column}",
                    $"UNIQUE constraint agreement mismatch - Safe: [{safeEnvs}], Unsafe: [{unsafeEnvs}]",
                    "profiling.validation.constraint.uniqueDisagreement"));
            }
        }
    }
}

public sealed record ValidationSummary(
    string EnvironmentName,
    int ColumnCount,
    int UniqueConstraintCount,
    int ForeignKeyCount,
    ImmutableArray<ValidationIssue> Issues)
{
    public int ErrorCount => Issues.Count(i => i.Severity == ValidationIssueSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationIssueSeverity.Warning);
    public int AdvisoryCount => Issues.Count(i => i.Severity == ValidationIssueSeverity.Advisory);
    public int InfoCount => Issues.Count(i => i.Severity == ValidationIssueSeverity.Info);

    public bool HasErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;

    public string FormatSummary()
    {
        return $"{EnvironmentName}: {ColumnCount} columns, {UniqueConstraintCount} unique constraints, {ForeignKeyCount} foreign keys - " +
               $"{ErrorCount} errors, {WarningCount} warnings, {AdvisoryCount} advisories, {InfoCount} info";
    }
}

public sealed record MultiEnvironmentValidationSummary(
    int EnvironmentCount,
    ImmutableArray<ValidationSummary> EnvironmentSummaries,
    ImmutableArray<ValidationIssue> AllIssues)
{
    public int TotalErrors => AllIssues.Count(i => i.Severity == ValidationIssueSeverity.Error);
    public int TotalWarnings => AllIssues.Count(i => i.Severity == ValidationIssueSeverity.Warning);
    public int TotalAdvisories => AllIssues.Count(i => i.Severity == ValidationIssueSeverity.Advisory);
    public int TotalInfo => AllIssues.Count(i => i.Severity == ValidationIssueSeverity.Info);

    public bool IsHealthy => TotalErrors == 0 && TotalWarnings == 0;
    public bool RequiresRemediation => TotalErrors > 0 || TotalWarnings > 0;

    public string FormatSummary()
    {
        return $"Multi-environment validation: {EnvironmentCount} environments - " +
               $"{TotalErrors} errors, {TotalWarnings} warnings, {TotalAdvisories} advisories, {TotalInfo} info - " +
               $"Status: {(IsHealthy ? "HEALTHY" : RequiresRemediation ? "REQUIRES REMEDIATION" : "REVIEW ADVISORIES")}";
    }
}

public sealed record ValidationIssue(
    ValidationIssueSeverity Severity,
    string Target,
    string Message,
    string Code);

public enum ValidationIssueSeverity
{
    Info,
    Advisory,
    Warning,
    Error
}
