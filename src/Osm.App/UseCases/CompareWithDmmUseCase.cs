using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.App.Configuration;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Smo;
using Osm.Smo.TypeMapping;
using Osm.Pipeline.Mediation;

namespace Osm.App.UseCases;

public sealed record CompareWithDmmUseCaseInput(
    CliConfigurationContext ConfigurationContext,
    CompareWithDmmOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed record CompareWithDmmUseCaseResult(
    DmmComparePipelineResult PipelineResult,
    string DiffOutputPath);

public sealed class CompareWithDmmUseCase
{
    private readonly ICommandDispatcher _dispatcher;

    public CompareWithDmmUseCase(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<CompareWithDmmUseCaseResult>> RunAsync(CompareWithDmmUseCaseInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var configuration = input.ConfigurationContext.Configuration;
        var tighteningOptions = configuration.Tightening;

        var sqlOptionsResult = SqlOptionsResolver.Resolve(configuration, input.Sql);
        if (sqlOptionsResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(sqlOptionsResult.Errors);
        }

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, input.ModuleFilter);
        if (moduleFilterResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;

        var modelPathResult = ResolveRequiredPath(
            input.Overrides.ModelPath,
            configuration.ModelPath,
            "pipeline.dmmCompare.model.missing",
            "Model path must be provided for DMM comparison.");

        if (modelPathResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(modelPathResult.Errors);
        }

        var profilePathResult = ResolveRequiredPath(
            input.Overrides.ProfilePath,
            configuration.ProfilePath ?? configuration.Profiler.ProfilePath,
            "pipeline.dmmCompare.profile.missing",
            "Profile path must be provided for DMM comparison.");

        if (profilePathResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(profilePathResult.Errors);
        }

        var dmmPathResult = ResolveRequiredPath(
            input.Overrides.DmmPath,
            configuration.DmmPath,
            "pipeline.dmmCompare.dmm.missing",
            "DMM path must be provided for comparison.");

        if (dmmPathResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(dmmPathResult.Errors);
        }

        var outputDirectory = ResolveOutputDirectory(input.Overrides.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var diffPath = Path.Combine(outputDirectory, "dmm-diff.json");

        if (input.Overrides.MaxDegreeOfParallelism is { } maxDegreeOfParallelism && maxDegreeOfParallelism < 1)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(
                ValidationError.Create(
                    "pipeline.dmmCompare.parallelism.invalid",
                    "Max degree of parallelism must be at least 1."));
        }

        var smoOptionsBase = SmoBuildOptions.FromEmission(tighteningOptions.Emission, applyNamingOverrides: false);
        if (input.Overrides.MaxDegreeOfParallelism is { } overrideParallelism)
        {
            smoOptionsBase = smoOptionsBase.WithModuleParallelism(overrideParallelism);
        }

        var smoOptions = smoOptionsBase;

        var typeMappingResult = TypeMappingPolicyResolver.Resolve(configuration, input.ConfigurationContext.ConfigPath);
        if (typeMappingResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(typeMappingResult.Errors);
        }

        var typeMappingPolicy = typeMappingResult.Value;

        var cacheOptions = ResolveCacheOptions(
            configuration,
            tighteningOptions,
            moduleFilter,
            modelPathResult.Value,
            profilePathResult.Value,
            dmmPathResult.Value,
            input.Cache,
            input.ConfigurationContext.ConfigPath);

        var request = new DmmComparePipelineRequest(
            modelPathResult.Value,
            moduleFilter,
            profilePathResult.Value,
            dmmPathResult.Value,
            tighteningOptions,
            ResolveSupplementalOptions(configuration.SupplementalModels),
            sqlOptionsResult.Value,
            smoOptions,
            typeMappingPolicy,
            diffPath,
            cacheOptions);

        var pipelineResult = await _dispatcher.DispatchAsync<DmmComparePipelineRequest, DmmComparePipelineResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<CompareWithDmmUseCaseResult>.Failure(pipelineResult.Errors);
        }

        var resolvedDiffPath = pipelineResult.Value.DiffArtifactPath;
        return new CompareWithDmmUseCaseResult(pipelineResult.Value, resolvedDiffPath);
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

    private static SupplementalModelOptions ResolveSupplementalOptions(SupplementalModelConfiguration configuration)
    {
        configuration ??= SupplementalModelConfiguration.Empty;
        var includeUsers = configuration.IncludeUsers ?? true;
        var paths = configuration.Paths ?? Array.Empty<string>();
        return new SupplementalModelOptions(includeUsers, paths.ToArray());
    }

    private static EvidenceCachePipelineOptions? ResolveCacheOptions(
        CliConfiguration configuration,
        TighteningOptions tightening,
        ModuleFilterOptions moduleFilter,
        string modelPath,
        string profilePath,
        string dmmPath,
        CacheOptionsOverrides overrides,
        string? configPath)
    {
        var cacheRoot = overrides.Root ?? configuration.Cache.Root;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return null;
        }

        var refresh = overrides.Refresh ?? configuration.Cache.Refresh ?? false;
        var metadata = CacheMetadataBuilder.Build(
            tightening,
            moduleFilter,
            configuration,
            modelPath,
            profilePath,
            dmmPath);

        return new EvidenceCachePipelineOptions(
            cacheRoot!.Trim(),
            refresh,
            "dmm-compare",
            modelPath,
            profilePath,
            dmmPath,
            configPath,
            metadata);
    }

    private static string ResolveOutputDirectory(string? overridePath)
        => string.IsNullOrWhiteSpace(overridePath) ? "out" : overridePath!;
}
