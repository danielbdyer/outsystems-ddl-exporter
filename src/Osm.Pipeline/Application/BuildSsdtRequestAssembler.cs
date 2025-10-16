using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
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
    IStaticEntityDataProvider? StaticDataProvider,
    CacheOptionsOverrides CacheOverrides,
    string? ConfigPath);

public sealed class BuildSsdtRequestAssembler
{
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

        if (cacheOptions is not null && !string.IsNullOrWhiteSpace(context.SqlOptions.ConnectionString))
        {
            var metadata = cacheOptions.Metadata is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(cacheOptions.Metadata, StringComparer.Ordinal);

            metadata["sql.connectionHash"] = ComputeSha256(context.SqlOptions.ConnectionString!);
            cacheOptions = cacheOptions with { Metadata = metadata };
        }

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
            context.StaticDataProvider,
            Path.Combine(context.OutputDirectory, "Seeds"));

        return new BuildSsdtRequestAssembly(request, profilerProvider, profilePath, context.OutputDirectory);
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(sha.ComputeHash(bytes));
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
}
