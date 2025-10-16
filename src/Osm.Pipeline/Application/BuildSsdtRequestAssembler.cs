using System;
using System.IO;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.StaticData;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Application;

public sealed record BuildSsdtRequestAssembly(
    BuildSsdtPipelineRequest Request,
    string ProfilerProvider,
    string? ProfilePath,
    string OutputDirectory);

public sealed record BuildSsdtRequestAssemblerContext(
    CliConfiguration Configuration,
    BuildSsdtOverrides Overrides,
    ModuleFilterOptions ModuleFilter,
    ResolvedSqlOptions SqlOptions,
    TighteningOptions TighteningOptions,
    TypeMappingPolicy TypeMappingPolicy,
    SmoBuildOptions SmoOptions,
    string ModelPath,
    string OutputDirectory,
    CacheOptionsOverrides CacheOverrides,
    string? ConfigPath);

public sealed class BuildSsdtRequestAssembler
{
    public string ResolveOutputDirectory(BuildSsdtOverrides overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        return string.IsNullOrWhiteSpace(overrides.OutputDirectory) ? "out" : overrides.OutputDirectory!;
    }

    public Result<BuildSsdtRequestAssembly> Assemble(BuildSsdtRequestAssemblerContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var profilerProvider = ResolveProfilerProvider(context.Configuration, context.Overrides);
        var profilePathResult = ResolveProfilePath(profilerProvider, context.Configuration, context.Overrides);
        if (profilePathResult.IsFailure)
        {
            return Result<BuildSsdtRequestAssembly>.Failure(profilePathResult.Errors);
        }

        var profilePath = profilePathResult.Value;
        var staticDataProvider = ResolveStaticEntityDataProvider(context.Overrides.StaticDataPath, context.SqlOptions);
        var cacheOptions = EvidenceCacheOptionsFactory.Create(
            "build-ssdt",
            context.Configuration,
            context.TighteningOptions,
            context.ModuleFilter,
            context.ModelPath,
            profilePath,
            dmmPath: null,
            context.CacheOverrides,
            context.ConfigPath);

        var request = new BuildSsdtPipelineRequest(
            context.ModelPath,
            context.ModuleFilter,
            context.OutputDirectory,
            context.TighteningOptions,
            ResolveSupplementalOptions(context.Configuration.SupplementalModels),
            profilerProvider,
            profilePath,
            context.SqlOptions,
            context.SmoOptions,
            context.TypeMappingPolicy,
            cacheOptions,
            staticDataProvider,
            Path.Combine(context.OutputDirectory, "Seeds"));

        return new BuildSsdtRequestAssembly(request, profilerProvider, profilePath, context.OutputDirectory);
    }

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
}
