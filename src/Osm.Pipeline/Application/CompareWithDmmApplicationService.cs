using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Smo;

namespace Osm.Pipeline.Application;

public sealed record CompareWithDmmApplicationInput(
    CliConfigurationContext ConfigurationContext,
    CompareWithDmmOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed record CompareWithDmmApplicationResult(
    DmmComparePipelineResult PipelineResult,
    string DiffOutputPath);

public sealed class CompareWithDmmApplicationService : PipelineApplicationServiceBase, IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IFileSystem _fileSystem;

    public CompareWithDmmApplicationService(ICommandDispatcher dispatcher)
        : this(dispatcher, new FileSystem())
    {
    }

    public CompareWithDmmApplicationService(ICommandDispatcher dispatcher, IFileSystem fileSystem)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<Result<CompareWithDmmApplicationResult>> RunAsync(CompareWithDmmApplicationInput input, CancellationToken cancellationToken = default)
    {
        input = EnsureNotNull(input, nameof(input));

        var configurationContext = EnsureNotNull(input.ConfigurationContext, nameof(input.ConfigurationContext));

        var contextResult = BuildContext(new PipelineRequestContextBuilderRequest(
            configurationContext,
            input.ModuleFilter,
            input.Sql,
            input.Cache,
            SqlMetadataOutputPath: null,
            NamingOverrides: null));
        if (contextResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(contextResult.Errors);
        }

        var context = contextResult.Value;

        var configuration = context.Configuration;
        var tighteningOptions = context.Tightening;
        var moduleFilter = context.ModuleFilter;
        var typeMappingPolicy = context.TypeMappingPolicy;

        var modelPathResult = RequirePath(
            input.Overrides.ModelPath,
            configuration.ModelPath,
            "pipeline.dmmCompare.model.missing",
            "Model path must be provided for DMM comparison.");

        if (modelPathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(modelPathResult.Errors);
        }

        var profilePathResult = RequirePath(
            input.Overrides.ProfilePath,
            configuration.ProfilePath ?? configuration.Profiler.ProfilePath,
            "pipeline.dmmCompare.profile.missing",
            "Profile path must be provided for DMM comparison.");

        if (profilePathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(profilePathResult.Errors);
        }

        var dmmPathResult = RequirePath(
            input.Overrides.DmmPath,
            configuration.DmmPath,
            "pipeline.dmmCompare.dmm.missing",
            "DMM path must be provided for comparison.");

        if (dmmPathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(dmmPathResult.Errors);
        }

        var outputDirectory = ResolveOutputDirectory(input.Overrides.OutputDirectory);
        _fileSystem.Directory.CreateDirectory(outputDirectory);
        var diffPath = _fileSystem.Path.Combine(outputDirectory, "dmm-diff.json");

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission, applyNamingOverrides: false);

        if (input.Overrides.MaxDegreeOfParallelism is int moduleParallelism)
        {
            if (moduleParallelism <= 0)
            {
                await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);
                return ValidationError.Create(
                    "cli.dmmCompare.parallelism.invalid",
                    "--max-degree-of-parallelism must be a positive integer when specified.");
            }

            smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
        }

        var cacheOptions = CreateCacheOptions(
            context,
            "dmm-compare",
            modelPathResult.Value,
            profilePathResult.Value,
            dmmPathResult.Value);

        var scope = new ModelExecutionScope(
            modelPathResult.Value,
            moduleFilter,
            context.SupplementalModels,
            tighteningOptions,
            context.SqlOptions,
            smoOptions,
            typeMappingPolicy,
            profilePathResult.Value);

        var request = new DmmComparePipelineRequest(
            scope,
            dmmPathResult.Value,
            diffPath,
            cacheOptions);

        var pipelineResult = await _dispatcher.DispatchAsync<DmmComparePipelineRequest, DmmComparePipelineResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(pipelineResult.Errors);
        }

        var resolvedDiffPath = pipelineResult.Value.DiffArtifactPath;
        return new CompareWithDmmApplicationResult(pipelineResult.Value, resolvedDiffPath);
    }

}
