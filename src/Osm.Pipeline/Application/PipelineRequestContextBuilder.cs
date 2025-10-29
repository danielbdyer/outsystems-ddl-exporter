using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;

namespace Osm.Pipeline.Application;

internal static class PipelineRequestContextBuilder
{
    public static Result<PipelineRequestContext> Build(PipelineRequestContextBuilderRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.ConfigurationContext is null)
        {
            throw new ArgumentNullException(nameof(request.ConfigurationContext));
        }

        var configuration = request.ConfigurationContext.Configuration ?? CliConfiguration.Empty;
        var tightening = configuration.Tightening ?? TighteningOptions.Default;

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, request.ModuleFilterOverrides);
        if (moduleFilterResult.IsFailure)
        {
            return Result<PipelineRequestContext>.Failure(moduleFilterResult.Errors);
        }

        var sqlOptionsResult = SqlOptionsResolver.Resolve(configuration, request.SqlOptionsOverrides);
        if (sqlOptionsResult.IsFailure)
        {
            return Result<PipelineRequestContext>.Failure(sqlOptionsResult.Errors);
        }

        var typeMappingResult = TypeMappingPolicyResolver.Resolve(request.ConfigurationContext);
        if (typeMappingResult.IsFailure)
        {
            return Result<PipelineRequestContext>.Failure(typeMappingResult.Errors);
        }

        var supplementalOptions = ResolveSupplementalOptions(configuration.SupplementalModels);

        NamingOverrideOptions? namingOverrides = null;
        if (request.NamingOverrides is not null)
        {
            var namingResult = request.NamingOverrides.Binder.Bind(request.NamingOverrides.Overrides, tightening);
            if (namingResult.IsFailure)
            {
                return Result<PipelineRequestContext>.Failure(namingResult.Errors);
            }

            namingOverrides = namingResult.Value;
        }

        var metadataLog = string.IsNullOrWhiteSpace(request.SqlMetadataOutputPath)
            ? null
            : new SqlMetadataLog();

        Func<CancellationToken, Task> flushMetadataAsync = metadataLog is null
            ? static _ => Task.CompletedTask
            : async cancellationToken =>
            {
                var state = metadataLog.BuildState();
                if (!state.HasSnapshot && !state.HasErrors && !state.HasRequests)
                {
                    return;
                }

                await SqlMetadataDiagnosticsWriter
                    .WriteAsync(request.SqlMetadataOutputPath, metadataLog, cancellationToken)
                    .ConfigureAwait(false);
            };

        var context = new PipelineRequestContext(
            configuration,
            request.ConfigurationContext.ConfigPath,
            tightening,
            moduleFilterResult.Value,
            sqlOptionsResult.Value,
            typeMappingResult.Value,
            supplementalOptions,
            namingOverrides,
            request.CacheOptionsOverrides,
            request.SqlMetadataOutputPath,
            metadataLog,
            flushMetadataAsync);

        return Result<PipelineRequestContext>.Success(context);
    }

    private static SupplementalModelOptions ResolveSupplementalOptions(SupplementalModelConfiguration configuration)
    {
        configuration ??= SupplementalModelConfiguration.Empty;
        var includeUsers = configuration.IncludeUsers ?? true;
        var paths = configuration.Paths ?? Array.Empty<string>();
        return new SupplementalModelOptions(includeUsers, paths.ToArray());
    }
}

internal sealed record PipelineRequestContextBuilderRequest(
    CliConfigurationContext ConfigurationContext,
    ModuleFilterOverrides ModuleFilterOverrides,
    SqlOptionsOverrides SqlOptionsOverrides,
    CacheOptionsOverrides CacheOptionsOverrides,
    string? SqlMetadataOutputPath,
    NamingOverridesRequest? NamingOverrides);

public sealed record NamingOverridesRequest(BuildSsdtOverrides Overrides, INamingOverridesBinder Binder);

public sealed record PipelineRequestContext(
    CliConfiguration Configuration,
    string? ConfigPath,
    TighteningOptions Tightening,
    ModuleFilterOptions ModuleFilter,
    ResolvedSqlOptions SqlOptions,
    TypeMappingPolicy TypeMappingPolicy,
    SupplementalModelOptions SupplementalModels,
    NamingOverrideOptions? NamingOverrides,
    CacheOptionsOverrides CacheOverrides,
    string? SqlMetadataOutputPath,
    SqlMetadataLog? SqlMetadataLog,
    Func<CancellationToken, Task> FlushMetadataAsync)
{
    public EvidenceCachePipelineOptions? CreateCacheOptions(
        string command,
        string modelPath,
        string? profilePath,
        string? dmmPath)
        => EvidenceCacheOptionsFactory.Create(
            command,
            Configuration,
            Tightening,
            ModuleFilter,
            modelPath,
            profilePath,
            dmmPath,
            CacheOverrides,
            ConfigPath);

    public IReadOnlyDictionary<string, string?> BuildCacheMetadata(
        string? modelPath,
        string? profilePath,
        string? dmmPath)
        => CacheMetadataBuilder.Build(
            Tightening,
            ModuleFilter,
            Configuration,
            modelPath,
            profilePath,
            dmmPath);
}
