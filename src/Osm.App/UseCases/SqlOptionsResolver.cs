using System;
using Osm.Domain.Abstractions;
using Osm.App.Configuration;
using Osm.Pipeline.Orchestration;

namespace Osm.App.UseCases;

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
        var maxParallel = overrides.MaxDegreeOfParallelism ?? configuration.Sql.MaxDegreeOfParallelism;
        if (maxParallel.HasValue && maxParallel.Value <= 0)
        {
            return ValidationError.Create("cli.sql.maxParallel.invalid", "Max degree of parallelism must be greater than zero.");
        }

        var tableBatchSize = overrides.TableBatchSize ?? configuration.Sql.TableBatchSize;
        if (tableBatchSize.HasValue && tableBatchSize.Value <= 0)
        {
            return ValidationError.Create("cli.sql.batchSize.invalid", "Profiling batch size must be greater than zero.");
        }

        var retryCount = overrides.RetryCount ?? configuration.Sql.RetryCount;
        if (retryCount.HasValue && retryCount.Value < 0)
        {
            return ValidationError.Create("cli.sql.retryCount.invalid", "Retry count must be zero or positive.");
        }

        var retryBaseMs = overrides.RetryBaseDelayMilliseconds ?? configuration.Sql.RetryBaseDelayMilliseconds;
        if (retryBaseMs.HasValue && retryBaseMs.Value < 0)
        {
            return ValidationError.Create("cli.sql.retryBase.invalid", "Retry base delay must be non-negative.");
        }

        var retryJitterMs = overrides.RetryJitterMilliseconds ?? configuration.Sql.RetryJitterMilliseconds;
        if (retryJitterMs.HasValue && retryJitterMs.Value < 0)
        {
            return ValidationError.Create("cli.sql.retryJitter.invalid", "Retry jitter must be non-negative.");
        }

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

        return new ResolvedSqlOptions(
            connection?.Trim(),
            timeout,
            new SqlSamplingSettings(sampling.RowSamplingThreshold, sampling.SampleSize),
            new SqlAuthenticationSettings(authentication.Method, authentication.TrustServerCertificate, authentication.ApplicationName, authentication.AccessToken),
            maxParallel,
            tableBatchSize,
            retryCount,
            retryBaseMs,
            retryJitterMs);
    }
}
