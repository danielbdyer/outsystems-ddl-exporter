using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class BuildSsdtCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptionsAndRunsPipeline()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
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
        var args = "build-ssdt --config config.json --modules ModuleA --include-system-modules --include-inactive-modules --allow-missing-primary-key Module::* --cache-root ./cache --refresh-cache --connection-string DataSource --model model.json --profile profile.json --profiler-provider fixture --static-data data.json --out output --rename-table Module=Override --max-degree-of-parallelism 4";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Contains("--output-format", command.Options.SelectMany(option => option.Aliases));
        Assert.Equal("config.json", configurationService.LastPath);
        var input = application.LastInput!;
        Assert.Equal("model.json", input.Overrides.ModelPath);
        Assert.Equal("profile.json", input.Overrides.ProfilePath);
        Assert.Equal("output", input.Overrides.OutputDirectory);
        Assert.Equal("fixture", input.Overrides.ProfilerProvider);
        Assert.Equal("data.json", input.Overrides.StaticDataPath);
        Assert.Equal("Module=Override", input.Overrides.RenameOverrides);
        Assert.Equal(4, input.Overrides.MaxDegreeOfParallelism);
        Assert.Equal(new[] { "ModuleA" }, input.ModuleFilter.Modules);
        Assert.True(input.ModuleFilter.IncludeSystemModules);
        Assert.True(input.ModuleFilter.IncludeInactiveModules);
        Assert.Equal(new[] { "Module::*" }, input.ModuleFilter.AllowMissingPrimaryKey);
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

    private sealed class FakeBuildApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(Result<BuildSsdtApplicationResult>.Success(CreateResult()));
        }

        private static BuildSsdtApplicationResult CreateResult()
        {
            var snapshot = ProfileSnapshot.Create(
                Array.Empty<ColumnProfile>(),
                Array.Empty<UniqueCandidateProfile>(),
                Array.Empty<CompositeUniqueCandidateProfile>(),
                Array.Empty<ForeignKeyReality>()).Value;

            var toggle = TighteningToggleSnapshot.Create(TighteningOptions.Default);
            var report = new PolicyDecisionReport(
                ImmutableArray<ColumnDecisionReport>.Empty,
                ImmutableArray<UniqueIndexDecisionReport>.Empty,
                ImmutableArray<ForeignKeyDecisionReport>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableArray<TighteningDiagnostic>.Empty,
                ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
                toggle);

            var manifest = new SsdtManifest(
                Array.Empty<TableManifestEntry>(),
                new SsdtManifestOptions(false, false, false, 1),
                null,
                new SsdtEmissionMetadata("sha256", "hash"),
                Array.Empty<PreRemediationManifestEntry>(),
                SsdtCoverageSummary.CreateComplete(0, 0, 0),
                Array.Empty<string>());

            var pipelineResult = new BuildSsdtPipelineResult(
                snapshot,
                report,
                manifest,
                "decision.log",
                ImmutableArray<string>.Empty,
                null,
                PipelineExecutionLog.Empty,
                ImmutableArray<string>.Empty);

            return new BuildSsdtApplicationResult(
                pipelineResult,
                "fixture",
                "profile.json",
                "output",
                "model.json",
                false,
                ImmutableArray<string>.Empty);
        }
    }
}
