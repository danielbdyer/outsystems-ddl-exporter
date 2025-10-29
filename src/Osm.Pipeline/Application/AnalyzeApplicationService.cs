using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

public sealed record AnalyzeApplicationInput(
    CliConfigurationContext ConfigurationContext,
    AnalyzeOverrides Overrides);

public sealed record AnalyzeApplicationResult(
    TighteningAnalysisPipelineResult PipelineResult,
    string OutputDirectory,
    string ModelPath,
    string ProfilePath);

public sealed class AnalyzeApplicationService : IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IFileSystem _fileSystem;

    public AnalyzeApplicationService(ICommandDispatcher dispatcher)
        : this(dispatcher, new FileSystem())
    {
    }

    public AnalyzeApplicationService(ICommandDispatcher dispatcher, IFileSystem fileSystem)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<Result<AnalyzeApplicationResult>> RunAsync(
        AnalyzeApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.ConfigurationContext is null)
        {
            throw new ArgumentNullException(nameof(input.ConfigurationContext));
        }

        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>());

        var contextResult = PipelineRequestContextBuilder.Build(new PipelineRequestContextBuilderRequest(
            input.ConfigurationContext,
            moduleFilterOverrides,
            SqlOptionsOverrides: null,
            CacheOptionsOverrides: null,
            SqlMetadataOutputPath: null,
            NamingOverrides: null));
        if (contextResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(contextResult.Errors);
        }

        var context = contextResult.Value;
        var configuration = context.Configuration;
        var overrides = input.Overrides ?? new AnalyzeOverrides(null, null, null);
        var tighteningOptions = context.Tightening;
        var moduleFilter = context.ModuleFilter;

        var modelPath = overrides.ModelPath ?? configuration.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return ValidationError.Create(
                "pipeline.analyze.model.missing",
                "Model path must be provided for tightening analysis.");
        }

        var profilePath = overrides.ProfilePath
            ?? configuration.ProfilePath
            ?? configuration.Profiler.ProfilePath;
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.missing",
                "Profile path must be provided for tightening analysis.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(overrides.OutputDirectory)
            ? "out"
            : overrides.OutputDirectory!;

        _fileSystem.Directory.CreateDirectory(outputDirectory);

        var request = new TighteningAnalysisPipelineRequest(
            modelPath,
            moduleFilter,
            tighteningOptions,
            context.SupplementalModels,
            profilePath,
            outputDirectory);

        var pipelineResult = await _dispatcher
            .DispatchAsync<TighteningAnalysisPipelineRequest, TighteningAnalysisPipelineResult>(request, cancellationToken)
            .ConfigureAwait(false);

        if (pipelineResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new AnalyzeApplicationResult(
            pipelineResult.Value,
            outputDirectory,
            modelPath,
            profilePath);
    }

}
