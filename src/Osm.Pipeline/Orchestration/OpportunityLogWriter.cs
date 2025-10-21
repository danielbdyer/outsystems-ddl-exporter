using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Pipeline.Orchestration;

public sealed record OpportunityArtifacts(
    string ReportPath,
    string SafeScriptPath,
    string SafeScript,
    string RemediationScriptPath,
    string RemediationScript);

public sealed class OpportunityLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<OpportunityArtifacts> WriteAsync(
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

        Directory.CreateDirectory(outputDirectory);
        var suggestionsDirectory = Path.Combine(outputDirectory, "suggestions");
        Directory.CreateDirectory(suggestionsDirectory);

        var reportPath = Path.Combine(outputDirectory, "opportunities.json");
        var safePath = Path.Combine(suggestionsDirectory, "safe-to-apply.sql");
        var remediationPath = Path.Combine(suggestionsDirectory, "needs-remediation.sql");

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(reportPath, json, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var safeScript = BuildSql(report.Opportunities.Where(o => o.Risk == ChangeRisk.SafeToApply));
        await File.WriteAllTextAsync(safePath, safeScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var remediationScript = BuildSql(report.Opportunities.Where(o => o.Risk == ChangeRisk.NeedsRemediation));
        await File.WriteAllTextAsync(remediationPath, remediationScript, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        return new OpportunityArtifacts(reportPath, safePath, safeScript, remediationPath, remediationScript);
    }

    private static string BuildSql(IEnumerable<Opportunity> opportunities)
    {
        var builder = new StringBuilder();
        var any = false;

        foreach (var opportunity in opportunities)
        {
            any = true;
            builder.Append("-- ");
            builder.Append(opportunity.Constraint);
            builder.Append(' ');
            builder.Append(opportunity.Schema);
            builder.Append('.');
            builder.Append(opportunity.Table);
            builder.Append(" (");
            builder.Append(opportunity.Name);
            builder.Append(") Risk=");
            builder.AppendLine(opportunity.Risk.ToString());

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

        if (!any)
        {
            builder.AppendLine("-- No opportunities in this category.");
        }

        return builder.ToString();
    }
}
