using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Validation.Tightening.Opportunities;
using System.Linq;

namespace Osm.Pipeline.Orchestration;

public sealed record OpportunityArtifactPaths(string ReportPath, string SafeScriptPath, string RemediationScriptPath);

public sealed class OpportunityLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<OpportunityArtifactPaths> WriteAsync(
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
        await File.WriteAllTextAsync(reportPath, json, cancellationToken).ConfigureAwait(false);

        await WriteSqlAsync(safePath, report.Opportunities.Where(o => o.Risk == ChangeRisk.SafeToApply), cancellationToken)
            .ConfigureAwait(false);
        await WriteSqlAsync(remediationPath, report.Opportunities.Where(o => o.Risk == ChangeRisk.NeedsRemediation), cancellationToken)
            .ConfigureAwait(false);

        return new OpportunityArtifactPaths(reportPath, safePath, remediationPath);
    }

    private static async Task WriteSqlAsync(
        string path,
        IEnumerable<Opportunity> opportunities,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var any = false;
        foreach (var opportunity in opportunities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            any = true;
            await writer.WriteLineAsync($"-- {opportunity.Constraint} {opportunity.Schema}.{opportunity.Table} ({opportunity.Name}) Risk={opportunity.Risk}").ConfigureAwait(false);

            if (!opportunity.Rationales.IsDefaultOrEmpty)
            {
                foreach (var rationale in opportunity.Rationales)
                {
                    await writer.WriteLineAsync($"-- Rationale: {rationale}").ConfigureAwait(false);
                }
            }

            if (!opportunity.Evidence.IsDefaultOrEmpty)
            {
                foreach (var evidence in opportunity.Evidence)
                {
                    await writer.WriteLineAsync($"-- Evidence: {evidence}").ConfigureAwait(false);
                }
            }

            if (opportunity.HasStatements)
            {
                foreach (var statement in opportunity.Statements)
                {
                    await writer.WriteLineAsync(statement).ConfigureAwait(false);
                }
            }
            else
            {
                await writer.WriteLineAsync("-- No automated statement available.").ConfigureAwait(false);
            }

            await writer.WriteLineAsync("GO").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (!any)
        {
            await writer.WriteLineAsync("-- No opportunities in this category.").ConfigureAwait(false);
        }
    }
}
