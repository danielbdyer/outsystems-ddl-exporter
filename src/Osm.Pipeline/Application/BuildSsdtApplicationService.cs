using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.StaticData;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Application;

public sealed record BuildSsdtApplicationInput(
    CliConfigurationContext ConfigurationContext,
    BuildSsdtOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed record BuildSsdtApplicationResult(
    BuildSsdtPipelineResult PipelineResult,
    string ProfilerProvider,
    string? ProfilePath,
    string OutputDirectory,
    string ModelPath,
    bool ModelWasExtracted,
    ImmutableArray<string> ModelExtractionWarnings);

public sealed class BuildSsdtApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;

    public BuildSsdtApplicationService(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
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
            return Result<BuildSsdtApplicationResult>.Failure(sqlOptionsResult.Errors);
        }

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, input.ModuleFilter);
        if (moduleFilterResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;

        var typeMappingResult = TypeMappingPolicyResolver.Resolve(input.ConfigurationContext);
        if (typeMappingResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(typeMappingResult.Errors);
        }

        var typeMappingPolicy = typeMappingResult.Value;

        var profilerProvider = ResolveProfilerProvider(configuration, input.Overrides);
        var profilePathResult = ResolveProfilePath(profilerProvider, configuration, input.Overrides);
        if (profilePathResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(profilePathResult.Errors);
        }

        var profilePath = profilePathResult.Value;
        var outputDirectory = ResolveOutputDirectory(input.Overrides.OutputDirectory);

        var modelResolutionResult = await ResolveModelAsync(
            configuration,
            input.Overrides,
            moduleFilter,
            sqlOptionsResult.Value,
            outputDirectory,
            cancellationToken).ConfigureAwait(false);
        if (modelResolutionResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(modelResolutionResult.Errors);
        }

        var modelResolution = modelResolutionResult.Value;

        var namingOverridesResult = NamingOverridesResolver.Resolve(
            input.Overrides.RenameOverrides,
            SmoBuildOptions.FromEmission(tighteningOptions.Emission).NamingOverrides);

        if (namingOverridesResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(namingOverridesResult.Errors);
        }

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission)
            .WithNamingOverrides(namingOverridesResult.Value);

        if (input.Overrides.MaxDegreeOfParallelism is int moduleParallelism)
        {
            if (moduleParallelism <= 0)
            {
                return ValidationError.Create(
                    "cli.buildSsdt.parallelism.invalid",
                    "--max-degree-of-parallelism must be a positive integer when specified.");
            }

            smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
        }

        var staticDataProvider = ResolveStaticEntityDataProvider(input.Overrides.StaticDataPath, sqlOptionsResult.Value);

        var cacheOptions = ResolveCacheOptions(
            configuration,
            tighteningOptions,
            moduleFilter,
            modelResolution.ModelPath,
            profilePath,
            input.Cache,
            input.ConfigurationContext.ConfigPath);

        var request = new BuildSsdtPipelineRequest(
            modelResolution.ModelPath,
            moduleFilter,
            outputDirectory,
            tighteningOptions,
            ResolveSupplementalOptions(configuration.SupplementalModels),
            profilerProvider,
            profilePath,
            sqlOptionsResult.Value,
            smoOptions,
            typeMappingPolicy,
            cacheOptions,
            staticDataProvider,
            Path.Combine(outputDirectory, "Seeds"));

        var pipelineResult = await _dispatcher.DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(
            request,
            cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new BuildSsdtApplicationResult(
            pipelineResult.Value,
            profilerProvider,
            profilePath,
            outputDirectory,
            modelResolution.ModelPath,
            modelResolution.WasExtracted,
            modelResolution.Warnings);
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

    private async Task<Result<ModelResolution>> ResolveModelAsync(
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
            return new ModelResolution(candidatePath!, WasExtracted: false, ImmutableArray<string>.Empty);
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
            return Result<ModelResolution>.Failure(extractionCommandResult.Errors);
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
            return Result<ModelResolution>.Failure(extractionResult.Errors);
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

        return new ModelResolution(Path.GetFullPath(modelPath), true, warnings);
    }

    private sealed record ModelResolution(string ModelPath, bool WasExtracted, ImmutableArray<string> Warnings);

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
            var connectionOptions = sqlOptions.ToConnectionOptions();
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
