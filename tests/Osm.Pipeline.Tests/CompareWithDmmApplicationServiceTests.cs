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
using Tests.Support;

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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunAsync_WhenMaxDegreeOfParallelismNotPositive_ReturnsParallelismError(int parallelism)
    {
        var dispatcher = new CapturingDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher);
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

    [Fact]
    public async Task RunAsync_ComposesPipelineRequestWithCacheOptions()
    {
        using var temp = new TempDirectory();
        var cacheRoot = Path.Combine(temp.Path, "cache");
        var outputDirectory = Path.Combine(temp.Path, "out");
        var modelPath = Path.Combine(temp.Path, "model.json");
        var profilePath = Path.Combine(temp.Path, "profile.snapshot");
        var dmmPath = Path.Combine(temp.Path, "baseline.dmm");
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(modelPath, "{}");
        File.WriteAllText(profilePath, "{}");
        File.WriteAllText(dmmPath, "{}");

        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: modelPath,
            ProfilePath: profilePath,
            DmmPath: dmmPath,
            new CacheConfiguration(cacheRoot, Refresh: false),
            new ProfilerConfiguration("fixture", profilePath, null),
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);

        var context = new CliConfigurationContext(configuration, "config.json");
        var overrides = new CompareWithDmmOverrides(null, null, null, outputDirectory, MaxDegreeOfParallelism: null);
        var dispatcher = new CapturingDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher);

        var result = await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            DefaultModuleFilter,
            DefaultSqlOverrides,
            DefaultCacheOverrides));

        Assert.True(result.IsSuccess);
        Assert.True(dispatcher.WasInvoked);
        var request = dispatcher.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(modelPath, request!.ModelPath);
        Assert.Equal(profilePath, request.ProfilePath);
        Assert.Equal(dmmPath, request.DmmPath);
        Assert.Equal(Path.Combine(outputDirectory, "dmm-diff.json"), request.DiffOutputPath);
        Assert.NotNull(request.EvidenceCache);
        Assert.Equal("dmm-compare", request.EvidenceCache!.Command);
        Assert.Equal(cacheRoot, request.EvidenceCache.RootDirectory);
        Assert.Equal(result.Value.PipelineResult.DiffArtifactPath, result.Value.DiffOutputPath);
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

    private static DmmComparePipelineResult CreatePipelineResult(string diffPath)
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
            DiffArtifactPath: diffPath,
            EvidenceCache: null,
            ExecutionLog: PipelineExecutionLog.Empty,
            Warnings: ImmutableArray<string>.Empty);
    }

    private sealed class CapturingDispatcher : ICommandDispatcher
    {
        public bool WasInvoked { get; private set; }

        public DmmComparePipelineRequest? LastRequest { get; private set; }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            WasInvoked = true;

            if (command is DmmComparePipelineRequest request)
            {
                LastRequest = request;
                var pipelineResult = CreatePipelineResult(request.DiffOutputPath);
                return Task.FromResult(Result<TResponse>.Success((TResponse)(object)pipelineResult));
            }

            throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
        }
    }
}
