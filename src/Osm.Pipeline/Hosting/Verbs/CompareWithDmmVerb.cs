using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Hosting.Verbs;

public sealed record CompareWithDmmVerbOptions(
    string? ConfigPath,
    CompareWithDmmOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed class CompareWithDmmVerb : PipelineVerb<CompareWithDmmVerbOptions>
{
    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult> _applicationService;
    private readonly ILogger<CompareWithDmmVerb> _logger;
    private readonly TimeProvider _timeProvider;

    public CompareWithDmmVerb(
        ICliConfigurationService configurationService,
        IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult> applicationService,
        ILogger<CompareWithDmmVerb> logger,
        TimeProvider timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public override string Name => "dmm-compare";

    protected override async Task<PipelineVerbResult> RunInternalAsync(CompareWithDmmVerbOptions options, CancellationToken cancellationToken)
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

            var input = new CompareWithDmmApplicationInput(
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

            return Emit(applicationResult.Value, startedAt);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("dmm-compare cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "dmm-compare failed.");
            return new PipelineVerbResult(1);
        }
    }

    private PipelineVerbResult Emit(CompareWithDmmApplicationResult result, DateTimeOffset startedAt)
    {
        var pipelineResult = result.PipelineResult;
        VerbLogging.LogPipelineLog(_logger, pipelineResult.ExecutionLog);
        VerbLogging.LogWarnings(_logger, pipelineResult.Warnings);

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            _logger.LogInformation(
                "Cached inputs to {CacheDirectory} (key {Key}).",
                cacheResult.CacheDirectory,
                cacheResult.Manifest.Key);
        }

        var artifacts = new List<ArtifactRef>
        {
            new("dmm-diff", result.DiffOutputPath)
        };

        var comparison = pipelineResult.Comparison;
        if (comparison.IsMatch)
        {
            _logger.LogInformation(
                "DMM parity confirmed. Diff artifact written to {DiffPath}.",
                result.DiffOutputPath);
            var completed = _timeProvider.GetUtcNow();
            var run = new PipelineRun(Name, startedAt, completed, artifacts);
            return new PipelineVerbResult(0, run);
        }

        if (comparison.ModelDifferences.Count > 0)
        {
            _logger.LogWarning("Model requires additional SSDT coverage:");
            foreach (var difference in comparison.ModelDifferences)
            {
                _logger.LogWarning(" - {Difference}", FormatDifference(difference));
            }
        }

        if (comparison.SsdtDifferences.Count > 0)
        {
            _logger.LogWarning("SSDT scripts drift from OutSystems model:");
            foreach (var difference in comparison.SsdtDifferences)
            {
                _logger.LogWarning(" - {Difference}", FormatDifference(difference));
            }
        }

        _logger.LogWarning("Diff artifact written to {DiffPath}.", result.DiffOutputPath);
        var completedAt = _timeProvider.GetUtcNow();
        var failureRun = new PipelineRun(Name, startedAt, completedAt, artifacts);
        return new PipelineVerbResult(2, failureRun);
    }

    private static string FormatDifference(DmmDifference difference)
    {
        if (difference is null)
        {
            return string.Empty;
        }

        var scopeParts = new List<string>(capacity: 3);
        if (!string.IsNullOrWhiteSpace(difference.Schema))
        {
            scopeParts.Add(difference.Schema);
        }

        if (!string.IsNullOrWhiteSpace(difference.Table))
        {
            scopeParts.Add(difference.Table);
        }

        var scope = scopeParts.Count > 0 ? string.Join('.', scopeParts) : "artifact";

        if (!string.IsNullOrWhiteSpace(difference.Column))
        {
            scope += $".{difference.Column}";
        }
        else if (!string.IsNullOrWhiteSpace(difference.Index))
        {
            scope += $" [Index: {difference.Index}]";
        }
        else if (!string.IsNullOrWhiteSpace(difference.ForeignKey))
        {
            scope += $" [FK: {difference.ForeignKey}]";
        }

        var property = string.IsNullOrWhiteSpace(difference.Property) ? "Difference" : difference.Property;
        var expected = difference.Expected ?? "<none>";
        var actual = difference.Actual ?? "<none>";

        var message = $"{scope} – {property} expected {expected} actual {actual}";
        if (!string.IsNullOrWhiteSpace(difference.ArtifactPath))
        {
            message += $" ({difference.ArtifactPath})";
        }

        return message;
    }
}
