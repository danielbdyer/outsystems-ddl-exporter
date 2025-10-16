using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Diagnostics;
using Osm.Pipeline.Reports;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Hosting.Verbs;

public sealed record BuildSsdtVerbOptions(
    string? ConfigPath,
    BuildSsdtOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache,
    bool OpenReport);

public sealed class BuildSsdtVerb : PipelineVerb<BuildSsdtVerbOptions>
{
    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> _applicationService;
    private readonly IPipelineReportLauncher _reportLauncher;
    private readonly ILogger<BuildSsdtVerb> _logger;
    private readonly TimeProvider _timeProvider;

    public BuildSsdtVerb(
        ICliConfigurationService configurationService,
        IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> applicationService,
        IPipelineReportLauncher reportLauncher,
        ILogger<BuildSsdtVerb> logger,
        TimeProvider timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        _reportLauncher = reportLauncher ?? throw new ArgumentNullException(nameof(reportLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public override string Name => "build-ssdt";

    protected override async Task<PipelineVerbResult> RunInternalAsync(BuildSsdtVerbOptions options, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();

        try
        {
            var configurationResult = await _configurationService
                .LoadAsync(options.ConfigPath, cancellationToken)
                .ConfigureAwait(false);
            if (configurationResult.IsFailure)
            {
                VerbLogging.LogErrors(_logger, configurationResult.Errors);
                return new PipelineVerbResult(1);
            }

            var input = new BuildSsdtApplicationInput(
                configurationResult.Value,
                options.Overrides,
                options.ModuleFilter,
                options.Sql,
                options.Cache);

            var applicationResult = await _applicationService
                .RunAsync(input, cancellationToken)
                .ConfigureAwait(false);
            if (applicationResult.IsFailure)
            {
                VerbLogging.LogErrors(_logger, applicationResult.Errors);
                return new PipelineVerbResult(1);
            }

            return await EmitAsync(applicationResult.Value, startedAt, options.OpenReport, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("build-ssdt cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "build-ssdt failed.");
            return new PipelineVerbResult(1);
        }
    }

    private async Task<PipelineVerbResult> EmitAsync(
        BuildSsdtApplicationResult result,
        DateTimeOffset startedAt,
        bool openReport,
        CancellationToken cancellationToken)
    {
        var pipelineResult = result.PipelineResult;
        var artifacts = new List<ArtifactRef>();

        if (!string.IsNullOrWhiteSpace(result.ModelPath))
        {
            if (result.ModelWasExtracted)
            {
                _logger.LogInformation("Extracted model to {ModelPath}.", result.ModelPath);
            }
            else
            {
                _logger.LogInformation("Using model at {ModelPath}.", result.ModelPath);
            }
        }

        if (!result.ModelExtractionWarnings.IsDefaultOrEmpty && result.ModelExtractionWarnings.Length > 0)
        {
            VerbLogging.LogWarnings(_logger, result.ModelExtractionWarnings);
        }

        if (string.Equals(result.ProfilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            var snapshotJson = ProfileSnapshotDebugFormatter.ToJson(pipelineResult.Profile);
            _logger.LogInformation("SQL profiler snapshot:{NewLine}{Snapshot}", Environment.NewLine, snapshotJson);
        }

        VerbLogging.LogPipelineLog(_logger, pipelineResult.ExecutionLog);
        VerbLogging.LogWarnings(_logger, pipelineResult.Warnings);

        foreach (var diagnostic in pipelineResult.DecisionReport.Diagnostics)
        {
            if (diagnostic.Severity == TighteningDiagnosticSeverity.Warning)
            {
                _logger.LogWarning("{Code}: {Message}", diagnostic.Code, diagnostic.Message);
            }
        }

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            _logger.LogInformation(
                "Cached inputs to {CacheDirectory} (key {Key}).",
                cacheResult.CacheDirectory,
                cacheResult.Manifest.Key);
            AddArtifact(artifacts, "evidence-cache", cacheResult.CacheDirectory);
        }

        if (!pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty && pipelineResult.StaticSeedScriptPaths.Length > 0)
        {
            foreach (var seedPath in pipelineResult.StaticSeedScriptPaths)
            {
                if (string.IsNullOrWhiteSpace(seedPath))
                {
                    continue;
                }

                _logger.LogInformation("Static entity seed script written to {SeedPath}.", seedPath);
                AddArtifact(artifacts, "static-seed", seedPath);
            }
        }

        var outputDirectory = result.OutputDirectory;
        _logger.LogInformation(
            "Emitted {TableCount} tables to {OutputDirectory}.",
            pipelineResult.Manifest.Tables.Count,
            outputDirectory);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        _logger.LogInformation("Manifest written to {ManifestPath}.", manifestPath);
        AddArtifact(artifacts, "manifest", manifestPath);

        _logger.LogInformation(
            "Columns tightened: {Tightened}/{TotalColumns}.",
            pipelineResult.DecisionReport.TightenedColumnCount,
            pipelineResult.DecisionReport.ColumnCount);
        _logger.LogInformation(
            "Unique indexes enforced: {Enforced}/{TotalUnique}.",
            pipelineResult.DecisionReport.UniqueIndexesEnforcedCount,
            pipelineResult.DecisionReport.UniqueIndexCount);
        _logger.LogInformation(
            "Foreign keys created: {Created}/{TotalForeignKeys}.",
            pipelineResult.DecisionReport.ForeignKeysCreatedCount,
            pipelineResult.DecisionReport.ForeignKeyCount);

        foreach (var summary in PolicyDecisionSummaryFormatter.FormatForConsole(pipelineResult.DecisionReport))
        {
            _logger.LogInformation("{Summary}", summary);
        }

        _logger.LogInformation("Decision log written to {DecisionLogPath}.", pipelineResult.DecisionLogPath);
        AddArtifact(artifacts, "policy-decisions", pipelineResult.DecisionLogPath);

        if (openReport)
        {
            try
            {
                var reportPath = await _reportLauncher
                    .GenerateAsync(result, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation("Report written to {ReportPath}.", reportPath);
                _reportLauncher.Open(reportPath);
                AddArtifact(artifacts, "pipeline-report", reportPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate or open pipeline report.");
            }
        }

        var completedAt = _timeProvider.GetUtcNow();
        var run = new PipelineRun(Name, startedAt, completedAt, artifacts);
        return new PipelineVerbResult(0, run);
    }

    private static void AddArtifact(ICollection<ArtifactRef> artifacts, string name, string? path)
    {
        if (artifacts is null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        artifacts.Add(new ArtifactRef(name, Path.GetFullPath(path)));
    }
}
