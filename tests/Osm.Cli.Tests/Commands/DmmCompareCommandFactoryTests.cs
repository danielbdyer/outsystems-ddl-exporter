using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class DmmCompareCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptions()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeCompareApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<DmmCompareCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DmmCompareCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var moduleBinder = provider.GetRequiredService<ModuleFilterOptionBinder>();
        Assert.Contains(moduleBinder.ModulesOption, command.Options);

        var cacheBinder = provider.GetRequiredService<CacheOptionBinder>();
        Assert.Contains(cacheBinder.CacheRootOption, command.Options);

        var sqlBinder = provider.GetRequiredService<SqlOptionBinder>();
        Assert.Contains(sqlBinder.ConnectionStringOption, command.Options);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var args = "dmm-compare --config config.json --modules ModuleA --include-system-modules --include-inactive-modules --cache-root ./cache --refresh-cache --connection-string DataSource --model model.json --profile profile.json --dmm baseline.dmm --out output --max-degree-of-parallelism 2";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal("config.json", configurationService.LastPath);
        var input = application.LastInput!;
        Assert.Equal("model.json", input.Overrides.ModelPath);
        Assert.Equal("profile.json", input.Overrides.ProfilePath);
        Assert.Equal("baseline.dmm", input.Overrides.DmmPath);
        Assert.Equal("output", input.Overrides.OutputDirectory);
        Assert.Equal(2, input.Overrides.MaxDegreeOfParallelism);
        Assert.Equal(new[] { "ModuleA" }, input.ModuleFilter.Modules);
        Assert.True(input.ModuleFilter.IncludeSystemModules);
        Assert.True(input.ModuleFilter.IncludeInactiveModules);
        Assert.Equal("./cache", input.Cache.Root);
        Assert.True(input.Cache.Refresh);
        Assert.Equal("DataSource", input.Sql.ConnectionString);
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public string? LastPath { get; private set; }

        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            LastPath = overrideConfigPath;
            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class FakeCompareApplicationService : IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>
    {
        public CompareWithDmmApplicationInput? LastInput { get; private set; }

        public Task<Result<CompareWithDmmApplicationResult>> RunAsync(CompareWithDmmApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            var snapshot = ProfileSnapshot.Create(
                Array.Empty<Osm.Domain.Profiling.ColumnProfile>(),
                Array.Empty<UniqueCandidateProfile>(),
                Array.Empty<CompositeUniqueCandidateProfile>(),
                Array.Empty<ForeignKeyReality>()).Value;
            var comparison = new DmmComparisonResult(true, Array.Empty<DmmDifference>(), Array.Empty<DmmDifference>());
            var pipeline = new DmmComparePipelineResult(
                snapshot,
                comparison,
                "diff.json",
                null,
                PipelineExecutionLog.Empty,
                ImmutableArray<string>.Empty);
            var result = new CompareWithDmmApplicationResult(pipeline, "diff.json");
            return Task.FromResult(Result<CompareWithDmmApplicationResult>.Success(result));
        }
    }
}
