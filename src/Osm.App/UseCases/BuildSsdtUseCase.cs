using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.App.Configuration;
using Osm.App.StaticData;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Pipeline.Mediation;

namespace Osm.App.UseCases;

public sealed record BuildSsdtUseCaseInput(
    CliConfigurationContext ConfigurationContext,
    BuildSsdtOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed record BuildSsdtUseCaseResult(
    BuildSsdtPipelineResult PipelineResult,
    string ProfilerProvider,
    string? ProfilePath,
    string OutputDirectory);

public sealed class BuildSsdtUseCase
{
    private readonly ICommandDispatcher _dispatcher;

    public BuildSsdtUseCase(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<BuildSsdtUseCaseResult>> RunAsync(BuildSsdtUseCaseInput input, CancellationToken cancellationToken = default)
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
            return Result<BuildSsdtUseCaseResult>.Failure(sqlOptionsResult.Errors);
        }

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, input.ModuleFilter);
        if (moduleFilterResult.IsFailure)
        {
            return Result<BuildSsdtUseCaseResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;

        var profilerProvider = ResolveProfilerProvider(configuration, input.Overrides);
        var profilePathResult = ResolveProfilePath(profilerProvider, configuration, input.Overrides);
        if (profilePathResult.IsFailure)
        {
            return Result<BuildSsdtUseCaseResult>.Failure(profilePathResult.Errors);
        }

        var profilePath = profilePathResult.Value;

        var modelPath = ResolveModelPath(configuration, input.Overrides.ModelPath);
        if (modelPath.IsFailure)
        {
            return Result<BuildSsdtUseCaseResult>.Failure(modelPath.Errors);
        }

        var outputDirectory = ResolveOutputDirectory(input.Overrides.OutputDirectory);

        var namingOverridesResult = NamingOverridesResolver.Resolve(
            input.Overrides.RenameOverrides,
            SmoBuildOptions.FromEmission(tighteningOptions.Emission).NamingOverrides);

        if (namingOverridesResult.IsFailure)
        {
            return Result<BuildSsdtUseCaseResult>.Failure(namingOverridesResult.Errors);
        }

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission)
            .WithNamingOverrides(namingOverridesResult.Value);

        var staticDataProvider = ResolveStaticEntityDataProvider(input.Overrides.StaticDataPath, sqlOptionsResult.Value);

        var cacheOptions = ResolveCacheOptions(
            configuration,
            tighteningOptions,
            moduleFilter,
            modelPath.Value,
            profilePath,
            input.Cache,
            input.ConfigurationContext.ConfigPath);

        var request = new BuildSsdtPipelineRequest(
            modelPath.Value,
            moduleFilter,
            outputDirectory,
            tighteningOptions,
            ResolveSupplementalOptions(configuration.SupplementalModels),
            profilerProvider,
            profilePath,
            sqlOptionsResult.Value,
            smoOptions,
            cacheOptions,
            staticDataProvider,
            Path.Combine(outputDirectory, "Seeds", "StaticEntities.seed.sql"));

        var pipelineResult = await _dispatcher.DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<BuildSsdtUseCaseResult>.Failure(pipelineResult.Errors);
        }

        return new BuildSsdtUseCaseResult(
            pipelineResult.Value,
            profilerProvider,
            profilePath,
            outputDirectory);
    }

    private static Result<string> ResolveModelPath(CliConfiguration configuration, string? overridePath)
    {
        var modelPath = overridePath ?? configuration.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.model.missing",
                "Model path must be provided for SSDT emission.");
        }

        return Result<string>.Success(modelPath);
    }

    private static string ResolveOutputDirectory(string? overridePath)
        => string.IsNullOrWhiteSpace(overridePath) ? "out" : overridePath!;

    private static string ResolveProfilerProvider(CliConfiguration configuration, BuildSsdtOverrides overrides)
    {
        if (!string.IsNullOrWhiteSpace(overrides.ProfilerProvider))
        {
            return overrides.ProfilerProvider!;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.Provider))
        {
            return configuration.Profiler.Provider!;
        }

        return "fixture";
    }

    private static Result<string?> ResolveProfilePath(string provider, CliConfiguration configuration, BuildSsdtOverrides overrides)
    {
        if (string.Equals(provider, "fixture", StringComparison.OrdinalIgnoreCase))
        {
            var profilePath = overrides.ProfilePath
                ?? configuration.ProfilePath
                ?? configuration.Profiler.ProfilePath;

            if (string.IsNullOrWhiteSpace(profilePath))
            {
                return ValidationError.Create(
                    "pipeline.buildSsdt.profile.missing",
                    "Profile path must be provided when using the fixture profiler.");
            }

            return Result<string?>.Success(profilePath);
        }

        if (!string.IsNullOrWhiteSpace(overrides.ProfilePath))
        {
            return overrides.ProfilePath;
        }

        if (!string.IsNullOrWhiteSpace(configuration.ProfilePath))
        {
            return configuration.ProfilePath;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.ProfilePath))
        {
            return configuration.Profiler.ProfilePath;
        }

        return Result<string?>.Success(null);
    }

    private static SupplementalModelOptions ResolveSupplementalOptions(SupplementalModelConfiguration configuration)
    {
        configuration ??= SupplementalModelConfiguration.Empty;
        var includeUsers = configuration.IncludeUsers ?? true;
        var paths = configuration.Paths ?? Array.Empty<string>();
        return new SupplementalModelOptions(includeUsers, paths.ToArray());
    }

    private static IStaticEntityDataProvider? ResolveStaticEntityDataProvider(string? fixturePath, ResolvedSqlOptions sqlOptions)
    {
        if (!string.IsNullOrWhiteSpace(fixturePath))
        {
            return new FixtureStaticEntityDataProvider(fixturePath!);
        }

        if (!string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            var connectionOptions = new SqlConnectionOptions(
                sqlOptions.Authentication.Method,
                sqlOptions.Authentication.TrustServerCertificate,
                sqlOptions.Authentication.ApplicationName,
                sqlOptions.Authentication.AccessToken);

            var factory = new SqlConnectionFactory(sqlOptions.ConnectionString!, connectionOptions);
            return new SqlStaticEntityDataProvider(factory, sqlOptions.CommandTimeoutSeconds);
        }

        return null;
    }

    private static EvidenceCachePipelineOptions? ResolveCacheOptions(
        CliConfiguration configuration,
        TighteningOptions tightening,
        ModuleFilterOptions moduleFilter,
        string modelPath,
        string? profilePath,
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
            resolvedDmmPath: null);

        return new EvidenceCachePipelineOptions(
            cacheRoot!.Trim(),
            refresh,
            "build-ssdt",
            modelPath,
            profilePath,
            DmmPath: null,
            configPath,
            metadata);
    }
}
