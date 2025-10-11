using System;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

internal static class SqlOptionsResolver
{
    public static Result<ResolvedSqlOptions> Resolve(CliConfiguration configuration, SqlOptionsOverrides overrides)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        overrides ??= new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null, null, null, null, null);

        var connection = overrides.ConnectionString ?? configuration.Sql.ConnectionString;
        var timeout = overrides.CommandTimeoutSeconds ?? configuration.Sql.CommandTimeoutSeconds;

        if (overrides.CommandTimeoutSeconds.HasValue && overrides.CommandTimeoutSeconds.Value < 0)
        {
            return ValidationError.Create("cli.sql.commandTimeout.invalid", "Command timeout must be non-negative.");
        }

        var sampling = configuration.Sql.Sampling;
        var authentication = configuration.Sql.Authentication;

        if (overrides.SamplingThreshold.HasValue)
        {
            if (overrides.SamplingThreshold.Value <= 0)
            {
                return ValidationError.Create("cli.sql.samplingThreshold.invalid", "Sampling threshold must be positive.");
            }

            sampling = sampling with { RowSamplingThreshold = overrides.SamplingThreshold };
        }

        if (overrides.SamplingSize.HasValue)
        {
            if (overrides.SamplingSize.Value <= 0)
            {
                return ValidationError.Create("cli.sql.samplingSize.invalid", "Sampling size must be positive.");
            }

            sampling = sampling with { SampleSize = overrides.SamplingSize };
        }

        if (overrides.AuthenticationMethod.HasValue)
        {
            authentication = authentication with { Method = overrides.AuthenticationMethod };
        }

        if (overrides.TrustServerCertificate.HasValue)
        {
            authentication = authentication with { TrustServerCertificate = overrides.TrustServerCertificate };
        }

        if (!string.IsNullOrWhiteSpace(overrides.ApplicationName))
        {
            authentication = authentication with { ApplicationName = overrides.ApplicationName!.Trim() };
        }

        if (!string.IsNullOrWhiteSpace(overrides.AccessToken))
        {
            authentication = authentication with { AccessToken = overrides.AccessToken };
        }

        var profilerExecutionResult = ResolveProfilerExecution(configuration.Sql.ProfilerExecution, overrides);
        if (profilerExecutionResult.IsFailure)
        {
            return Result<ResolvedSqlOptions>.Failure(profilerExecutionResult.Errors);
        }

        return new ResolvedSqlOptions(
            connection?.Trim(),
            timeout,
            new SqlSamplingSettings(sampling.RowSamplingThreshold, sampling.SampleSize),
            new SqlAuthenticationSettings(authentication.Method, authentication.TrustServerCertificate, authentication.ApplicationName, authentication.AccessToken),
            profilerExecutionResult.Value);
    }

    private static Result<SqlProfilerExecutionSettings> ResolveProfilerExecution(
        SqlProfilerExecutionConfiguration configuration,
        SqlOptionsOverrides overrides)
    {
        var maxDegree = overrides.ProfilingMaxDegreeOfParallelism ?? configuration.MaxDegreeOfParallelism ?? SqlProfilerExecutionSettings.Default.MaxDegreeOfParallelism;
        if (maxDegree <= 0)
        {
            return ValidationError.Create("cli.sql.profiler.maxDegree.invalid", "Profiler max degree of parallelism must be positive.");
        }

        var batchSize = overrides.ProfilingBatchSize ?? configuration.TablesPerBatch ?? SqlProfilerExecutionSettings.Default.TablesPerBatch;
        if (batchSize <= 0)
        {
            return ValidationError.Create("cli.sql.profiler.batchSize.invalid", "Profiler batch size must be positive.");
        }

        var retryCount = overrides.ProfilingRetryCount ?? configuration.RetryCount ?? SqlProfilerExecutionSettings.Default.RetryCount;
        if (retryCount < 0)
        {
            return ValidationError.Create("cli.sql.profiler.retryCount.invalid", "Profiler retry count must be zero or greater.");
        }

        var baseDelaySeconds = overrides.ProfilingRetryBaseDelaySeconds ?? configuration.RetryBaseDelaySeconds ?? SqlProfilerExecutionSettings.Default.RetryBaseDelay.TotalSeconds;
        if (baseDelaySeconds < 0)
        {
            return ValidationError.Create("cli.sql.profiler.retryBaseDelay.invalid", "Profiler retry base delay must be non-negative.");
        }

        var jitterSeconds = overrides.ProfilingRetryJitterSeconds ?? configuration.RetryJitterSeconds ?? SqlProfilerExecutionSettings.Default.RetryJitter.TotalSeconds;
        if (jitterSeconds < 0)
        {
            return ValidationError.Create("cli.sql.profiler.retryJitter.invalid", "Profiler retry jitter must be non-negative.");
        }

        return new SqlProfilerExecutionSettings(
            maxDegree,
            batchSize,
            retryCount,
            TimeSpan.FromSeconds(baseDelaySeconds),
            TimeSpan.FromSeconds(jitterSeconds));
    }
}
