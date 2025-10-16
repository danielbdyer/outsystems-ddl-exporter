using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Application;

public sealed class ModelResolutionService : IModelResolutionService
{
    private readonly ICommandDispatcher _dispatcher;

    public ModelResolutionService(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<ModelResolutionResult>> ResolveModelAsync(
        CliConfiguration configuration,
        BuildSsdtOverrides overrides,
        ModuleFilterOptions moduleFilter,
        ResolvedSqlOptions sqlOptions,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        overrides ??= new BuildSsdtOverrides(null, null, null, null, null, null, null);

        var candidatePath = overrides.ModelPath ?? configuration.ModelPath;
        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            return new ModelResolutionResult(candidatePath!, false, ImmutableArray<string>.Empty);
        }

        var moduleNames = moduleFilter.Modules.IsDefaultOrEmpty
            ? null
            : moduleFilter.Modules.Select(static module => module.Value);

        var extractionCommandResult = ModelExtractionCommand.Create(
            moduleNames,
            moduleFilter.IncludeSystemModules,
            onlyActiveAttributes: false);
        if (extractionCommandResult.IsFailure)
        {
            return Result<ModelResolutionResult>.Failure(extractionCommandResult.Errors);
        }

        if (string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.model.extraction.connectionStringMissing",
                "Model path was not provided and SQL extraction requires a connection string. Provide --model, configure model.path, or supply --connection-string/sql.connectionString.");
        }

        var extractRequest = new ExtractModelPipelineRequest(
            extractionCommandResult.Value,
            sqlOptions,
            AdvancedSqlFixtureManifestPath: null);

        var extractionResult = await _dispatcher
            .DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(extractRequest, cancellationToken)
            .ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ModelResolutionResult>.Failure(extractionResult.Errors);
        }

        var extraction = extractionResult.Value;
        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(outputDirectory);

        Directory.CreateDirectory(resolvedOutputDirectory);
        var modelPath = Path.Combine(resolvedOutputDirectory, "model.extracted.json");
        await File.WriteAllTextAsync(modelPath, extraction.Json, cancellationToken).ConfigureAwait(false);

        var warnings = extraction.Warnings.Count == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(extraction.Warnings);

        return new ModelResolutionResult(Path.GetFullPath(modelPath), true, warnings);
    }
}
