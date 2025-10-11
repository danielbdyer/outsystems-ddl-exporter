using System;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Profiling;

public sealed class DefaultProfilerFactory : IProfilerFactory
{
    private readonly IProfileSnapshotDeserializer _deserializer;

    public DefaultProfilerFactory(IProfileSnapshotDeserializer deserializer)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
    }

    public Result<IDataProfiler> Create(BuildSsdtPipelineRequest request, OsmModel model)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.Equals(request.ProfilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.SqlOptions.ConnectionString))
            {
                return Result<IDataProfiler>.Failure(ValidationError.Create(
                    "pipeline.buildSsdt.sql.connectionString.missing",
                    "Connection string is required when using the SQL profiler."));
            }

            var sampling = CreateSamplingOptions(request.SqlOptions.Sampling);
            var connectionOptions = CreateConnectionOptions(request.SqlOptions.Authentication);
            var profilerOptions = new SqlProfilerOptions(request.SqlOptions.CommandTimeoutSeconds, sampling);
            var profiler = new SqlDataProfiler(
                new SqlConnectionFactory(request.SqlOptions.ConnectionString!, connectionOptions),
                model,
                profilerOptions);

            return Result<IDataProfiler>.Success(profiler);
        }

        if (string.Equals(request.ProfilerProvider, "fixture", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.ProfilePath))
            {
                return Result<IDataProfiler>.Failure(ValidationError.Create(
                    "pipeline.buildSsdt.profile.path.missing",
                    "Profile path is required when using the fixture profiler."));
            }

            var profiler = new FixtureDataProfiler(request.ProfilePath!, _deserializer);
            return Result<IDataProfiler>.Success(profiler);
        }

        return Result<IDataProfiler>.Failure(ValidationError.Create(
            "pipeline.buildSsdt.profiler.unsupported",
            $"Profiler provider '{request.ProfilerProvider}' is not supported."));
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
