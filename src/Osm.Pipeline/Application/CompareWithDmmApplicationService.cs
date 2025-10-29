using System;
using System.IO;
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

    public sealed class CompareWithDmmApplicationService : IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly PipelineRequestContextFactory _contextFactory;

        public CompareWithDmmApplicationService(
            ICommandDispatcher dispatcher,
            PipelineRequestContextFactory contextFactory)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

    public async Task<Result<CompareWithDmmApplicationResult>> RunAsync(CompareWithDmmApplicationInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        await using var contextScope = await _contextFactory
            .CreateAsync(
                new PipelineRequestContextFactoryRequest(
                    input.ConfigurationContext,
                    input.ModuleFilter,
                    input.Sql,
                    input.Cache,
                    SqlMetadataOutputPath: null,
                    NamingOverrides: null),
                cancellationToken)
            .ConfigureAwait(false);

        if (contextScope.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(contextScope.Errors);
        }

        var context = contextScope.Context;

        var configuration = context.Configuration;
        var tighteningOptions = context.Tightening;
        var moduleFilter = context.ModuleFilter;
        var typeMappingPolicy = context.TypeMappingPolicy;

        var modelPathResult = ResolveRequiredPath(
            input.Overrides.ModelPath,
            configuration.ModelPath,
            "pipeline.dmmCompare.model.missing",
            "Model path must be provided for DMM comparison.");

        if (modelPathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(modelPathResult.Errors);
        }

        var profilePathResult = ResolveRequiredPath(
            input.Overrides.ProfilePath,
            configuration.ProfilePath ?? configuration.Profiler.ProfilePath,
            "pipeline.dmmCompare.profile.missing",
            "Profile path must be provided for DMM comparison.");

        if (profilePathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(profilePathResult.Errors);
        }

        var dmmPathResult = ResolveRequiredPath(
            input.Overrides.DmmPath,
            configuration.DmmPath,
            "pipeline.dmmCompare.dmm.missing",
            "DMM path must be provided for comparison.");

        if (dmmPathResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(dmmPathResult.Errors);
        }

        var outputDirectory = ResolveOutputDirectory(input.Overrides.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var diffPath = Path.Combine(outputDirectory, "dmm-diff.json");

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission, applyNamingOverrides: false);

        if (input.Overrides.MaxDegreeOfParallelism is int moduleParallelism)
        {
            if (moduleParallelism <= 0)
            {
                return ValidationError.Create(
                    "cli.dmmCompare.parallelism.invalid",
                    "--max-degree-of-parallelism must be a positive integer when specified.");
            }

            smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
        }

        var cacheOptions = context.CreateCacheOptions(
            "dmm-compare",
            modelPathResult.Value,
            profilePathResult.Value,
            dmmPathResult.Value);

        var request = new DmmComparePipelineRequest(
            modelPathResult.Value,
            moduleFilter,
            profilePathResult.Value,
            dmmPathResult.Value,
            tighteningOptions,
            context.SupplementalModels,
            context.SqlOptions,
            smoOptions,
            typeMappingPolicy,
            diffPath,
            cacheOptions);

        var pipelineResult = await _dispatcher.DispatchAsync<DmmComparePipelineRequest, DmmComparePipelineResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<CompareWithDmmApplicationResult>.Failure(pipelineResult.Errors);
        }

        var resolvedDiffPath = pipelineResult.Value.DiffArtifactPath;
        return new CompareWithDmmApplicationResult(pipelineResult.Value, resolvedDiffPath);
    }

    private static Result<string> ResolveRequiredPath(string? overridePath, string? fallbackPath, string errorCode, string errorMessage)
    {
        var resolved = overridePath ?? fallbackPath;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return ValidationError.Create(errorCode, errorMessage);
        }

        return Result<string>.Success(resolved);
    }

    private static string ResolveOutputDirectory(string? overridePath)
        => string.IsNullOrWhiteSpace(overridePath) ? "out" : overridePath!;
}
