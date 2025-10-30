using System;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Application;

public sealed class ModelResolutionService : IModelResolutionService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IFileSystem _fileSystem;

    public ModelResolutionService(ICommandDispatcher dispatcher)
        : this(dispatcher, new FileSystem())
    {
    }

    public ModelResolutionService(ICommandDispatcher dispatcher, IFileSystem fileSystem)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<Result<ModelResolutionResult>> ResolveModelAsync(
        CliConfiguration configuration,
        BuildSsdtOverrides overrides,
        ModuleFilterOptions moduleFilter,
        ResolvedSqlOptions sqlOptions,
        string outputDirectory,
        SqlMetadataLog? sqlMetadataLog,
        CancellationToken cancellationToken)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        overrides ??= new BuildSsdtOverrides(null, null, null, null, null, null, null, null);

        var extractionRequested = overrides.ExtractModelInline;
        var candidatePath = overrides.ModelPath ?? configuration.ModelPath;
        if (!extractionRequested && !string.IsNullOrWhiteSpace(candidatePath))
        {
            return new ModelResolutionResult(candidatePath!, false, ImmutableArray<string>.Empty);
        }

        var moduleNames = moduleFilter.Modules.IsDefaultOrEmpty
            ? null
            : moduleFilter.Modules.Select(static module => module.Value);

        var extractionCommandResult = ModelExtractionCommand.Create(
            moduleNames,
            moduleFilter.IncludeSystemModules,
            moduleFilter.IncludeInactiveModules,
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
            AdvancedSqlFixtureManifestPath: null,
            OutputPath: null,
            SqlMetadataOutputPath: overrides.SqlMetadataOutputPath,
            SqlMetadataLog: sqlMetadataLog);

        var extractionResult = await _dispatcher
            .DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(extractRequest, cancellationToken)
            .ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<ModelResolutionResult>.Failure(extractionResult.Errors);
        }

        var extraction = extractionResult.Value;
        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? _fileSystem.Directory.GetCurrentDirectory()
            : _fileSystem.Path.GetFullPath(outputDirectory);

        _fileSystem.Directory.CreateDirectory(resolvedOutputDirectory);
        var modelPath = _fileSystem.Path.Combine(resolvedOutputDirectory, "model.extracted.json");
        await using (var outputStream = _fileSystem.File.Create(modelPath))
        {
            await extraction.JsonPayload.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }

        var warnings = extraction.Warnings.Count == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(extraction.Warnings);

        return new ModelResolutionResult(
            _fileSystem.Path.GetFullPath(modelPath),
            true,
            warnings,
            extraction);
    }
}
