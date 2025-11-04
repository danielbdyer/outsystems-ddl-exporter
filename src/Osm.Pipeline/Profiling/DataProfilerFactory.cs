using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed class DataProfilerFactory : IDataProfilerFactory
{
    private readonly IProfileSnapshotDeserializer _profileSnapshotDeserializer;
    private readonly Func<string, SqlConnectionOptions, IDbConnectionFactory> _connectionFactoryFactory;

    public DataProfilerFactory(
        IProfileSnapshotDeserializer profileSnapshotDeserializer,
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory)
    {
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? throw new ArgumentNullException(nameof(profileSnapshotDeserializer));
        _connectionFactoryFactory = connectionFactoryFactory ?? throw new ArgumentNullException(nameof(connectionFactoryFactory));
    }

    public Result<IDataProfiler> Create(BuildSsdtPipelineRequest request, OsmModel model)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var provider = request.ProfilerProvider;
        if (string.Equals(provider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            return CreateSqlProfiler(request, model);
        }

        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "fixture", StringComparison.OrdinalIgnoreCase))
        {
            return CreateFixtureProfiler(request);
        }

        return Result<IDataProfiler>.Failure(ValidationError.Create(
            "pipeline.buildSsdt.profiler.unsupported",
            $"Profiler provider '{provider}' is not supported. Supported providers: fixture, sql."));
    }

    private Result<IDataProfiler> CreateSqlProfiler(BuildSsdtPipelineRequest request, OsmModel model)
    {
        if (string.IsNullOrWhiteSpace(request.Scope.SqlOptions.ConnectionString))
        {
            return Result<IDataProfiler>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.sql.connectionString.missing",
                "Connection string is required when using the SQL profiler."));
        }

        var sampling = CreateSamplingOptions(request.Scope.SqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(request.Scope.SqlOptions.Authentication);

        // Primary profiler uses strict mode (AllowMissingTables=false)
        // Fails fast if any table doesn't exist in the database
        var primaryProfilerOptions = SqlProfilerOptions.Default with
        {
            CommandTimeoutSeconds = request.Scope.SqlOptions.CommandTimeoutSeconds,
            Sampling = sampling,
            NamingOverrides = request.Scope.SmoOptions.NamingOverrides,
            AllowMissingTables = false
        };

        // Secondary profilers use lenient mode (AllowMissingTables=true)
        // Gracefully skips missing tables to handle environment drift
        var tableNameMappings = request.Scope.SqlOptions.TableNameMappings
            .Select(static config => new Sql.TableNameMapping(
                config.SourceSchema,
                config.SourceTable,
                config.TargetSchema,
                config.TargetTable))
            .ToImmutableArray();

        var secondaryProfilerOptions = SqlProfilerOptions.Default with
        {
            CommandTimeoutSeconds = request.Scope.SqlOptions.CommandTimeoutSeconds,
            Sampling = sampling,
            NamingOverrides = request.Scope.SmoOptions.NamingOverrides,
            AllowMissingTables = true,
            TableNameMappings = tableNameMappings
        };

        var allocator = new EnvironmentLabelAllocator();

        var primaryEntry = ParseEnvironmentEntry(
            request.Scope.SqlOptions.ConnectionString!,
            defaultLabel: "Primary");

        var primaryLabel = allocator.Allocate(primaryEntry.Label, out var primaryAdjusted);

        var primaryConnectionFactory = _connectionFactoryFactory(primaryEntry.ConnectionString, connectionOptions);
        var primaryProfiler = new SqlDataProfiler(primaryConnectionFactory, model, primaryProfilerOptions, request.SqlMetadataLog);
        var primaryEnvironment = new MultiTargetSqlDataProfiler.ProfilerEnvironment(
            primaryLabel,
            primaryProfiler,
            isPrimary: true,
            primaryEntry.LabelOrigin,
            primaryAdjusted);

        var secondaryEnvironments = ImmutableArray.CreateBuilder<MultiTargetSqlDataProfiler.ProfilerEnvironment>();
        var index = 1;

        foreach (var entry in request.Scope.SqlOptions.ProfilingConnectionStrings
                     .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var defaultLabel = $"Secondary #{index}";
            var parsed = ParseEnvironmentEntry(entry!, defaultLabel);
            var label = allocator.Allocate(parsed.Label, out var adjusted);
            var factory = _connectionFactoryFactory(parsed.ConnectionString, connectionOptions);
            var environmentProfiler = new SqlDataProfiler(factory, model, secondaryProfilerOptions, request.SqlMetadataLog);
            secondaryEnvironments.Add(new MultiTargetSqlDataProfiler.ProfilerEnvironment(
                label,
                environmentProfiler,
                isPrimary: false,
                parsed.LabelOrigin,
                adjusted));
            index++;
        }

        if (secondaryEnvironments.Count == 0)
        {
            return Result<IDataProfiler>.Success(primaryProfiler);
        }

        var multiEnvironmentProfiler = new MultiTargetSqlDataProfiler(primaryEnvironment, secondaryEnvironments.ToImmutable());
        return Result<IDataProfiler>.Success(multiEnvironmentProfiler);
    }

    private Result<IDataProfiler> CreateFixtureProfiler(BuildSsdtPipelineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Scope.ProfilePath))
        {
            return Result<IDataProfiler>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.profile.path.missing",
                "Profile path is required when using the fixture profiler."));
        }

        var profiler = new FixtureDataProfiler(request.Scope.ProfilePath!, _profileSnapshotDeserializer);
        return Result<IDataProfiler>.Success(profiler);
    }

    private static SqlSamplingOptions CreateSamplingOptions(SqlSamplingSettings configuration)
    {
        var threshold = configuration.RowSamplingThreshold ?? SqlSamplingOptions.Default.RowCountSamplingThreshold;
        var sampleSize = configuration.SampleSize ?? SqlSamplingOptions.Default.SampleSize;
        return new SqlSamplingOptions(threshold, sampleSize);
    }

    private static SqlConnectionOptions CreateConnectionOptions(SqlAuthenticationSettings configuration)
    {
        return new SqlConnectionOptions(
            configuration.Method,
            configuration.TrustServerCertificate,
            configuration.ApplicationName,
            configuration.AccessToken);
    }

    private static ParsedEnvironmentEntry ParseEnvironmentEntry(string value, string defaultLabel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(value));
        }

        const string Separator = "::";
        var trimmed = value.Trim();
        var separatorIndex = trimmed.IndexOf(Separator, StringComparison.Ordinal);

        string? label = null;
        string connection = trimmed;

        if (separatorIndex >= 0)
        {
            label = trimmed[..separatorIndex].Trim();
            connection = trimmed[(separatorIndex + Separator.Length)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(value));
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            return new ParsedEnvironmentEntry(
                label.Trim(),
                connection,
                MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Provided);
        }

        var (generatedLabel, origin) = BuildDefaultLabel(connection, defaultLabel);
        return new ParsedEnvironmentEntry(generatedLabel, connection, origin);
    }

    private static (string Label, MultiTargetSqlDataProfiler.EnvironmentLabelOrigin Origin) BuildDefaultLabel(
        string connectionString,
        string baseLabel)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                return ($"{baseLabel} ({builder.InitialCatalog})", MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromDatabase);
            }

            if (!string.IsNullOrWhiteSpace(builder.ApplicationName))
            {
                return ($"{baseLabel} ({builder.ApplicationName})", MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromApplicationName);
            }

            if (!string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return ($"{baseLabel} ({builder.DataSource})", MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.DerivedFromDataSource);
            }
        }
        catch (ArgumentException)
        {
            // Ignore malformed connection strings and fall back to the base label.
        }

        return (baseLabel, MultiTargetSqlDataProfiler.EnvironmentLabelOrigin.Fallback);
    }

    private readonly record struct ParsedEnvironmentEntry(
        string Label,
        string ConnectionString,
        MultiTargetSqlDataProfiler.EnvironmentLabelOrigin LabelOrigin);

    private sealed class EnvironmentLabelAllocator
    {
        private readonly Dictionary<string, int> _labelCounts = new(StringComparer.OrdinalIgnoreCase);

        public string Allocate(string label, out bool adjusted)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                adjusted = false;
                return label;
            }

            var trimmed = label.Trim();

            if (!_labelCounts.TryGetValue(trimmed, out var count))
            {
                _labelCounts[trimmed] = 1;
                adjusted = false;
                return trimmed;
            }

            adjusted = true;
            count++;
            string candidate;

            do
            {
                candidate = $"{trimmed} #{count}";
                count++;
            }
            while (_labelCounts.ContainsKey(candidate));

            _labelCounts[trimmed] = count;
            _labelCounts[candidate] = 1;
            return candidate;
        }
    }
}
