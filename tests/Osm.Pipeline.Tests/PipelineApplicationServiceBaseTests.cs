using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class PipelineApplicationServiceBaseTests
{
    [Fact]
    public void RequirePath_WhenOverrideAndFallbackMissing_ReturnsValidationError()
    {
        var result = TestService.InvokeRequirePath(
            overridePath: null,
            fallbackPath: null,
            errorCode: "test.missing",
            errorMessage: "path missing");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("test.missing", error.Code);
    }

    [Fact]
    public void ResolveOutputDirectory_WhenOverrideMissing_UsesDefault()
    {
        var resolved = TestService.InvokeResolveOutputDirectory(null, defaultDirectory: "default-out");

        Assert.Equal("default-out", resolved);
    }

    [Fact]
    public async Task EnsureSuccessOrFlushAsync_WhenResultFails_FlushesMetadata()
    {
        var flushCount = 0;
        var context = CreateContext(cacheRoot: null, onFlush: () => flushCount++);
        var failure = Result<int>.Failure(ValidationError.Create("test.failure", "failed"));

        var result = await TestService.InvokeEnsureSuccessOrFlushAsync(failure, context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, flushCount);
    }

    [Fact]
    public async Task EnsureSuccessOrFlushAsync_WhenResultSucceeds_SkipsFlush()
    {
        var flushCount = 0;
        var context = CreateContext(cacheRoot: null, onFlush: () => flushCount++);
        var success = Result<int>.Success(42);

        var result = await TestService.InvokeEnsureSuccessOrFlushAsync(success, context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, flushCount);
    }

    [Fact]
    public void CreateCacheOptions_WhenCacheRootAvailable_UsesConfiguration()
    {
        var cacheRoot = "/tmp/cache";
        var context = CreateContext(cacheRoot, onFlush: null);

        var options = TestService.InvokeCreateCacheOptions(context, "command", "model.json", "profile.snapshot", "baseline.dmm");

        Assert.NotNull(options);
        Assert.Equal(cacheRoot, options!.RootDirectory);
        Assert.Equal("command", options.Command);
        Assert.Equal("model.json", options.ModelPath);
        Assert.Equal("profile.snapshot", options.ProfilePath);
        Assert.Equal("baseline.dmm", options.DmmPath);
    }

    private static PipelineRequestContext CreateContext(string? cacheRoot, Action? onFlush)
    {
        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            new CacheConfiguration(cacheRoot, Refresh: false),
            ProfilerConfiguration.Empty,
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty,
            UatUsersConfiguration.Empty);

        var pathCanonicalizer = new ForwardSlashPathCanonicalizer();
        var metadataBuilder = new CacheMetadataBuilder(pathCanonicalizer);
        var optionsFactory = new EvidenceCacheOptionsFactory(metadataBuilder, pathCanonicalizer);

        return new PipelineRequestContext(
            configuration,
            "config.json",
            configuration.Tightening,
            ModuleFilterOptions.IncludeAll,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                new SqlSamplingSettings(null, null),
                new SqlAuthenticationSettings(null, null, null, null),
                MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            TypeMappingPolicy.Default,
            new SupplementalModelOptions(true, Array.Empty<string>()),
            NamingOverrides: null,
            new CacheOptionsOverrides(null, null),
            SqlMetadataOutputPath: null,
            SqlMetadataLog: null,
            FlushMetadataAsync: _ =>
            {
                onFlush?.Invoke();
                return Task.CompletedTask;
            },
            metadataBuilder,
            optionsFactory);
    }

    private sealed class TestService : PipelineApplicationServiceBase
    {
        public static Result<string> InvokeRequirePath(string? overridePath, string? fallbackPath, string errorCode, string errorMessage)
            => RequirePath(overridePath, fallbackPath, errorCode, errorMessage);

        public static string InvokeResolveOutputDirectory(string? overridePath, string defaultDirectory)
            => ResolveOutputDirectory(overridePath, defaultDirectory);

        public static Task<Result<T>> InvokeEnsureSuccessOrFlushAsync<T>(Result<T> result, PipelineRequestContext context, CancellationToken cancellationToken)
            => EnsureSuccessOrFlushAsync(result, context, cancellationToken);

        public static EvidenceCachePipelineOptions? InvokeCreateCacheOptions(
            PipelineRequestContext context,
            string command,
            string modelPath,
            string? profilePath,
            string? dmmPath)
            => CreateCacheOptions(context, command, modelPath, profilePath, dmmPath);
    }
}
