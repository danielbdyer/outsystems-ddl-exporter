using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Tests;

public sealed class CompareWithDmmApplicationServiceTests
{
    private static readonly ModuleFilterOverrides DefaultModuleFilter = new(
        Array.Empty<string>(),
        IncludeSystemModules: null,
        IncludeInactiveModules: null,
        AllowMissingPrimaryKey: Array.Empty<string>(),
        AllowMissingSchema: Array.Empty<string>());

    private static readonly SqlOptionsOverrides DefaultSqlOverrides = new(
        ConnectionString: null,
        CommandTimeoutSeconds: null,
        SamplingThreshold: null,
        SamplingSize: null,
        AuthenticationMethod: null,
        TrustServerCertificate: null,
        ApplicationName: null,
        AccessToken: null);

    private static readonly CacheOptionsOverrides DefaultCacheOverrides = new(
        Root: null,
        Refresh: null);

    [Fact]
    public async Task RunAsync_WhenModelPathMissing_ReturnsModelMissingError()
    {
        var dispatcher = new SuccessfulDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher, new PipelineRequestContextFactory());
        var context = CreateContext(modelPath: null, profilePath: null, profilerProfilePath: null, dmmPath: "baseline.dmm");
        var overrides = new CompareWithDmmOverrides(
            ModelPath: null,
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: CreateTemporaryDirectory(),
            MaxDegreeOfParallelism: null);

        var result = await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            DefaultModuleFilter,
            DefaultSqlOverrides,
            DefaultCacheOverrides));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.dmmCompare.model.missing", error.Code);
        Assert.False(dispatcher.WasInvoked);
    }

    [Fact]
    public async Task RunAsync_WhenProfilePathMissing_ReturnsProfileMissingError()
    {
        var dispatcher = new SuccessfulDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher, new PipelineRequestContextFactory());
        var context = CreateContext(modelPath: "model.json", profilePath: null, profilerProfilePath: null, dmmPath: "baseline.dmm");
        var overrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: null,
            DmmPath: "baseline.dmm",
            OutputDirectory: CreateTemporaryDirectory(),
            MaxDegreeOfParallelism: null);

        var result = await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            DefaultModuleFilter,
            DefaultSqlOverrides,
            DefaultCacheOverrides));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.dmmCompare.profile.missing", error.Code);
        Assert.False(dispatcher.WasInvoked);
    }

    [Fact]
    public async Task RunAsync_WhenDmmPathMissing_ReturnsDmmMissingError()
    {
        var dispatcher = new SuccessfulDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher, new PipelineRequestContextFactory());
        var context = CreateContext(modelPath: "model.json", profilePath: "profile.snapshot", profilerProfilePath: null, dmmPath: null);
        var overrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: null,
            OutputDirectory: CreateTemporaryDirectory(),
            MaxDegreeOfParallelism: null);

        var result = await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            DefaultModuleFilter,
            DefaultSqlOverrides,
            DefaultCacheOverrides));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.dmmCompare.dmm.missing", error.Code);
        Assert.False(dispatcher.WasInvoked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunAsync_WhenMaxDegreeOfParallelismNotPositive_ReturnsParallelismError(int parallelism)
    {
        var dispatcher = new SuccessfulDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher, new PipelineRequestContextFactory());
        var context = CreateContext(modelPath: "model.json", profilePath: "profile.snapshot", profilerProfilePath: null, dmmPath: "baseline.dmm");
        var overrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: CreateTemporaryDirectory(),
            MaxDegreeOfParallelism: parallelism);

        var result = await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            DefaultModuleFilter,
            DefaultSqlOverrides,
            DefaultCacheOverrides));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("cli.dmmCompare.parallelism.invalid", error.Code);
        Assert.False(dispatcher.WasInvoked);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "osm-pipeline-tests", Guid.NewGuid().ToString("N"));
        return path;
    }

    private static CliConfigurationContext CreateContext(
        string? modelPath,
        string? profilePath,
        string? profilerProfilePath,
        string? dmmPath)
    {
        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: modelPath,
            ProfilePath: profilePath,
            DmmPath: dmmPath,
            CacheConfiguration.Empty,
            new ProfilerConfiguration("fixture", profilerProfilePath, null),
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);

        return new CliConfigurationContext(configuration, "config.json");
    }

    private static DmmComparePipelineResult CreatePipelineResult()
    {
        var profileResult = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>());

        if (profileResult.IsFailure)
        {
            throw new InvalidOperationException("Profile snapshot creation should not fail for empty inputs.");
        }

        var comparison = new DmmComparisonResult(
            IsMatch: true,
            ModelDifferences: Array.Empty<DmmDifference>(),
            SsdtDifferences: Array.Empty<DmmDifference>());

        return new DmmComparePipelineResult(
            profileResult.Value,
            comparison,
            DiffArtifactPath: "diff.json",
            EvidenceCache: null,
            ExecutionLog: PipelineExecutionLog.Empty,
            Warnings: ImmutableArray<string>.Empty);
    }

    private sealed class SuccessfulDispatcher : ICommandDispatcher
    {
        public bool WasInvoked { get; private set; }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            WasInvoked = true;

            if (command is DmmComparePipelineRequest)
            {
                var pipelineResult = CreatePipelineResult();
                return Task.FromResult(Result<TResponse>.Success((TResponse)(object)pipelineResult));
            }

            throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
        }
    }
}
