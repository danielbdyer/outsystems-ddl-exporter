using System;
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
        if (string.IsNullOrWhiteSpace(request.SqlOptions.ConnectionString))
        {
            return Result<IDataProfiler>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.sql.connectionString.missing",
                "Connection string is required when using the SQL profiler."));
        }

        var sampling = CreateSamplingOptions(request.SqlOptions.Sampling);
        var connectionOptions = CreateConnectionOptions(request.SqlOptions.Authentication);
        var profilerOptions = SqlProfilerOptions.Default with
        {
            CommandTimeoutSeconds = request.SqlOptions.CommandTimeoutSeconds,
            Sampling = sampling,
            NamingOverrides = request.SmoOptions.NamingOverrides
        };

        var connectionFactory = _connectionFactoryFactory(request.SqlOptions.ConnectionString!, connectionOptions);
        var profiler = new SqlDataProfiler(connectionFactory, model, profilerOptions);
        return Result<IDataProfiler>.Success(profiler);
    }

    private Result<IDataProfiler> CreateFixtureProfiler(BuildSsdtPipelineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfilePath))
        {
            return Result<IDataProfiler>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.profile.path.missing",
                "Profile path is required when using the fixture profiler."));
        }

        var profiler = new FixtureDataProfiler(request.ProfilePath!, _profileSnapshotDeserializer);
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
}
