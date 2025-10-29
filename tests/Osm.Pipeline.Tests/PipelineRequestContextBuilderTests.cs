using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Smo;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class PipelineRequestContextBuilderTests
{
    [Fact]
    public void Build_ComposesResolvedContext()
    {
        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            DmmPath: "baseline.dmm",
            new CacheConfiguration("cache-root", Refresh: true),
            new ProfilerConfiguration("fixture", "profile.json", null),
            new SqlConfiguration(
                ConnectionString: "Server=(local);Database=OSM;",
                CommandTimeoutSeconds: 45,
                SqlSamplingConfiguration.Empty,
                SqlAuthenticationConfiguration.Empty,
                MetadataContractConfiguration.Empty),
            new ModuleFilterConfiguration(
                new[] { "CRM" },
                IncludeSystemModules: true,
                IncludeInactiveModules: false,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase)),
            TypeMappingConfiguration.Empty,
            new SupplementalModelConfiguration(includeUsers: true, Paths: new[] { "users.json" }));

        var configurationContext = new CliConfigurationContext(configuration, "cli.config.json");
        var moduleFilterOverrides = new ModuleFilterOverrides(
            new[] { "CRM" },
            IncludeSystemModules: false,
            IncludeInactiveModules: true,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var sqlOverrides = new SqlOptionsOverrides(
            ConnectionString: null,
            CommandTimeoutSeconds: null,
            SamplingThreshold: null,
            SamplingSize: null,
            AuthenticationMethod: null,
            TrustServerCertificate: null,
            ApplicationName: null,
            AccessToken: null);
        var cacheOverrides = new CacheOptionsOverrides("cache-root", Refresh: false);
        var overrides = new BuildSsdtOverrides(null, null, null, null, null, null, null, null);
        var binder = new RecordingNamingBinder();

        var result = PipelineRequestContextBuilder.Build(new PipelineRequestContextBuilderRequest(
            configurationContext,
            moduleFilterOverrides,
            sqlOverrides,
            cacheOverrides,
            SqlMetadataOutputPath: null,
            new NamingOverridesRequest(overrides, binder)));

        Assert.True(result.IsSuccess);
        var context = result.Value;
        Assert.Equal(configuration, context.Configuration);
        Assert.Equal(configuration.Tightening, context.Tightening);
        Assert.Equal(configurationContext.ConfigPath, context.ConfigPath);
        Assert.False(context.ModuleFilter.IncludeSystemModules);
        Assert.True(context.ModuleFilter.IncludeInactiveModules);
        Assert.NotNull(context.TypeMappingPolicy);
        Assert.Same(binder.Result, context.NamingOverrides);
        Assert.Null(context.SqlMetadataLog);
        Assert.Equal(cacheOverrides, context.CacheOverrides);

        var cacheOptions = context.CreateCacheOptions(
            "dmm-compare",
            "model.json",
            "profile.json",
            "baseline.dmm");

        Assert.NotNull(cacheOptions);
        Assert.Equal("cache-root", cacheOptions!.Root);
        Assert.Equal("dmm-compare", cacheOptions.Command);
        Assert.NotNull(cacheOptions.Metadata);
        Assert.Contains("moduleFilter.selectionScope", cacheOptions.Metadata!.Keys);
    }

    [Fact]
    public async Task FlushMetadataAsync_WritesFileWhenLogHasEntries()
    {
        using var temp = new TempDirectory();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");

        var configuration = CliConfiguration.Empty;
        var contextResult = PipelineRequestContextBuilder.Build(new PipelineRequestContextBuilderRequest(
            new CliConfigurationContext(configuration, null),
            ModuleFilterOverrides: null,
            SqlOptionsOverrides: null,
            CacheOptionsOverrides: null,
            SqlMetadataOutputPath: metadataPath,
            NamingOverrides: null));

        Assert.True(contextResult.IsSuccess);
        var context = contextResult.Value;
        Assert.NotNull(context.SqlMetadataLog);

        context.SqlMetadataLog!.RecordRequest("test", new { value = 1 });

        await context.FlushMetadataAsync(CancellationToken.None);

        Assert.True(File.Exists(metadataPath));
        var contents = await File.ReadAllTextAsync(metadataPath);
        Assert.Contains("\"status\"", contents);
        Assert.Contains("\"requests\"", contents);
    }

    [Fact]
    public async Task FlushMetadataAsync_DoesNotWriteWhenLogEmpty()
    {
        using var temp = new TempDirectory();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");

        var configuration = CliConfiguration.Empty;
        var contextResult = PipelineRequestContextBuilder.Build(new PipelineRequestContextBuilderRequest(
            new CliConfigurationContext(configuration, null),
            ModuleFilterOverrides: null,
            SqlOptionsOverrides: null,
            CacheOptionsOverrides: null,
            SqlMetadataOutputPath: metadataPath,
            NamingOverrides: null));

        Assert.True(contextResult.IsSuccess);
        var context = contextResult.Value;

        await context.FlushMetadataAsync(CancellationToken.None);

        Assert.False(File.Exists(metadataPath));
    }

    private sealed class RecordingNamingBinder : INamingOverridesBinder
    {
        public NamingOverrideOptions? Result { get; private set; }

        public Result<NamingOverrideOptions> Bind(BuildSsdtOverrides overrides, TighteningOptions tighteningOptions)
        {
            Result = NamingOverrideOptions.Empty;
            return Result<NamingOverrideOptions>.Success(NamingOverrideOptions.Empty);
        }
    }
}
