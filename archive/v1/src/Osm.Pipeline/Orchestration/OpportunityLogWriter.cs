using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;
using TighteningRationales = Osm.Validation.Tightening.TighteningRationales;

namespace Osm.Pipeline.Orchestration;

public sealed record OpportunityArtifacts(
    string ReportPath,
    string ValidationsPath,
    string SafeScriptPath,
    string SafeScript,
    string RemediationScriptPath,
    string RemediationScript);

public sealed class OpportunityLogWriter : IOpportunityLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IFileSystem _fileSystem;

    public OpportunityLogWriter()
        : this(new FileSystem())
    {
    }

    public OpportunityLogWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<Result<OpportunityArtifacts>> WriteAsync(
        string outputDirectory,
        OpportunitiesReport report,
        ValidationReport validations,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (validations is null)
        {
            throw new ArgumentNullException(nameof(validations));
        }

        try
        {
            _fileSystem.Directory.CreateDirectory(outputDirectory);
            var suggestionsDirectory = _fileSystem.Path.Combine(outputDirectory, "suggestions");
            _fileSystem.Directory.CreateDirectory(suggestionsDirectory);

            var reportPath = _fileSystem.Path.Combine(outputDirectory, "opportunities.json");
            var validationsPath = _fileSystem.Path.Combine(outputDirectory, "validations.json");
            var safePath = _fileSystem.Path.Combine(suggestionsDirectory, "safe-to-apply.sql");
            var remediationPath = _fileSystem.Path.Combine(suggestionsDirectory, "needs-remediation.sql");

            var json = JsonSerializer.Serialize(report, JsonOptions);
            await _fileSystem.File.WriteAllTextAsync(reportPath, json, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            var normalizedValidations = NormalizeValidations(validations);
            var validationsJson = JsonSerializer.Serialize(normalizedValidations, JsonOptions);
            await _fileSystem.File.WriteAllTextAsync(validationsPath, validationsJson, Utf8NoBom, cancellationToken)
                .ConfigureAwait(false);

            var safeOpportunities = report.Opportunities.Where(o => o.Disposition == OpportunityDisposition.ReadyToApply).ToList();
            var safeScript = BuildSql(safeOpportunities, report, "Safe to Apply");
            await _fileSystem.File.WriteAllTextAsync(safePath, safeScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            var remediationOpportunities = report.Opportunities.Where(o => o.Disposition == OpportunityDisposition.NeedsRemediation).ToList();
            var remediationScript = BuildSql(remediationOpportunities, report, "Needs Remediation");
            await _fileSystem.File.WriteAllTextAsync(remediationPath, remediationScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            return Result<OpportunityArtifacts>.Success(new OpportunityArtifacts(reportPath, validationsPath, safePath, safeScript, remediationPath, remediationScript));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<OpportunityArtifacts>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.output.permissionDenied",
                $"Failed to write tightening opportunities to '{outputDirectory}': {ex.Message}"));
        }
    }

    private static string BuildSql(IReadOnlyList<Opportunity> opportunities, OpportunitiesReport report, string scriptCategory)
    {
        var builder = new StringBuilder();

        // Header with categorization summary
        builder.AppendLine("-- ============================================================================");
        builder.AppendLine($"-- OutSystems DDL Exporter - {scriptCategory} Opportunities");
        builder.AppendLine("-- ============================================================================");
        builder.AppendLine($"-- Generated: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine("--");
        builder.AppendLine("-- SUMMARY:");
        builder.AppendLine($"--   Total Opportunities: {report.TotalCount}");

        if (report.ContradictionCount > 0)
        {
            builder.AppendLine($"--   ⚠️  Contradictions: {report.ContradictionCount} (Data violates model expectations - REQUIRES MANUAL REMEDIATION)");
        }

        if (report.RecommendationCount > 0)
        {
            builder.AppendLine($"--   Recommendations: {report.RecommendationCount} (New constraints that could be safely applied)");
        }

        builder.AppendLine("--");
        builder.AppendLine($"-- This script contains {opportunities.Count} {scriptCategory.ToLower(System.Globalization.CultureInfo.InvariantCulture)} opportunities.");
        builder.AppendLine("--");

        var contradictions = opportunities.Where(o => o.IsContradiction).ToList();
        if (contradictions.Count > 0)
        {
            builder.AppendLine("-- ⚠️  WARNING: This script contains DATA CONTRADICTIONS that require manual remediation.");
            builder.AppendLine("--              Do NOT execute these statements until the underlying data issues are resolved.");
            builder.AppendLine("--");
        }

        builder.AppendLine("-- IMPORTANT: Never modify OutSystems model JSON files directly.");
        builder.AppendLine("--            These scripts are suggestions only and will not auto-execute.");
        builder.AppendLine("-- ============================================================================");
        builder.AppendLine();

        if (opportunities.Count == 0)
        {
            builder.AppendLine("-- No opportunities in this category.");
            return builder.ToString();
        }

        // Group by category (Contradiction first, then Recommendation, then Validation)
        // and then by type within each category
        var byCategory = opportunities
            .GroupBy(o => o.Category)
            .OrderBy(g => GetCategoryPriority(g.Key));

        foreach (var categoryGroup in byCategory)
        {
            builder.AppendLine($"-- ========== {categoryGroup.Key.ToString().ToUpperInvariant()} ==========");
            builder.AppendLine();

            AppendCategoryDescription(builder, categoryGroup.Key);
            builder.AppendLine();

            // Group by type within each category
            var byType = categoryGroup.GroupBy(o => o.Type).OrderBy(g => g.Key);

            foreach (var typeGroup in byType)
            {
                builder.AppendLine($"-- ---------- {typeGroup.Key} ----------");
                builder.AppendLine();

                AppendTypeDescription(builder, typeGroup.Key, categoryGroup.Key);
                builder.AppendLine();

                foreach (var opportunity in typeGroup)
                {
                    builder.Append("-- ");
                    builder.Append(opportunity.Type);
                    builder.Append(' ');
                    builder.Append(opportunity.Schema);
                    builder.Append('.');
                    builder.Append(opportunity.Table);
                    builder.Append(" (");
                    builder.Append(opportunity.ConstraintName);
                    builder.Append(") Category=");
                    builder.Append(opportunity.Category);
                    builder.Append(" Risk=");
                    builder.AppendLine(opportunity.Risk.Label);

                    builder.Append("-- Summary: ");
                    builder.AppendLine(opportunity.Summary);

                    AppendForeignKeyGuidance(builder, opportunity);

                    if (!opportunity.Rationales.IsDefaultOrEmpty)
                    {
                        foreach (var rationale in opportunity.Rationales)
                        {
                            builder.Append("-- Rationale: ");
                            builder.AppendLine(rationale);
                        }
                    }

                    if (!opportunity.Evidence.IsDefaultOrEmpty)
                    {
                        foreach (var evidence in opportunity.Evidence)
                        {
                            builder.Append("-- Evidence: ");
                            builder.AppendLine(evidence);
                        }
                    }

                    if (opportunity.HasStatements)
                    {
                        foreach (var statement in opportunity.Statements)
                        {
                            builder.AppendLine(statement);
                        }
                    }
                    else
                    {
                        builder.AppendLine("-- No automated statement available.");
                    }

                    builder.AppendLine("GO");
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendForeignKeyGuidance(StringBuilder builder, Opportunity opportunity)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (opportunity is null)
        {
            throw new ArgumentNullException(nameof(opportunity));
        }

        if (opportunity.Type != OpportunityType.ForeignKey)
        {
            return;
        }

        if (opportunity.Columns.IsDefaultOrEmpty)
        {
            return;
        }

        var column = opportunity.Columns[0];
        var hasOrphans = column.HasOrphans ?? false;
        var hasConstraint = column.HasDatabaseConstraint;
        var hasNoCheckEvidence = opportunity.Evidence.Any(static evidence =>
            evidence.StartsWith("ConstraintTrust=", StringComparison.Ordinal)
            && evidence.Contains("NO CHECK", StringComparison.Ordinal));
        var requiresNoCheck = opportunity.Rationales.Contains(
            TighteningRationales.ForeignKeyNoCheckRecommended, StringComparer.Ordinal);

        if (requiresNoCheck)
        {
            builder.AppendLine("-- Model expects this relationship; constraint will be emitted WITH NOCHECK until remediation completes.");
        }

        if (opportunity.Category == OpportunityCategory.Contradiction && hasOrphans)
        {
            if (hasConstraint is true)
            {
                var message = hasNoCheckEvidence
                    ? "Existing constraint is marked WITH NOCHECK so SQL Server is not validating existing data."
                    : "Existing constraint should block these rows—verify it has not been disabled or replicated without checks.";
                builder.Append("-- Foreign key state: ");
                builder.AppendLine(message);
            }
            else if (hasConstraint is false)
            {
                builder.AppendLine("-- Foreign key state: No database constraint currently enforces this relationship.");
            }

            builder.AppendLine("-- Remediation steps:");
            builder.AppendLine("--   1. Use the CLI orphan samples to query the child rows and confirm they lack parents.");
            builder.AppendLine("--   2. Repair or backfill the offending child rows so every key maps to a valid parent.");
            builder.AppendLine("--   3. Re-run build-ssdt; once orphan counts reach zero the FK moves into the safe scripts.");

            if (hasConstraint is true)
            {
                var trustMessage = hasNoCheckEvidence
                    ? "--   4. Run ALTER TABLE ... WITH CHECK CHECK CONSTRAINT after cleanup to re-trust the FK."
                    : "--   4. Re-enable the constraint (WITH CHECK) after remediation to keep enforcement active.";
                builder.AppendLine(trustMessage);
            }

            if (requiresNoCheck)
            {
                builder.AppendLine("--   5. Keep the generated WITH NOCHECK constraint only as a stop-gap; remove the waiver once data is clean.");
            }

            builder.AppendLine("--");
        }
        else if (opportunity.Category == OpportunityCategory.Recommendation && hasConstraint is false)
        {
            builder.AppendLine("-- Evidence: Profiler observed zero orphans and there is no existing database constraint.");
            builder.AppendLine("-- Action: Apply the generated WITH CHECK statements to formalize referential integrity.");
            builder.AppendLine("--");
        }
    }

    private static int GetCategoryPriority(OpportunityCategory category)
    {
        return category switch
        {
            OpportunityCategory.Contradiction => 1,  // Highest priority - requires manual remediation
            OpportunityCategory.Recommendation => 2, // Medium priority - safe to apply
            OpportunityCategory.Validation => 3,     // Lowest priority - informational
            _ => 99                                  // Unknown goes last
        };
    }

    private static void AppendCategoryDescription(StringBuilder builder, OpportunityCategory category)
    {
        var description = category switch
        {
            OpportunityCategory.Contradiction =>
                "-- ⚠️  CONTRADICTIONS - MANUAL DATA REMEDIATION REQUIRED\n" +
                "--\n" +
                "-- These opportunities represent the MOST SEVERE issues where actual data in the database\n" +
                "-- contradicts what the OutSystems model expects. Examples include:\n" +
                "--   • NULL values in columns marked as Mandatory in the model\n" +
                "--   • Duplicate values in columns that should be unique\n" +
                "--   • Orphaned foreign key references (child records pointing to non-existent parents)\n" +
                "--\n" +
                "-- ACTION REQUIRED: You must manually clean the data BEFORE applying these constraints.\n" +
                "-- Attempting to add these constraints without fixing the data will result in SQL errors.\n" +
                "-- Review the evidence and remediation suggestions for each opportunity below.",

            OpportunityCategory.Recommendation =>
                "-- RECOMMENDATIONS - SAFE TO APPLY\n" +
                "--\n" +
                "-- These opportunities represent NEW constraints that could be safely added to your database.\n" +
                "-- Profiling has confirmed that the existing data already satisfies these constraints.\n" +
                "-- Examples include:\n" +
                "--   • Adding NOT NULL to columns that have no null values\n" +
                "--   • Adding UNIQUE constraints where data has no duplicates\n" +
                "--   • Adding FOREIGN KEY constraints where referential integrity is already maintained\n" +
                "--\n" +
                "-- ACTION: Review and apply these constraints to better align your database with the model.\n" +
                "-- These changes will improve data integrity and help prevent future data quality issues.",

            OpportunityCategory.Validation =>
                "-- VALIDATIONS - INFORMATIONAL\n" +
                "--\n" +
                "-- These opportunities represent EXISTING constraints that profiling has validated.\n" +
                "-- The database already has these constraints in place, and the data conforms to them.\n" +
                "-- Examples include:\n" +
                "--   • Columns already marked as NOT NULL that have no null values\n" +
                "--   • Existing unique indexes with no duplicate values\n" +
                "--   • Foreign key constraints with no orphaned references\n" +
                "--\n" +
                "-- ACTION: No action needed. This is confirmation that your database and model are aligned.",

            _ =>
                "-- UNKNOWN CATEGORY\n" +
                "--\n" +
                "-- These opportunities could not be properly categorized."
        };

        builder.Append(description);
    }

    private static void AppendTypeDescription(StringBuilder builder, OpportunityType type, OpportunityCategory category)
    {
        var description = (type, category) switch
        {
            (OpportunityType.Nullability, OpportunityCategory.Contradiction) =>
                "-- NULLABILITY CONTRADICTIONS\n" +
                "-- Why this matters: Your OutSystems model marks these columns as Mandatory (NOT NULL),\n" +
                "-- but the actual database contains NULL values in these columns.\n" +
                "-- What to do: Update the NULL values to appropriate defaults, then add NOT NULL constraints.",

            (OpportunityType.Nullability, OpportunityCategory.Recommendation) =>
                "-- NULLABILITY RECOMMENDATIONS\n" +
                "-- Why this matters: These columns could be made NOT NULL to improve data integrity.\n" +
                "-- Profiling confirms no NULL values exist in the data.\n" +
                "-- What to do: Consider adding NOT NULL constraints to prevent future NULL insertions.",

            (OpportunityType.Nullability, OpportunityCategory.Validation) =>
                "-- NULLABILITY VALIDATIONS\n" +
                "-- Why this matters: Confirms that existing NOT NULL constraints are working correctly.\n" +
                "-- What to do: No action needed - this is validation that your constraints are effective.",

            (OpportunityType.UniqueIndex, OpportunityCategory.Contradiction) =>
                "-- UNIQUE INDEX CONTRADICTIONS\n" +
                "-- Why this matters: Your OutSystems model expects unique values, but duplicates exist.\n" +
                "-- What to do: Identify and resolve duplicate records before adding unique constraints.\n" +
                "-- This may require merging records or updating values to ensure uniqueness.",

            (OpportunityType.UniqueIndex, OpportunityCategory.Recommendation) =>
                "-- UNIQUE INDEX RECOMMENDATIONS\n" +
                "-- Why this matters: These columns have naturally unique values and could benefit from\n" +
                "-- a unique constraint to enforce this pattern and improve query performance.\n" +
                "-- What to do: Consider adding unique indexes to formalize this uniqueness guarantee.",

            (OpportunityType.UniqueIndex, OpportunityCategory.Validation) =>
                "-- UNIQUE INDEX VALIDATIONS\n" +
                "-- Why this matters: Confirms that existing unique constraints are working correctly.\n" +
                "-- What to do: No action needed - this validates your unique constraints are effective.",

            (OpportunityType.ForeignKey, OpportunityCategory.Contradiction) =>
                "-- FOREIGN KEY CONTRADICTIONS\n" +
                "-- Why this matters: Orphaned rows exist - child records reference parent records that\n" +
                "-- don't exist. This violates referential integrity and can cause application errors.\n" +
                "-- What to do: Delete orphaned records or update them to reference valid parent records,\n" +
                "-- then add foreign key constraints to prevent this from happening again.",

            (OpportunityType.ForeignKey, OpportunityCategory.Recommendation) =>
                "-- FOREIGN KEY RECOMMENDATIONS\n" +
                "-- Why this matters: Referential integrity is currently maintained by application logic,\n" +
                "-- but adding database-level foreign keys provides stronger guarantees and better performance.\n" +
                "-- What to do: Consider adding foreign key constraints to enforce referential integrity at\n" +
                "-- the database level and improve query optimization.",

            (OpportunityType.ForeignKey, OpportunityCategory.Validation) =>
                "-- FOREIGN KEY VALIDATIONS\n" +
                "-- Why this matters: Confirms that existing foreign key constraints are working correctly.\n" +
                "-- What to do: No action needed - this validates your referential integrity is maintained.",

            _ =>
                $"-- {type} opportunities in {category} category."
        };

        builder.Append(description);
    }

    private static ValidationReport NormalizeValidations(ValidationReport validations)
    {
        if (validations.Validations.IsDefaultOrEmpty)
        {
            return validations;
        }

        var sorted = validations.Validations.Sort(ValidationFindingComparer.Instance);
        if (sorted == validations.Validations)
        {
            return validations;
        }

        return new ValidationReport(sorted, validations.TypeCounts, validations.GeneratedAtUtc);
    }

    private sealed class ValidationFindingComparer : IComparer<ValidationFinding>
    {
        public static ValidationFindingComparer Instance { get; } = new();

        public int Compare(ValidationFinding? x, ValidationFinding? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var schemaComparison = string.Compare(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase);
            if (schemaComparison != 0)
            {
                return schemaComparison;
            }

            var tableComparison = string.Compare(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
            if (tableComparison != 0)
            {
                return tableComparison;
            }

            var constraintComparison = string.Compare(x.ConstraintName, y.ConstraintName, StringComparison.OrdinalIgnoreCase);
            if (constraintComparison != 0)
            {
                return constraintComparison;
            }

            var typeComparison = x.Type.CompareTo(y.Type);
            if (typeComparison != 0)
            {
                return typeComparison;
            }

            return string.Compare(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
        }
    }
}
