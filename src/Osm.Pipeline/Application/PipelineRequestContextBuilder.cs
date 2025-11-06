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

        if (request.TighteningOverrides is { } overrides && overrides.HasOverrides)
        {
            var tighteningResult = ApplyTighteningOverrides(tightening, overrides);
            if (tighteningResult.IsFailure)
            {
                return Result<PipelineRequestContext>.Failure(tighteningResult.Errors);
            }

            tightening = tighteningResult.Value;
        }

        var moduleFilterOverrides = request.ModuleFilterOverrides
            ?? new ModuleFilterOverrides(
                Array.Empty<string>(),
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>());

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, moduleFilterOverrides);
        if (moduleFilterResult.IsFailure)
        {
            return Result<PipelineRequestContext>.Failure(moduleFilterResult.Errors);
        }

        var sqlOverrides = request.SqlOptionsOverrides
            ?? new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null);

        var sqlOptionsResult = SqlOptionsResolver.Resolve(configuration, sqlOverrides);
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

        var cacheOverrides = request.CacheOptionsOverrides ?? new CacheOptionsOverrides(null, null);

        var context = new PipelineRequestContext(
            configuration,
            request.ConfigurationContext.ConfigPath,
            tightening,
            moduleFilterResult.Value,
            sqlOptionsResult.Value,
            typeMappingResult.Value,
            supplementalOptions,
            namingOverrides,
            cacheOverrides,
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

    private static Result<TighteningOptions> ApplyTighteningOverrides(
        TighteningOptions baseOptions,
        TighteningOverrides overrides)
    {
        if (baseOptions is null)
        {
            throw new ArgumentNullException(nameof(baseOptions));
        }

        if (overrides is null || !overrides.HasOverrides)
        {
            return baseOptions;
        }

        var policy = baseOptions.Policy;
        var foreignKeys = baseOptions.ForeignKeys;
        var uniqueness = baseOptions.Uniqueness;
        var emission = baseOptions.Emission;
        var remediation = baseOptions.Remediation;
        var mocking = baseOptions.Mocking;

        var remediationSentinels = remediation.Sentinels;
        var sentinelOverrideProvided = overrides.RemediationSentinelNumeric is not null
            || overrides.RemediationSentinelText is not null
            || overrides.RemediationSentinelDate is not null;

        if (sentinelOverrideProvided)
        {
            var sentinelResult = RemediationSentinelOptions.Create(
                overrides.RemediationSentinelNumeric ?? remediationSentinels.Numeric,
                overrides.RemediationSentinelText ?? remediationSentinels.Text,
                overrides.RemediationSentinelDate ?? remediationSentinels.Date);

            if (sentinelResult.IsFailure)
            {
                return Result<TighteningOptions>.Failure(sentinelResult.Errors);
            }

            remediationSentinels = sentinelResult.Value;
        }

        if (sentinelOverrideProvided
            || overrides.RemediationGeneratePreScripts.HasValue
            || overrides.RemediationMaxRowsDefaultBackfill.HasValue)
        {
            var remediationResult = RemediationOptions.Create(
                overrides.RemediationGeneratePreScripts ?? remediation.GeneratePreScripts,
                remediationSentinels,
                overrides.RemediationMaxRowsDefaultBackfill ?? remediation.MaxRowsDefaultBackfill);

            if (remediationResult.IsFailure)
            {
                return Result<TighteningOptions>.Failure(remediationResult.Errors);
            }

            remediation = remediationResult.Value;
        }

        if (overrides.MockingUseProfileMockFolder.HasValue || overrides.MockingProfileMockFolder is not null)
        {
            var mockFolder = overrides.MockingProfileMockFolder ?? mocking.ProfileMockFolder;
            var mockingResult = MockingOptions.Create(
                overrides.MockingUseProfileMockFolder ?? mocking.UseProfileMockFolder,
                mockFolder);

            if (mockingResult.IsFailure)
            {
                return Result<TighteningOptions>.Failure(mockingResult.Errors);
            }

            mocking = mockingResult.Value;
        }

        return TighteningOptions.Create(policy, foreignKeys, uniqueness, remediation, emission, mocking);
    }
}

internal sealed record PipelineRequestContextBuilderRequest(
    CliConfigurationContext ConfigurationContext,
    ModuleFilterOverrides? ModuleFilterOverrides,
    SqlOptionsOverrides? SqlOptionsOverrides,
    CacheOptionsOverrides? CacheOptionsOverrides,
    string? SqlMetadataOutputPath,
    NamingOverridesRequest? NamingOverrides,
    TighteningOverrides? TighteningOverrides = null);

internal sealed record NamingOverridesRequest(BuildSsdtOverrides Overrides, INamingOverridesBinder Binder);

internal sealed record PipelineRequestContext(
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
