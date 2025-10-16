using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
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
        Authentication: new SqlAuthenticationSettings(null, null, null, null));

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
            MaxDegreeOfParallelism: null);

        var context = CreateContext(configuration, overrides, DefaultSqlOptions, modelPath: "model.json", outputDirectory: "out");

        var result = assembler.Assemble(context);
        Assert.True(result.IsSuccess);
        var assembly = result.Value;
        Assert.Equal("override-provider", assembly.ProfilerProvider);
        Assert.Equal("override.profile", assembly.ProfilePath);
        Assert.Equal("override-provider", assembly.Request.ProfilerProvider);
        Assert.Equal("override.profile", assembly.Request.ProfilePath);
    }

    [Fact]
    public void Assemble_ComposesCacheMetadata()
    {
        var assembler = new BuildSsdtRequestAssembler();
        var sqlConfiguration = new SqlConfiguration(
            "Server=.;Database=Osm;",
            CommandTimeoutSeconds: null,
            SqlSamplingConfiguration.Empty,
            SqlAuthenticationConfiguration.Empty);
        var configuration = CreateConfiguration(
            cache: new CacheConfiguration("  cache-root  ", true),
            profiler: new ProfilerConfiguration("fixture", "config.profile", null),
            sql: sqlConfiguration);
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: "override.profile",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);

        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };
        var moduleFilterResult = ModuleFilterOptions.Create(new[] { "AppCore", "Ops" }, includeSystemModules: false, includeInactiveModules: false);
        Assert.True(moduleFilterResult.IsSuccess);
        var moduleFilter = moduleFilterResult.Value;
        Assert.False(moduleFilter.Modules.IsDefaultOrEmpty);

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
        Assert.True(cache.Metadata!.TryGetValue("moduleFilter.selectionScope", out var selectionScope));
        Assert.Equal("filtered", selectionScope);
        Assert.True(cache.Metadata.TryGetValue("moduleFilter.modules", out var moduleList));
        Assert.Equal("AppCore,Ops", moduleList);
        Assert.True(cache.Metadata.TryGetValue("moduleFilter.moduleCount", out var moduleCount));
        Assert.Equal("2", moduleCount);
        Assert.True(cache.Metadata.TryGetValue("moduleFilter.modulesHash", out var moduleHash));
        Assert.False(string.IsNullOrWhiteSpace(moduleHash));
        Assert.Equal(64, moduleHash!.Length);
        Assert.True(cache.Metadata.TryGetValue("inputs.model", out var modelInput));
        Assert.Equal(Path.GetFullPath("model.json"), modelInput);
        Assert.True(cache.Metadata.TryGetValue("inputs.profile", out var profileInput));
        Assert.Equal(Path.GetFullPath("override.profile"), profileInput);
        Assert.True(cache.Metadata.TryGetValue("cache.root", out var cacheRoot));
        Assert.Equal(Path.GetFullPath("  cache-root  "), cacheRoot);
        Assert.True(cache.Metadata.TryGetValue("cache.refreshRequested", out var refreshRequested));
        Assert.Equal(bool.TrueString, refreshRequested);
        Assert.True(cache.Metadata.TryGetValue("sql.connectionHash", out var connectionHash));
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
            MaxDegreeOfParallelism: null);
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
        IStaticEntityDataProvider? staticDataProvider = null)
    {
        var filter = moduleFilter ?? ModuleFilterOptions.IncludeAll;
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
            staticDataProvider,
            new CacheOptionsOverrides(null, null),
            "config.json");
    }

    private static CliConfiguration CreateConfiguration(
        CacheConfiguration? cache = null,
        ProfilerConfiguration? profiler = null,
        SqlConfiguration? sql = null)
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            cache ?? CacheConfiguration.Empty,
            profiler ?? ProfilerConfiguration.Empty,
            sql ?? SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
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
