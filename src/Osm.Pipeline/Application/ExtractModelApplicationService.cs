using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.Application;

public sealed record ExtractModelApplicationInput(
    CliConfigurationContext ConfigurationContext,
    ExtractModelOverrides Overrides,
    SqlOptionsOverrides Sql);

public sealed record ExtractModelApplicationResult(
    ModelExtractionResult ExtractionResult,
    string OutputPath);

public sealed class ExtractModelApplicationService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ILogger<ExtractModelApplicationService> _logger;

    public ExtractModelApplicationService(
        ICommandDispatcher dispatcher,
        ILogger<ExtractModelApplicationService>? logger = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? NullLogger<ExtractModelApplicationService>.Instance;
    }

    public async Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var configurationContext = input.ConfigurationContext;
        if (configurationContext is null)
        {
            throw new ArgumentNullException(nameof(input.ConfigurationContext));
        }

        var configuration = configurationContext.Configuration ?? CliConfiguration.Empty;
        var overrides = input.Overrides ?? new ExtractModelOverrides(null, null, null, null, null);
        var moduleFilter = configuration.ModuleFilter ?? ModuleFilterConfiguration.Empty;

        var overrideModules = overrides.Modules ?? Array.Empty<string>();
        var configModules = moduleFilter.Modules ?? Array.Empty<string>();
        var moduleSource = overrideModules.Count > 0
            ? "cli"
            : configModules.Count > 0 ? "config" : "default";
        var resolvedModules = moduleSource == "cli"
            ? overrideModules.Where(static module => !string.IsNullOrWhiteSpace(module)).Select(static module => module.Trim()).ToArray()
            : configModules.Where(static module => !string.IsNullOrWhiteSpace(module)).Select(static module => module.Trim()).ToArray();

        bool includeSystem;
        var includeSystemSource = "default";
        if (overrides.IncludeSystemModules.HasValue)
        {
            includeSystem = overrides.IncludeSystemModules.Value;
            includeSystemSource = "cli";
        }
        else if (moduleFilter.IncludeSystemModules.HasValue)
        {
            includeSystem = moduleFilter.IncludeSystemModules.Value;
            includeSystemSource = "config";
        }
        else
        {
            includeSystem = false;
        }

        bool onlyActiveAttributes;
        var onlyActiveSource = "default";
        if (overrides.OnlyActiveAttributes.HasValue)
        {
            onlyActiveAttributes = overrides.OnlyActiveAttributes.Value;
            onlyActiveSource = "cli";
        }
        else if (moduleFilter.IncludeInactiveModules.HasValue)
        {
            onlyActiveAttributes = !moduleFilter.IncludeInactiveModules.Value;
            onlyActiveSource = "config";
        }
        else
        {
            onlyActiveAttributes = false;
        }

        var fixtureProvided = !string.IsNullOrWhiteSpace(overrides.MockAdvancedSqlManifest);

        _logger.LogDebug(
            "extract-model configuration resolved (configPath: {ConfigPath}, moduleSource: {ModuleSource}, includeSystemSource: {IncludeSource}, onlyActiveSource: {OnlyActiveSource}).",
            configurationContext.ConfigPath ?? "<none>",
            moduleSource,
            includeSystemSource,
            onlyActiveSource);

        if (resolvedModules.Length > 0)
        {
            _logger.LogInformation(
                "extract-model modules ({Source}): {Modules}.",
                moduleSource,
                string.Join(",", resolvedModules));
        }
        else
        {
            _logger.LogInformation("extract-model modules ({Source}): <all>.", moduleSource);
        }

        _logger.LogInformation(
            "extract-model options: includeSystem={IncludeSystem}, onlyActiveAttributes={OnlyActive}, fixtureProvided={FixtureProvided}.",
            includeSystem,
            onlyActiveAttributes,
            fixtureProvided);

        var commandModules = resolvedModules.Length > 0 ? resolvedModules : null;
        var commandResult = ModelExtractionCommand.Create(commandModules, includeSystem, onlyActiveAttributes);
        if (commandResult.IsFailure)
        {
            _logger.LogError(
                "Failed to create extraction command: {Errors}.",
                string.Join(", ", commandResult.Errors.Select(static error => error.Code)));
            return Result<ExtractModelApplicationResult>.Failure(commandResult.Errors);
        }

        var outputPath = string.IsNullOrWhiteSpace(overrides.OutputPath)
            ? "model.extracted.json"
            : overrides.OutputPath!;

        var sqlOptionsResult = SqlOptionsResolver.Resolve(configuration, input.Sql);
        if (sqlOptionsResult.IsFailure)
        {
            _logger.LogError(
                "Failed to resolve SQL options: {Errors}.",
                string.Join(", ", sqlOptionsResult.Errors.Select(static error => error.Code)));
            return Result<ExtractModelApplicationResult>.Failure(sqlOptionsResult.Errors);
        }

        var request = new ExtractModelPipelineRequest(
            commandResult.Value,
            sqlOptionsResult.Value,
            overrides.MockAdvancedSqlManifest);

        _logger.LogInformation(
            "Dispatching extract-model pipeline (outputPath: {OutputPath}, fixtureProvided: {FixtureProvided}).",
            outputPath,
            fixtureProvided);

        var extractionResult = await _dispatcher.DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            _logger.LogError(
                "Model extraction pipeline failed: {Errors}.",
                string.Join(", ", extractionResult.Errors.Select(static error => error.Code)));
            return Result<ExtractModelApplicationResult>.Failure(extractionResult.Errors);
        }

        return new ExtractModelApplicationResult(extractionResult.Value, outputPath);
    }
}
