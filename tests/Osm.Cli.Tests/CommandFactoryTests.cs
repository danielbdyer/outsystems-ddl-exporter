using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Xunit;

namespace Osm.Cli.Tests;

public sealed class CommandFactoryTests
{
    [Fact]
    public void BuildCommand_includes_expected_options()
    {
        var command = BuildCommandFactory.Create();

        var aliases = command.Options
            .SelectMany(static option => option.Aliases)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("--config", aliases);
        Assert.Contains("--modules", aliases);
        Assert.Contains("--max-degree-of-parallelism", aliases);
        Assert.Contains("--connection-string", aliases);
        Assert.Contains("--rename-table", aliases);
    }

    [Fact]
    public async Task BuildCommand_resolves_services_from_provider()
    {
        var command = BuildCommandFactory.Create();
        var configurationService = new StubConfigurationService();
        var applicationService = new StubBuildApplicationService();

        using var provider = new ServiceCollection()
            .AddSingleton<ICliConfigurationService>(configurationService)
            .AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(applicationService)
            .BuildServiceProvider();

        var parser = CreateParser(command, provider);

        var exitCode = await parser.InvokeAsync("--config dummy").ConfigureAwait(false);

        Assert.Equal(1, exitCode);
        Assert.True(configurationService.Invoked);
        Assert.True(applicationService.Invoked);
    }

    [Fact]
    public void ExtractCommand_includes_expected_options()
    {
        var command = ExtractCommandFactory.Create();

        var aliases = command.Options
            .SelectMany(static option => option.Aliases)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("--config", aliases);
        Assert.Contains("--modules", aliases);
        Assert.Contains("--connection-string", aliases);
        Assert.Contains("--out", aliases);
        Assert.Contains("--mock-advanced-sql", aliases);
    }

    [Fact]
    public async Task ExtractCommand_resolves_services_from_provider()
    {
        var command = ExtractCommandFactory.Create();
        var configurationService = new StubConfigurationService();
        var applicationService = new StubExtractApplicationService();

        using var provider = new ServiceCollection()
            .AddSingleton<ICliConfigurationService>(configurationService)
            .AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>>(applicationService)
            .BuildServiceProvider();

        var parser = CreateParser(command, provider);

        var exitCode = await parser.InvokeAsync("--config dummy").ConfigureAwait(false);

        Assert.Equal(1, exitCode);
        Assert.True(configurationService.Invoked);
        Assert.True(applicationService.Invoked);
    }

    private static Parser CreateParser(Command command, IServiceProvider provider)
        => new CommandLineBuilder(command)
            .UseDefaults()
            .AddMiddleware((context, next) =>
            {
                context.BindingContext.AddService(_ => provider);
                return next(context);
            })
            .Build();

    private sealed class StubConfigurationService : ICliConfigurationService
    {
        public bool Invoked { get; private set; }

        public Task<Result<CliConfigurationContext>> LoadAsync(
            string? overrideConfigPath,
            CancellationToken cancellationToken = default)
        {
            Invoked = true;
            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class StubBuildApplicationService
        : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        public bool Invoked { get; private set; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(
            BuildSsdtApplicationInput input,
            CancellationToken cancellationToken = default)
        {
            Invoked = true;
            var error = ValidationError.Create("tests.build.failure", "Test build failure");
            return Task.FromResult(Result<BuildSsdtApplicationResult>.Failure(error));
        }
    }

    private sealed class StubExtractApplicationService
        : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        public bool Invoked { get; private set; }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(
            ExtractModelApplicationInput input,
            CancellationToken cancellationToken = default)
        {
            Invoked = true;
            var error = ValidationError.Create("tests.extract.failure", "Test extract failure");
            return Task.FromResult(Result<ExtractModelApplicationResult>.Failure(error));
        }
    }
}
