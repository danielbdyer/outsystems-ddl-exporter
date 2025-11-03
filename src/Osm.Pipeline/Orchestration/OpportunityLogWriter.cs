using System;
using System.Collections.Generic;
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

namespace Osm.Pipeline.Orchestration;

public sealed record OpportunityArtifacts(
    string ReportPath,
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

        try
        {
            _fileSystem.Directory.CreateDirectory(outputDirectory);
            var suggestionsDirectory = _fileSystem.Path.Combine(outputDirectory, "suggestions");
            _fileSystem.Directory.CreateDirectory(suggestionsDirectory);

            var reportPath = _fileSystem.Path.Combine(outputDirectory, "opportunities.json");
            var safePath = _fileSystem.Path.Combine(suggestionsDirectory, "safe-to-apply.sql");
            var remediationPath = _fileSystem.Path.Combine(suggestionsDirectory, "needs-remediation.sql");

            var json = JsonSerializer.Serialize(report, JsonOptions);
            await _fileSystem.File.WriteAllTextAsync(reportPath, json, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            var safeOpportunities = report.Opportunities.Where(o => o.Disposition == OpportunityDisposition.ReadyToApply).ToList();
            var safeScript = BuildSql(safeOpportunities, report, "Safe to Apply");
            await _fileSystem.File.WriteAllTextAsync(safePath, safeScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            var remediationOpportunities = report.Opportunities.Where(o => o.Disposition == OpportunityDisposition.NeedsRemediation).ToList();
            var remediationScript = BuildSql(remediationOpportunities, report, "Needs Remediation");
            await _fileSystem.File.WriteAllTextAsync(remediationPath, remediationScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            return Result<OpportunityArtifacts>.Success(new OpportunityArtifacts(reportPath, safePath, safeScript, remediationPath, remediationScript));
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

        if (report.ValidationCount > 0)
        {
            builder.AppendLine($"--   Validations: {report.ValidationCount} (Existing constraints confirmed by profiling)");
        }

        builder.AppendLine("--");
        builder.AppendLine($"-- This script contains {opportunities.Count} {scriptCategory.ToLowerInvariant()} opportunities.");
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

        // Group by category for better organization
        var byCategory = opportunities.GroupBy(o => o.Category).OrderBy(g => g.Key);

        foreach (var categoryGroup in byCategory)
        {
            builder.AppendLine($"-- ========== {categoryGroup.Key} ==========");
            builder.AppendLine();

            foreach (var opportunity in categoryGroup)
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

        return builder.ToString();
    }
}
