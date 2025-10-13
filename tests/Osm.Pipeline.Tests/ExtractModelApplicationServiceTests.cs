using System;
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
            new ExtractModelOverrides(null, null, null, null, null),
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
            new ExtractModelOverrides(new[] { "Ops" }, IncludeSystemModules: false, OnlyActiveAttributes: true, OutputPath: null, MockAdvancedSqlManifest: null),
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
            new ModuleFilterConfiguration(modules, includeSystem, includeInactive),
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
}
