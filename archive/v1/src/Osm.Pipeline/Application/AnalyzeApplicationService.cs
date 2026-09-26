using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Smo;

namespace Osm.Pipeline.Application;

public sealed record AnalyzeApplicationInput(
    CliConfigurationContext ConfigurationContext,
    AnalyzeOverrides Overrides,
    TighteningOverrides? TighteningOverrides = null);

public sealed record AnalyzeApplicationResult(
    TighteningAnalysisPipelineResult PipelineResult,
    string OutputDirectory,
    string ModelPath,
    string ProfilePath);

public sealed class AnalyzeApplicationService : PipelineApplicationServiceBase, IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>
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
        input = EnsureNotNull(input, nameof(input));
        var configurationContext = EnsureNotNull(input.ConfigurationContext, nameof(input.ConfigurationContext));

        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>());

        var contextResult = BuildContext(new PipelineRequestContextBuilderRequest(
            configurationContext,
            moduleFilterOverrides,
            SqlOptionsOverrides: null,
            CacheOptionsOverrides: null,
            SqlMetadataOutputPath: null,
            NamingOverrides: null,
            TighteningOverrides: input.TighteningOverrides));
        if (contextResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(contextResult.Errors);
        }

        var context = contextResult.Value;
        var configuration = context.Configuration;
        var overrides = input.Overrides ?? new AnalyzeOverrides(null, null, null);
        var tighteningOptions = context.Tightening;
        var moduleFilter = context.ModuleFilter;

        var modelPathResult = RequirePath(
            overrides.ModelPath,
            configuration.ModelPath,
            "pipeline.analyze.model.missing",
            "Model path must be provided for tightening analysis.");
        if (modelPathResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(modelPathResult.Errors);
        }

        var profilePathResult = RequirePath(
            overrides.ProfilePath,
            configuration.ProfilePath ?? configuration.Profiler.ProfilePath,
            "pipeline.analyze.profile.missing",
            "Profile path must be provided for tightening analysis.");
        if (profilePathResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(profilePathResult.Errors);
        }

        var modelPath = modelPathResult.Value;
        var profilePath = profilePathResult.Value;

        var outputDirectory = ResolveOutputDirectory(overrides.OutputDirectory);

        _fileSystem.Directory.CreateDirectory(outputDirectory);

        var scope = new ModelExecutionScope(
            modelPath,
            moduleFilter,
            context.SupplementalModels,
            tighteningOptions,
            context.SqlOptions,
            SmoBuildOptions.FromEmission(tighteningOptions.Emission),
            context.TypeMappingPolicy,
            profilePath);

        var request = new TighteningAnalysisPipelineRequest(
            scope,
            outputDirectory);

        var pipelineResult = await _dispatcher
            .DispatchAsync<TighteningAnalysisPipelineRequest, TighteningAnalysisPipelineResult>(request, cancellationToken)
            .ConfigureAwait(false);

        await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);

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
