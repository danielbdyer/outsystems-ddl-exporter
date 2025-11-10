using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class BuildSsdtRequestAssemblerTests
{
    private static readonly ResolvedSqlOptions DefaultSqlOptions = new(
        ConnectionString: null,
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, null, null),
        MetadataContract: MetadataContractOverrides.Strict,
        ProfilingConnectionStrings: ImmutableArray<string>.Empty,
        TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty);

    [Fact]
    public void Assemble_PrefersOverrideProfilerProvider()
    {
        var assembler = new BuildSsdtRequestAssembler();
        var configuration = CreateConfiguration(
            profiler: new ProfilerConfiguration("config-provider", "config.profile", null));
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: "override.profile",
            OutputDirectory: null,
            ProfilerProvider: "override-provider",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var context = CreateContext(configuration, overrides, DefaultSqlOptions, modelPath: "model.json", outputDirectory: "out");

        var result = assembler.Assemble(context);
        Assert.True(result.IsSuccess);
        var assembly = result.Value;
        Assert.Equal("override-provider", assembly.ProfilerProvider);
        Assert.Equal("override.profile", assembly.ProfilePath);
        Assert.Equal("override-provider", assembly.Request.ProfilerProvider);
        Assert.Equal("override.profile", assembly.Request.Scope.ProfilePath);
    }

    [Fact]
    public void Assemble_ComposesCacheMetadata()
    {
        var assembler = new BuildSsdtRequestAssembler();
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration("  cache-root  ", true),
            profiler: new ProfilerConfiguration("fixture", "config.profile", null));
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: "override.profile",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };
        var moduleFilterResult = ModuleFilterOptions.Create(new[] { "AppCore", "Ops" }, includeSystemModules: false, includeInactiveModules: false);
        Assert.True(moduleFilterResult.IsSuccess);
        var moduleFilter = moduleFilterResult.Value;

        var context = CreateContext(
            configuration,
            overrides,
            sqlOptions,
            moduleFilter,
            modelPath: "model.json",
            outputDirectory: "out");

        var result = assembler.Assemble(context);
        Assert.True(result.IsSuccess);
        var cache = result.Value.Request.EvidenceCache;
        Assert.NotNull(cache);
        Assert.Equal("cache-root", cache!.RootDirectory);
        Assert.True(cache.Refresh);
        Assert.Equal("build-ssdt", cache.Command);
        Assert.Equal("model.json", cache.ModelPath);
        Assert.Equal("override.profile", cache.ProfilePath);
        Assert.NotNull(cache.Metadata);
        Assert.Equal("filtered", cache.Metadata!["moduleFilter.selectionScope"]);
        var connectionHash = cache.Metadata["sql.connectionHash"];
        Assert.False(string.IsNullOrWhiteSpace(connectionHash));
        Assert.Equal(64, connectionHash!.Length);
    }

    [Fact]
    public void Assemble_UsesProvidedStaticDataProvider()
    {
        var assembler = new BuildSsdtRequestAssembler();
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: "override.profile",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var provider = new StubStaticEntityDataProvider();

        var context = CreateContext(
            configuration,
            overrides,
            DefaultSqlOptions,
            modelPath: "model.json",
            outputDirectory: "out",
            staticDataProvider: provider);

        var result = assembler.Assemble(context);

        Assert.True(result.IsSuccess);
        Assert.Same(provider, result.Value.Request.StaticDataProvider);
    }

    private static BuildSsdtRequestAssemblerContext CreateContext(
        CliConfiguration configuration,
        BuildSsdtOverrides overrides,
        ResolvedSqlOptions sqlOptions,
        ModuleFilterOptions? moduleFilter = null,
        string modelPath = "model.json",
        string outputDirectory = "out",
        DynamicEntityDataset? dynamicDataset = null,
        DynamicDatasetSource datasetSource = DynamicDatasetSource.None,
        IStaticEntityDataProvider? staticDataProvider = null,
        SqlMetadataLog? sqlMetadataLog = null)
    {
        var filter = moduleFilter ?? ModuleFilterOptions.IncludeAll;
        dynamicDataset ??= DynamicEntityDataset.Empty;
        return new BuildSsdtRequestAssemblerContext(
            configuration,
            overrides,
            filter,
            sqlOptions,
            configuration.Tightening,
            TypeMappingPolicy.Default,
            SmoBuildOptions.FromEmission(configuration.Tightening.Emission),
            modelPath,
            outputDirectory,
            dynamicDataset,
            datasetSource,
            staticDataProvider,
            new CacheOptionsOverrides(null, null),
            "config.json",
            sqlMetadataLog,
            null,
            ImmutableArray<string>.Empty);
    }

    private static CliConfiguration CreateConfiguration(
        CacheConfiguration? cache = null,
        ProfilerConfiguration? profiler = null)
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            cache ?? CacheConfiguration.Empty,
            profiler ?? ProfilerConfiguration.Empty,
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty,
            UatUsersConfiguration.Empty);
    }

    private sealed class StubStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(Array.Empty<StaticEntityTableData>()));
        }
    }
}
