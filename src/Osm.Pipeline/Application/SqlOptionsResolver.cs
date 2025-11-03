using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Application;

internal static class SqlOptionsResolver
{
    public static Result<ResolvedSqlOptions> Resolve(CliConfiguration configuration, SqlOptionsOverrides overrides)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        overrides ??= new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null);

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

        var metadataContract = configuration.Sql.MetadataContract ?? MetadataContractConfiguration.Empty;
        var contractOverrides = MetadataContractOverrides.Strict;
        foreach (var pair in metadataContract.OptionalColumns)
        {
            contractOverrides = contractOverrides.WithOptionalColumns(pair.Key, pair.Value);
        }

        ImmutableArray<string> profilingConnections;
        if (overrides.ProfilingConnectionStrings is { } overrideProfiling)
        {
            profilingConnections = overrideProfiling
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }
        else
        {
            profilingConnections = configuration.Sql.ProfilingConnectionStrings
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        return new ResolvedSqlOptions(
            connection?.Trim(),
            timeout,
            new SqlSamplingSettings(sampling.RowSamplingThreshold, sampling.SampleSize),
            new SqlAuthenticationSettings(authentication.Method, authentication.TrustServerCertificate, authentication.ApplicationName, authentication.AccessToken),
            contractOverrides,
            profilingConnections);
    }
}
