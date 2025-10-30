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
    private readonly ISqlProfilerPreflight _preflight;

    public DataProfilerFactory(
        IProfileSnapshotDeserializer profileSnapshotDeserializer,
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory,
        ISqlProfilerPreflight preflight)
    {
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? throw new ArgumentNullException(nameof(profileSnapshotDeserializer));
        _connectionFactoryFactory = connectionFactoryFactory ?? throw new ArgumentNullException(nameof(connectionFactoryFactory));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
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

        var connectionOptions = SqlProfilerOptionFactory.CreateConnectionOptions(request.SqlOptions.Authentication);
        var profilerOptions = SqlProfilerOptionFactory.CreateProfilerOptions(request.SqlOptions, request.SmoOptions);

        if (!request.SkipProfilerPreflight && request.ProfilerPreflight is null)
        {
            var preflightRequest = new SqlProfilerPreflightRequest(
                request.SqlOptions.ConnectionString!,
                connectionOptions,
                profilerOptions);
            var preflightResult = _preflight.Run(preflightRequest);
            if (preflightResult.IsFailure)
            {
                return Result<IDataProfiler>.Failure(preflightResult.Errors);
            }
        }

        var connectionFactory = _connectionFactoryFactory(request.SqlOptions.ConnectionString!, connectionOptions);
        var profiler = new SqlDataProfiler(connectionFactory, model, profilerOptions, request.SqlMetadataLog);
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

}
