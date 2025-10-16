using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Reports;

public interface IPipelineReportLauncher
{
    Task<string> GenerateAsync(BuildSsdtApplicationResult applicationResult, CancellationToken cancellationToken);

    void Open(string reportPath);
}

public sealed class PipelineReportLauncher : IPipelineReportLauncher
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ILogger<PipelineReportLauncher> _logger;

    public PipelineReportLauncher(ILogger<PipelineReportLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GenerateAsync(BuildSsdtApplicationResult applicationResult, CancellationToken cancellationToken)
    {
        if (applicationResult is null)
        {
            throw new ArgumentNullException(nameof(applicationResult));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var outputDirectory = applicationResult.OutputDirectory;
        var pipelineResult = applicationResult.PipelineResult;
        var manifest = pipelineResult.Manifest ?? throw new InvalidOperationException("Manifest not available for report generation.");
        var decisionReport = pipelineResult.DecisionReport ?? throw new InvalidOperationException("Decision report not available for report generation.");

        var moduleSummaries = manifest.Tables
            .GroupBy(table => table.Module, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ModuleSummary(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.Indexes?.Count ?? 0),
                group.Sum(entry => entry.ForeignKeys?.Count ?? 0)))
            .OrderBy(summary => summary.Module, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalTables = manifest.Tables.Count;
        var totalIndexes = manifest.Tables.Sum(entry => entry.Indexes?.Count ?? 0);
        var totalForeignKeys = manifest.Tables.Sum(entry => entry.ForeignKeys?.Count ?? 0);
        var diffPath = Path.Combine(outputDirectory, "dmm-diff.json");
        var hasDiff = File.Exists(diffPath);

        var staticSeedPaths = pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : pipelineResult.StaticSeedScriptPaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path!))
                .ToArray();

        var html = BuildHtml(
            applicationResult,
            manifest,
            decisionReport,
            moduleSummaries,
            totalTables,
            totalIndexes,
            totalForeignKeys,
            pipelineResult.PipelineInsights,
            hasDiff,
            staticSeedPaths);

        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "report.html");
        await File.WriteAllTextAsync(reportPath, html, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return reportPath;
    }

    public void Open(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("Report path must be provided.", nameof(reportPath));
        }

        if (!File.Exists(reportPath))
        {
            _logger.LogWarning("Report '{ReportPath}' does not exist.", reportPath);
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{reportPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", reportPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", reportPath);
            }
            else
            {
                _logger.LogWarning("Opening reports is not supported on this platform.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open pipeline report.");
        }
    }

    private static string BuildHtml(
        BuildSsdtApplicationResult applicationResult,
        SsdtManifest manifest,
        Osm.Validation.Tightening.PolicyDecisionReport decisionReport,
        ModuleSummary[] moduleSummaries,
        int totalTables,
        int totalIndexes,
        int totalForeignKeys,
        ImmutableArray<PipelineInsight> insights,
        bool hasDiff,
        IReadOnlyList<string> staticSeedPaths)
    {
        var builder = new StringBuilder();
        var outputDirectory = applicationResult.OutputDirectory;
        var insightItems = insights.IsDefaultOrEmpty
            ? Array.Empty<PipelineInsight>()
            : insights.Where(static insight => insight is not null).ToArray();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <title>OutSystems DDL Exporter Report</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 2rem; color: #1f2933; background-color: #f8fafc; }");
        builder.AppendLine("    h1 { margin-bottom: 0.25rem; }");
        builder.AppendLine("    h2 { margin-top: 2rem; }");
        builder.AppendLine("    .timestamp { color: #52606d; margin-bottom: 1.5rem; }");
        builder.AppendLine("    .summary-grid { display: flex; flex-wrap: wrap; gap: 1rem; margin: 0; padding: 0; list-style: none; }");
        builder.AppendLine("    .card { background: #ffffff; border-radius: 10px; padding: 1rem 1.25rem; box-shadow: 0 10px 25px -20px rgba(15, 23, 42, 0.6); min-width: 160px; }");
        builder.AppendLine("    .card .value { font-size: 1.8rem; font-weight: 600; color: #0b7285; }");
        builder.AppendLine("    .card .label { font-size: 0.9rem; color: #52606d; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    .decision-summary { background: #ffffff; border-radius: 10px; padding: 1rem 1.5rem; box-shadow: 0 10px 25px -20px rgba(15, 23, 42, 0.6); }");
        builder.AppendLine("    .decision-summary li { margin: 0.35rem 0; }");
        builder.AppendLine("    .insights-section { margin-top: 2rem; }");
        builder.AppendLine("    .insight-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1rem; margin: 0; padding: 0; list-style: none; }");
        builder.AppendLine("    .insight-card { background: #ffffff; border-radius: 12px; padding: 1rem 1.25rem; box-shadow: 0 18px 30px -22px rgba(15, 23, 42, 0.65); display: flex; flex-direction: column; gap: 0.75rem; }");
        builder.AppendLine("    .insight-header { display: flex; align-items: baseline; justify-content: space-between; gap: 0.5rem; }");
        builder.AppendLine("    .insight-badge { display: inline-flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; font-weight: 600; letter-spacing: 0.08em; padding: 0.25rem 0.6rem; border-radius: 999px; text-transform: uppercase; }");
        builder.AppendLine("    .insight-code { font-size: 0.85rem; color: #52606d; font-weight: 500; }");
        builder.AppendLine("    .insight-summary { margin: 0; color: #334e68; }");
        builder.AppendLine("    .insight-meta { display: grid; grid-template-columns: max-content 1fr; gap: 0.35rem 0.75rem; margin: 0; }");
        builder.AppendLine("    .insight-meta dt { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.08em; color: #52606d; }");
        builder.AppendLine("    .insight-meta dd { margin: 0; color: #102a43; }");
        builder.AppendLine("    .insight-doc { align-self: flex-start; font-weight: 600; color: #0b7285; }");
        builder.AppendLine("    .insight-doc:hover { text-decoration: underline; }");
        builder.AppendLine("    .insights-empty { color: #52606d; font-style: italic; }");
        builder.AppendLine("    .severity-info { background: rgba(59, 130, 246, 0.12); color: #1d4ed8; }");
        builder.AppendLine("    .severity-advisory { background: rgba(14, 165, 233, 0.12); color: #0f766e; }");
        builder.AppendLine("    .severity-warning { background: rgba(251, 191, 36, 0.2); color: #b45309; }");
        builder.AppendLine("    .severity-critical { background: rgba(248, 113, 113, 0.2); color: #b91c1c; }");
        builder.AppendLine("    table { border-collapse: collapse; width: 100%; background: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 10px 25px -20px rgba(15, 23, 42, 0.6); }");
        builder.AppendLine("    th, td { padding: 0.65rem 0.9rem; text-align: left; border-bottom: 1px solid #d9e2ec; }");
        builder.AppendLine("    th { background: #0b7285; color: #ffffff; font-weight: 600; letter-spacing: 0.05em; }");
        builder.AppendLine("    tr:last-child td { border-bottom: none; }");
        builder.AppendLine("    a { color: #0b7285; text-decoration: none; }");
        builder.AppendLine("    a:hover { text-decoration: underline; }");
        builder.AppendLine("    dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.5rem 1rem; }");
        builder.AppendLine("    dt { font-weight: 600; color: #334e68; }");
        builder.AppendLine("    dd { margin: 0; color: #52606d; word-break: break-word; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <h1>OutSystems DDL Exporter Report</h1>");
        builder.AppendLine($"  <p class=\"timestamp\">Generated {DateTimeOffset.UtcNow:O} (UTC)</p>");

        builder.AppendLine("  <section>");
        builder.AppendLine("    <h2>Overview</h2>");
        builder.AppendLine("    <ul class=\"summary-grid\">");
        builder.AppendLine(RenderCard("Modules", moduleSummaries.Length));
        builder.AppendLine(RenderCard("Tables", totalTables));
        builder.AppendLine(RenderCard("Indexes", totalIndexes));
        builder.AppendLine(RenderCard("Foreign Keys", totalForeignKeys));
        builder.AppendLine("    </ul>");
        builder.AppendLine("  </section>");

        builder.AppendLine("  <section>");
        builder.AppendLine("    <h2>Policy decisions</h2>");
        builder.AppendLine("    <ul class=\"decision-summary\">");
        builder.AppendLine($"      <li><strong>Columns tightened:</strong> {decisionReport.TightenedColumnCount:N0} of {decisionReport.ColumnCount:N0}</li>");
        builder.AppendLine($"      <li><strong>Unique indexes enforced:</strong> {decisionReport.UniqueIndexesEnforcedCount:N0} of {decisionReport.UniqueIndexCount:N0}</li>");
        builder.AppendLine($"      <li><strong>Foreign keys created:</strong> {decisionReport.ForeignKeysCreatedCount:N0} of {decisionReport.ForeignKeyCount:N0}</li>");
        if (decisionReport.Diagnostics.Length > 0)
        {
            builder.AppendLine("      <li><strong>Diagnostics:</strong></li>");
            builder.AppendLine("      <ul>");
            foreach (var diagnostic in decisionReport.Diagnostics)
            {
                builder.AppendLine($"        <li>{diagnostic.Code}: {WebUtility.HtmlEncode(diagnostic.Message)}</li>");
            }
            builder.AppendLine("      </ul>");
        }
        builder.AppendLine("    </ul>");
        builder.AppendLine("  </section>");

        builder.AppendLine("  <section>");
        builder.AppendLine("    <h2>Artifacts</h2>");
        builder.AppendLine("    <dl>");
        builder.AppendLine($"      <dt>Output directory</dt><dd>{WebUtility.HtmlEncode(outputDirectory)}</dd>");
        builder.AppendLine($"      <dt>Manifest</dt><dd><a href=\"manifest.json\">manifest.json</a></dd>");
        builder.AppendLine($"      <dt>Decision log</dt><dd><a href=\"policy-decisions.json\">policy-decisions.json</a></dd>");
        if (staticSeedPaths.Count > 0)
        {
            builder.AppendLine("      <dt>Static seed scripts</dt>");
            builder.AppendLine("      <dd><ul>");
            foreach (var path in staticSeedPaths)
            {
                var fileName = Path.GetFileName(path);
                builder.AppendLine($"        <li><a href=\"{WebUtility.HtmlEncode(fileName)}\">{WebUtility.HtmlEncode(fileName)}</a></li>");
            }
            builder.AppendLine("      </ul></dd>");
        }
        if (hasDiff)
        {
            builder.AppendLine("      <dt>DMM diff</dt><dd><a href=\"dmm-diff.json\">dmm-diff.json</a></dd>");
        }
        builder.AppendLine("    </dl>");
        builder.AppendLine("  </section>");

        builder.AppendLine("  <section class=\"insights-section\">");
        builder.AppendLine("    <h2>Insights</h2>");
        if (insightItems.Length == 0)
        {
            builder.AppendLine("    <p class=\"insights-empty\">No insights were recorded for this run.</p>");
        }
        else
        {
            builder.AppendLine("    <ul class=\"insight-grid\">");
            foreach (var insight in insightItems)
            {
                var severityClass = insight.Severity switch
                {
                    PipelineInsightSeverity.Advisory => "severity-advisory",
                    PipelineInsightSeverity.Warning => "severity-warning",
                    PipelineInsightSeverity.Critical => "severity-critical",
                    _ => "severity-info"
                };

                builder.AppendLine("      <li class=\"insight-card\">");
                builder.AppendLine("        <div class=\"insight-header\">");
                builder.AppendLine($"          <span class=\"insight-badge {severityClass}\">{insight.Severity}</span>");
                builder.AppendLine($"          <span class=\"insight-code\">{WebUtility.HtmlEncode(insight.Code)}</span>");
                builder.AppendLine("        </div>");
                builder.AppendLine($"        <p class=\"insight-summary\">{WebUtility.HtmlEncode(insight.Summary ?? string.Empty)}</p>");
                builder.AppendLine("        <dl class=\"insight-meta\">");
                var scope = insight.AffectedObjects.Length > 0
                    ? string.Join(", ", insight.AffectedObjects)
                    : null;
                if (!string.IsNullOrWhiteSpace(scope))
                {
                    builder.AppendLine($"          <dt>Scope</dt><dd>{WebUtility.HtmlEncode(scope)}</dd>");
                }
                if (!string.IsNullOrWhiteSpace(insight.SuggestedAction))
                {
                    builder.AppendLine($"          <dt>Action</dt><dd>{WebUtility.HtmlEncode(insight.SuggestedAction)}</dd>");
                }
                builder.AppendLine("        </dl>");
                if (!string.IsNullOrWhiteSpace(insight.DocumentationUri))
                {
                    builder.AppendLine($"        <a class=\"insight-doc\" href=\"{WebUtility.HtmlEncode(insight.DocumentationUri)}\">Documentation</a>");
                }
                builder.AppendLine("      </li>");
            }
            builder.AppendLine("    </ul>");
        }
        builder.AppendLine("  </section>");

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string RenderCard(string label, int value)
        => $"    <li class=\"card\"><div class=\"value\">{value:N0}</div><div class=\"label\">{label}</div></li>";

    private sealed record ModuleSummary(string Module, int TableCount, int IndexCount, int ForeignKeyCount);
}
