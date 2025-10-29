using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class ExtractModelApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_UsesConfigurationModulesWhenOverridesMissing()
    {
        var configuration = CreateConfiguration(
            modules: new[] { "AppCore", "ExtBilling" },
            includeSystem: true,
            includeInactive: null);
        var context = new CliConfigurationContext(configuration, ConfigPath: "config/appsettings.json");

        var dispatcher = new RecordingDispatcher();
        var service = new ExtractModelApplicationService(dispatcher, NullLogger<ExtractModelApplicationService>.Instance);

        var input = new ExtractModelApplicationInput(
            context,
            new ExtractModelOverrides(null, null, null, null, null, null),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null));

        var result = await service.RunAsync(input);

        Assert.True(result.IsFailure);
        Assert.NotNull(dispatcher.Request);

        var command = dispatcher.Request!.Command;
        Assert.Equal(2, command.ModuleNames.Length);
        Assert.Contains("AppCore", command.ModuleNames.Select(static module => module.Value));
        Assert.Contains("ExtBilling", command.ModuleNames.Select(static module => module.Value));
        Assert.True(command.IncludeSystemModules);
        Assert.False(command.OnlyActiveAttributes);
    }

    [Fact]
    public async Task RunAsync_PrefersCliOverridesOverConfiguration()
    {
        var configuration = CreateConfiguration(
            modules: new[] { "AppCore", "ExtBilling" },
            includeSystem: true,
            includeInactive: true);
        var context = new CliConfigurationContext(configuration, ConfigPath: "config/appsettings.json");

        var dispatcher = new RecordingDispatcher();
        var service = new ExtractModelApplicationService(dispatcher, NullLogger<ExtractModelApplicationService>.Instance);

        var input = new ExtractModelApplicationInput(
            context,
            new ExtractModelOverrides(new[] { "Ops" }, IncludeSystemModules: false, OnlyActiveAttributes: true, OutputPath: null, MockAdvancedSqlManifest: null, SqlMetadataOutputPath: null),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null));

        var result = await service.RunAsync(input);

        Assert.True(result.IsFailure);
        Assert.NotNull(dispatcher.Request);

        var command = dispatcher.Request!.Command;
        Assert.Single(command.ModuleNames);
        Assert.Equal("Ops", command.ModuleNames[0].Value);
        Assert.False(command.IncludeSystemModules);
        Assert.True(command.OnlyActiveAttributes);
    }

    [Fact]
    public async Task RunAsync_FlushesMetadataLogWhenEntriesRecorded()
    {
        using var temp = new TempDirectory();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");
        var configuration = CreateConfiguration(Array.Empty<string>(), includeSystem: null, includeInactive: null);
        var context = new CliConfigurationContext(configuration, ConfigPath: "config/appsettings.json");

        var dispatcher = new MetadataRecordingDispatcher();
        var service = new ExtractModelApplicationService(dispatcher, NullLogger<ExtractModelApplicationService>.Instance);

        var input = new ExtractModelApplicationInput(
            context,
            new ExtractModelOverrides(null, null, null, null, null, metadataPath),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null));

        var result = await service.RunAsync(input);

        Assert.True(result.IsFailure);
        Assert.True(File.Exists(metadataPath));
        dispatcher.AssertRequestLogged();
    }

    private static CliConfiguration CreateConfiguration(
        string[] modules,
        bool? includeSystem,
        bool? includeInactive)
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            CacheConfiguration.Empty,
            ProfilerConfiguration.Empty,
            SqlConfiguration.Empty,
            new ModuleFilterConfiguration(
                modules,
                includeSystem,
                includeInactive,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase)),
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public ExtractModelPipelineRequest? Request { get; private set; }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            if (command is ExtractModelPipelineRequest request)
            {
                Request = request;
                return Task.FromResult(Result<TResponse>.Failure(ValidationError.Create("test.dispatch", "stub failure")));
            }

            throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
        }
    }

    private sealed class MetadataRecordingDispatcher : ICommandDispatcher
    {
        public ExtractModelPipelineRequest? Request { get; private set; }

        public void AssertRequestLogged()
        {
            Assert.NotNull(Request);
            Assert.NotNull(Request!.SqlMetadataLog);
        }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            if (command is ExtractModelPipelineRequest request)
            {
                Request = request;
                request.SqlMetadataLog?.RecordRequest("extract", new { value = 1 });
                return Task.FromResult(Result<TResponse>.Failure(ValidationError.Create("test.dispatch", "stub failure")));
            }

            throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
        }
    }
}
