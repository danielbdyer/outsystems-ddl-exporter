using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
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
        var overrides = input.Overrides ?? new ExtractModelOverrides(null, null, null, null, null, null);
        var moduleFilterConfiguration = configuration.ModuleFilter ?? ModuleFilterConfiguration.Empty;
        bool? includeInactiveOverride = overrides.OnlyActiveAttributes.HasValue
            ? !overrides.OnlyActiveAttributes.Value
            : null;

        var moduleFilterOverrides = new ModuleFilterOverrides(
            overrides.Modules ?? Array.Empty<string>(),
            overrides.IncludeSystemModules,
            includeInactiveOverride,
            Array.Empty<string>(),
            Array.Empty<string>());

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, moduleFilterOverrides);
        if (moduleFilterResult.IsFailure)
        {
            _logger.LogError(
                "Failed to resolve module filter: {Errors}.",
                string.Join(", ", moduleFilterResult.Errors.Select(static error => error.Code)));
            return Result<ExtractModelApplicationResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;
        var moduleNames = moduleFilter.Modules.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : moduleFilter.Modules.Select(static module => module.Value).ToArray();

        var moduleSource = overrides.Modules is { Count: > 0 }
            ? "cli"
            : moduleFilterConfiguration.Modules is { Count: > 0 }
                ? "config"
                : "default";

        var includeSystemSource = overrides.IncludeSystemModules.HasValue
            ? "cli"
            : moduleFilterConfiguration.IncludeSystemModules.HasValue
                ? "config"
                : "default";

        var includeInactiveSource = overrides.OnlyActiveAttributes.HasValue
            ? "cli"
            : moduleFilterConfiguration.IncludeInactiveModules.HasValue
                ? "config"
                : "default";

        var onlyActiveAttributes = overrides.OnlyActiveAttributes
            ?? !moduleFilter.IncludeInactiveModules;

        var fixtureProvided = !string.IsNullOrWhiteSpace(overrides.MockAdvancedSqlManifest);

        _logger.LogDebug(
            "extract-model configuration resolved (configPath: {ConfigPath}, moduleSource: {ModuleSource}, includeSystemSource: {IncludeSource}, includeInactiveSource: {IncludeInactiveSource}).",
            configurationContext.ConfigPath ?? "<none>",
            moduleSource,
            includeSystemSource,
            includeInactiveSource);

        if (moduleNames.Length > 0)
        {
            _logger.LogInformation(
                "extract-model modules ({Source}): {Modules}.",
                moduleSource,
                string.Join(",", moduleNames));
        }
        else
        {
            _logger.LogInformation("extract-model modules ({Source}): <all>.", moduleSource);
        }

        _logger.LogInformation(
            "extract-model options: includeSystem={IncludeSystem}, includeInactiveModules={IncludeInactive}, onlyActiveAttributes={OnlyActive}, fixtureProvided={FixtureProvided}.",
            moduleFilter.IncludeSystemModules,
            moduleFilter.IncludeInactiveModules,
            onlyActiveAttributes,
            fixtureProvided);

        var commandModules = moduleNames.Length > 0 ? moduleNames : null;
        var commandResult = ModelExtractionCommand.Create(
            commandModules,
            moduleFilter.IncludeSystemModules,
            moduleFilter.IncludeInactiveModules,
            onlyActiveAttributes);
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

        var metadataLog = string.IsNullOrWhiteSpace(overrides.SqlMetadataOutputPath)
            ? null
            : new SqlMetadataLog();

        var request = new ExtractModelPipelineRequest(
            commandResult.Value,
            sqlOptionsResult.Value,
            overrides.MockAdvancedSqlManifest,
            overrides.SqlMetadataOutputPath,
            metadataLog);

        _logger.LogInformation(
            "Dispatching extract-model pipeline (outputPath: {OutputPath}, metadataPath: {MetadataPath}, fixtureProvided: {FixtureProvided}).",
            outputPath,
            string.IsNullOrWhiteSpace(overrides.SqlMetadataOutputPath) ? "<none>" : overrides.SqlMetadataOutputPath,
            fixtureProvided);

        var extractionResult = await _dispatcher.DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(
            request,
            cancellationToken).ConfigureAwait(false);

        if (metadataLog is not null && !string.IsNullOrWhiteSpace(overrides.SqlMetadataOutputPath))
        {
            await SqlMetadataDiagnosticsWriter
                .WriteAsync(overrides.SqlMetadataOutputPath!, metadataLog, cancellationToken)
                .ConfigureAwait(false);
        }

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
