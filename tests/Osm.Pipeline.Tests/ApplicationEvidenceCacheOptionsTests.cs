using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ApplicationEvidenceCacheOptionsTests
{
    private static readonly SqlOptionsOverrides DefaultSqlOverrides = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    [Fact]
    public async Task BuildSsdt_UsesConfigurationCacheOptions_WhenOverridesMissing()
    {
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration("  config-root  ", true),
            profiler: new ProfilerConfiguration("fixture", "config-profile.snapshot", null));
        var context = new CliConfigurationContext(configuration, "config.json");
        var dispatcher = new RecordingDispatcher();
        var service = CreateBuildService(dispatcher);

        var overrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var moduleFilter = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        await service.RunAsync(new BuildSsdtApplicationInput(
            context,
            overrides,
            moduleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var request = dispatcher.BuildRequest;
        Assert.NotNull(request);
        var cache = request!.EvidenceCache;
        Assert.NotNull(cache);
        Assert.Equal("config-root", cache!.RootDirectory);
        Assert.True(cache.Refresh);
        Assert.Equal("build-ssdt", cache.Command);
        Assert.Equal("model.json", cache.ModelPath);
        Assert.Equal("profile.snapshot", cache.ProfilePath);
        Assert.Null(cache.DmmPath);
        Assert.Equal("config.json", cache.ConfigPath);
        Assert.NotNull(cache.Metadata);
        Assert.Equal(Path.GetFullPath("model.json"), cache.Metadata!["inputs.model"]);
        Assert.Equal(Path.GetFullPath("profile.snapshot"), cache.Metadata["inputs.profile"]);
        Assert.Equal("all", cache.Metadata["moduleFilter.selectionScope"]);
    }

    [Fact]
    public async Task BuildSsdt_UsesOverrideCacheOptions_WhenProvided()
    {
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration(null, null),
            profiler: new ProfilerConfiguration("fixture", null, null));
        var context = new CliConfigurationContext(configuration, "config.json");
        var dispatcher = new RecordingDispatcher();
        var service = CreateBuildService(dispatcher);

        var overrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var moduleFilter = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        await service.RunAsync(new BuildSsdtApplicationInput(
            context,
            overrides,
            moduleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(" ./override ", true)));

        var request = dispatcher.BuildRequest;
        Assert.NotNull(request);
        var cache = request!.EvidenceCache;
        Assert.NotNull(cache);
        Assert.Equal("./override", cache!.RootDirectory);
        Assert.True(cache.Refresh);
        Assert.Equal("build-ssdt", cache.Command);
    }

    [Fact]
    public async Task BuildSsdt_SkipsEvidenceCache_WhenRootMissing()
    {
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration(null, false),
            profiler: new ProfilerConfiguration("fixture", null, null));
        var context = new CliConfigurationContext(configuration, "config.json");
        var dispatcher = new RecordingDispatcher();
        var service = CreateBuildService(dispatcher);

        var overrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var moduleFilter = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        await service.RunAsync(new BuildSsdtApplicationInput(
            context,
            overrides,
            moduleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var request = dispatcher.BuildRequest;
        Assert.NotNull(request);
        Assert.Null(request!.EvidenceCache);
    }

    [Fact]
    public async Task DmmCompare_UsesConfigurationCacheOptions_WhenOverridesMissing()
    {
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration("cache-root", false),
            profiler: new ProfilerConfiguration("fixture", null, null),
            dmmPath: "config.dmm");
        var context = new CliConfigurationContext(configuration, "config.json");
        var dispatcher = new RecordingDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher);

        var overrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: "out",
            MaxDegreeOfParallelism: 8);

        var moduleFilter = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            moduleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var request = dispatcher.CompareRequest;
        Assert.NotNull(request);
        var cache = request!.EvidenceCache;
        Assert.NotNull(cache);
        Assert.Equal("cache-root", cache!.RootDirectory);
        Assert.False(cache.Refresh);
        Assert.Equal("dmm-compare", cache.Command);
        Assert.Equal("baseline.dmm", cache.DmmPath);
        Assert.Equal(Path.GetFullPath("baseline.dmm"), cache.Metadata!["inputs.dmm"]);
    }

    [Fact]
    public async Task DmmCompare_UsesOverrideCacheOptions_WhenProvided()
    {
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration("config-root", false),
            profiler: new ProfilerConfiguration("fixture", null, null));
        var context = new CliConfigurationContext(configuration, "config.json");
        var dispatcher = new RecordingDispatcher();
        var service = new CompareWithDmmApplicationService(dispatcher);

        var overrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: "out",
            MaxDegreeOfParallelism: null);

        var moduleFilter = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        await service.RunAsync(new CompareWithDmmApplicationInput(
            context,
            overrides,
            moduleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides("overridden-root", true)));

        var request = dispatcher.CompareRequest;
        Assert.NotNull(request);
        var cache = request!.EvidenceCache;
        Assert.NotNull(cache);
        Assert.Equal("overridden-root", cache!.RootDirectory);
        Assert.True(cache.Refresh);
        Assert.Equal("dmm-compare", cache.Command);
    }

    private static CliConfiguration CreateConfiguration(
        CacheConfiguration cache,
        ProfilerConfiguration profiler,
        string? dmmPath = null)
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: dmmPath,
            cache,
            profiler,
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
    }

    private static BuildSsdtApplicationService CreateBuildService(RecordingDispatcher dispatcher)
    {
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService();
        var outputDirectoryResolver = new OutputDirectoryResolver();
        var namingOverridesBinder = new NamingOverridesBinder();
        var staticDataProviderFactory = new StaticDataProviderFactory();
        return new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputDirectoryResolver,
            namingOverridesBinder,
            staticDataProviderFactory);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public BuildSsdtPipelineRequest? BuildRequest { get; private set; }

        public DmmComparePipelineRequest? CompareRequest { get; private set; }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            switch (command)
            {
                case BuildSsdtPipelineRequest build:
                    BuildRequest = build;
                    break;
                case DmmComparePipelineRequest compare:
                    CompareRequest = compare;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
            }

            return Task.FromResult(Result<TResponse>.Failure(ValidationError.Create("test.dispatch", "stub failure")));
        }
    }

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        public Task<Result<ModelResolutionResult>> ResolveModelAsync(
            CliConfiguration configuration,
            BuildSsdtOverrides overrides,
            ModuleFilterOptions moduleFilter,
            ResolvedSqlOptions sqlOptions,
            string outputDirectory,
            SqlMetadataLog? sqlMetadataLog,
            CancellationToken cancellationToken)
        {
            var path = overrides.ModelPath ?? configuration.ModelPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Tests must provide a model path override.");
            }

            var result = new ModelResolutionResult(path!, false, ImmutableArray<string>.Empty);
            return Task.FromResult(Result<ModelResolutionResult>.Success(result));
        }
    }
}
