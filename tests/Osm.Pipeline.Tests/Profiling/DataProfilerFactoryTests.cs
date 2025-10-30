using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Json;
using Tests.Support;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class DataProfilerFactoryTests
{
    private static readonly OsmModel Model = ModelFixtures.LoadModel("model.edge-case.json");
    private static readonly ResolvedSqlOptions DefaultSqlOptions = new(
        ConnectionString: "Server=(local);Database=Sample;Trusted_Connection=True;",
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, null, null),
        MetadataContract: MetadataContractOverrides.Strict);

    [Fact]
    public void Create_sql_provider_returns_sql_profiler()
    {
        var connectionBuilder = new RecordingConnectionFactoryBuilder();
        var preflight = new StubSqlProfilerPreflight();
        var factory = new DataProfilerFactory(new ProfileSnapshotDeserializer(), connectionBuilder.Create, preflight);
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: "Server=tcp:example,1433;Database=OutSystems;",
            CommandTimeoutSeconds: 120,
            Sampling: new SqlSamplingSettings(10_000, 1_000),
            Authentication: new SqlAuthenticationSettings(
                SqlAuthenticationMethod.ActiveDirectoryPassword,
                TrustServerCertificate: true,
                ApplicationName: "Profiler",
                AccessToken: "token"),
            MetadataContract: MetadataContractOverrides.Strict);
        var request = CreateRequest("sql", profilePath: null, sqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsSuccess);
        Assert.IsType<SqlDataProfiler>(result.Value);
        Assert.Equal(sqlOptions.ConnectionString, connectionBuilder.LastConnectionString);
        Assert.NotNull(connectionBuilder.LastOptions);
        Assert.Equal(sqlOptions.Authentication.Method, connectionBuilder.LastOptions!.AuthenticationMethod);
        Assert.Equal(sqlOptions.Authentication.TrustServerCertificate, connectionBuilder.LastOptions.TrustServerCertificate);
        Assert.Equal(sqlOptions.Authentication.ApplicationName, connectionBuilder.LastOptions.ApplicationName);
        Assert.Equal(sqlOptions.Authentication.AccessToken, connectionBuilder.LastOptions.AccessToken);
        Assert.Single(preflight.Requests);
        Assert.Equal(sqlOptions.ConnectionString, preflight.Requests[0].ConnectionString);
    }

    [Fact]
    public void Create_fixture_provider_returns_fixture_profiler()
    {
        var factory = CreateFactory();
        var request = CreateRequest("fixture", profilePath: "profile.snapshot", DefaultSqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsSuccess);
        Assert.IsType<FixtureDataProfiler>(result.Value);
    }

    [Fact]
    public void Create_sql_provider_without_connection_string_returns_error()
    {
        var factory = CreateFactory();
        var sqlOptions = new ResolvedSqlOptions(
            ConnectionString: null,
            CommandTimeoutSeconds: null,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict);
        var request = CreateRequest("sql", profilePath: null, sqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.sql.connectionString.missing", error.Code);
    }

    [Fact]
    public void Create_fixture_provider_without_profile_path_returns_error()
    {
        var factory = CreateFactory();
        var request = CreateRequest("fixture", profilePath: null, DefaultSqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.profile.path.missing", error.Code);
    }

    [Fact]
    public void Create_unsupported_provider_returns_error()
    {
        var factory = CreateFactory();
        var request = CreateRequest("oracle", profilePath: null, DefaultSqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.profiler.unsupported", error.Code);
    }

    [Fact]
    public void Create_sql_provider_runs_preflight_failure()
    {
        var connectionBuilder = new RecordingConnectionFactoryBuilder();
        var preflight = new StubSqlProfilerPreflight
        {
            Handler = _ => Result<SqlProfilerPreflightResult>.Failure(ValidationError.Create(
                "pipeline.sqlProfiler.connection.failed",
                "Authentication failed."))
        };
        var factory = new DataProfilerFactory(new ProfileSnapshotDeserializer(), connectionBuilder.Create, preflight);
        var request = CreateRequest("sql", profilePath: null, DefaultSqlOptions);

        var result = factory.Create(request, Model);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.sqlProfiler.connection.failed", error.Code);
    }

    [Fact]
    public void Create_sql_provider_skips_preflight_when_requested()
    {
        var connectionBuilder = new RecordingConnectionFactoryBuilder();
        var preflight = new StubSqlProfilerPreflight();
        var factory = new DataProfilerFactory(new ProfileSnapshotDeserializer(), connectionBuilder.Create, preflight);
        var request = CreateRequest("sql", profilePath: null, DefaultSqlOptions, skipPreflight: true);

        var result = factory.Create(request, Model);

        Assert.True(result.IsSuccess);
        Assert.Empty(preflight.Requests);
    }

    private static IDataProfilerFactory CreateFactory()
    {
        return new DataProfilerFactory(
            new ProfileSnapshotDeserializer(),
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options),
            new StubSqlProfilerPreflight());
    }

    private static BuildSsdtPipelineRequest CreateRequest(
        string provider,
        string? profilePath,
        ResolvedSqlOptions sqlOptions,
        bool skipPreflight = false)
    {
        var request = new BuildSsdtPipelineRequest(
            ModelPath: FixtureFile.GetPath("model.edge-case.json"),
            ModuleFilter: ModuleFilterOptions.IncludeAll,
            OutputDirectory: "out",
            TighteningOptions: TighteningOptions.Default,
            SupplementalModels: SupplementalModelOptions.Default,
            ProfilerProvider: provider,
            ProfilePath: profilePath,
            SqlOptions: sqlOptions,
            SmoOptions: SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicy: TypeMappingPolicyLoader.LoadDefault(),
            EvidenceCache: null,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            SqlMetadataLog: null);

        if (skipPreflight)
        {
            request = request with { SkipProfilerPreflight = true };
        }

        return request;
    }

    private sealed class RecordingConnectionFactoryBuilder
    {
        public string? LastConnectionString { get; private set; }
        public SqlConnectionOptions? LastOptions { get; private set; }

        public IDbConnectionFactory Create(string connectionString, SqlConnectionOptions options)
        {
            LastConnectionString = connectionString;
            LastOptions = options;
            return new StubConnectionFactory();
        }

        private sealed class StubConnectionFactory : IDbConnectionFactory
        {
            public Task<System.Data.Common.DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
    }

    private sealed class StubSqlProfilerPreflight : ISqlProfilerPreflight
    {
        public List<SqlProfilerPreflightRequest> Requests { get; } = new();

        public Func<SqlProfilerPreflightRequest, Result<SqlProfilerPreflightResult>> Handler { get; set; }
            = _ => Result<SqlProfilerPreflightResult>.Success(SqlProfilerPreflightResult.Empty);

        public Result<SqlProfilerPreflightResult> Run(SqlProfilerPreflightRequest request)
        {
            Requests.Add(request);
            return Handler(request);
        }
    }
}
