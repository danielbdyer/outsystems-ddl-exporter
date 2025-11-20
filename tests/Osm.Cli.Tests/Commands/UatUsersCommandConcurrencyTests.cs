using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Pipeline.Configuration;
using Xunit;
using Tests.Support;

namespace Osm.Cli.Tests.Commands;

public class UatUsersCommandConcurrencyTests
{
    [Fact]
    public async Task Invoke_ParsesConcurrencyOption()
    {
        var command = "uat-users --model model.json --connection-string Server=.; --uat-user-inventory uat.csv --qa-user-inventory qa.csv --concurrency 8";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode); // Fake command returns 5
        Assert.Equal(8, options.Concurrency);
    }

    [Fact]
    public async Task Invoke_DefaultsConcurrencyToFour()
    {
        var command = "uat-users --model model.json --connection-string Server=.; --uat-user-inventory uat.csv --qa-user-inventory qa.csv";
        var (options, exitCode) = await InvokeAsync(command);

        Assert.Equal(5, exitCode);
        Assert.Equal(4, options.Concurrency);
    }

    private static async Task<(UatUsersOptions Options, int ExitCode)> InvokeAsync(
        string commandLine,
        CliConfiguration? configuration = null)
    {
        var executor = new FakeUatUsersCommand();

        await using var provider = CreateServiceProvider(executor, configuration);
        var factory = provider.GetRequiredService<UatUsersCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var exitCode = await parser.InvokeAsync(commandLine);

        if (executor.LastOptions == null && exitCode != 0)
        {
            // Command failed validation or execution
            // But since we expect 5, it means it executed.
        }

        Assert.NotNull(executor.LastOptions);
        return (executor.LastOptions!, exitCode);
    }

    private sealed class FakeUatUsersCommand : IUatUsersCommand
    {
        public UatUsersOptions? LastOptions { get; private set; }

        public Task<int> ExecuteAsync(UatUsersOptions options, CancellationToken cancellationToken)
        {
            LastOptions = options;
            return Task.FromResult(5);
        }
    }

    private static ServiceProvider CreateServiceProvider(FakeUatUsersCommand executor, CliConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUatUsersCommand>(executor);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService(configuration ?? CliConfiguration.Empty));
        services.AddSingleton<UatUsersCommandFactory>();
        return services.BuildServiceProvider();
    }

    private sealed class StubConfigurationService : ICliConfigurationService
    {
        private readonly CliConfiguration _configuration;

        public StubConfigurationService(CliConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<Osm.Domain.Abstractions.Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            var context = new CliConfigurationContext(_configuration, overrideConfigPath);
            return Task.FromResult(Osm.Domain.Abstractions.Result<CliConfigurationContext>.Success(context));
        }
    }
}
