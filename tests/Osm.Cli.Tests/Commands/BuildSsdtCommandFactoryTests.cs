using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
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
        services.AddSingleton<PipelineVerbExecutor>();
        services.AddSingleton<IPipelineVerb>(provider => new BuildSsdtVerb(
            provider.GetRequiredService<ICliConfigurationService>(),
            provider.GetRequiredService<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>()));
        services.AddSingleton<IVerbRegistry>(TestVerbRegistry.Create);
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
        var console = new TestConsole();
        var args = "build-ssdt --config config.json --modules ModuleA --include-system-modules --include-inactive-modules --allow-missing-primary-key Module::* --cache-root ./cache --refresh-cache --connection-string DataSource --model model.json --profile profile.json --profiler-provider fixture --static-data data.json --out output --rename-table Module=Override --max-degree-of-parallelism 4";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
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

        var result = application.LastResult!;
        Assert.Equal("output", result.OutputDirectory);
        Assert.Equal("model.json", result.ModelPath);
        Assert.Equal("fixture", result.ProfilerProvider);
        Assert.Single(result.PipelineResult.Manifest.Tables);
        var table = result.PipelineResult.Manifest.Tables.Single();
        Assert.Equal("Orders", table.Table);
        Assert.Equal("dbo", table.Schema);
        Assert.Equal("dbo.Orders.sql", table.TableFile);

        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("Emitted 1 tables to output", output);
        var manifestMessage = $"Manifest written to {Path.Combine("output", "manifest.json")}";
        Assert.Contains(manifestMessage, output);
        Assert.Contains("Decision log written to decision.log", output);
        Assert.Contains("Columns tightened: 1/2", output);
        Assert.Contains("Unique indexes enforced: 1/1", output);
        Assert.Contains("Foreign keys created: 1/1", output);

        Assert.Equal(string.Empty, console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenPipelineFails()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService
        {
            ShouldFail = true,
            FailureErrors = new[]
            {
                ValidationError.Create("cli.build", "Pipeline execution failed.")
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<PipelineVerbExecutor>();
        services.AddSingleton<IPipelineVerb>(provider => new BuildSsdtVerb(
            provider.GetRequiredService<ICliConfigurationService>(),
            provider.GetRequiredService<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>()));
        services.AddSingleton<IVerbRegistry>(TestVerbRegistry.Create);
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("build-ssdt --model model.json --profile profile.json", console);

        Assert.Equal(1, exitCode);
        Assert.Null(application.LastResult);
        Assert.Contains("cli.build: Pipeline execution failed.", console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenConfigurationFails()
    {
        var configurationService = new FakeConfigurationService
        {
            FailureErrors = new[]
            {
                ValidationError.Create("cli.config", "Missing configuration.")
            }
        };
        var application = new FakeBuildApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<PipelineVerbExecutor>();
        services.AddSingleton<IPipelineVerb>(provider => new BuildSsdtVerb(
            provider.GetRequiredService<ICliConfigurationService>(),
            provider.GetRequiredService<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>()));
        services.AddSingleton<IVerbRegistry>(TestVerbRegistry.Create);
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("build-ssdt", console);

        Assert.Equal(1, exitCode);
        Assert.Null(application.LastInput);
        Assert.Contains("cli.config: Missing configuration.", console.Error.ToString());
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public string? LastPath { get; private set; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            LastPath = overrideConfigPath;
            if (FailureErrors is { Count: > 0 } errors)
            {
                return Task.FromResult(Result<CliConfigurationContext>.Failure(errors));
            }

            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class FakeBuildApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public BuildSsdtApplicationResult? LastResult { get; private set; }

        public bool ShouldFail { get; init; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            if (ShouldFail)
            {
                return Task.FromResult(Result<BuildSsdtApplicationResult>.Failure(FailureErrors ?? Array.Empty<ValidationError>()));
            }

            LastResult = CreateResult();
            return Task.FromResult(Result<BuildSsdtApplicationResult>.Success(LastResult));
        }

        private static BuildSsdtApplicationResult CreateResult()
        {
            var snapshot = ProfileSnapshot.Create(
                Array.Empty<ColumnProfile>(),
                Array.Empty<UniqueCandidateProfile>(),
                Array.Empty<CompositeUniqueCandidateProfile>(),
                Array.Empty<ForeignKeyReality>()).Value;

            var toggle = TighteningToggleSnapshot.Create(TighteningOptions.Default);
            var columns = ImmutableArray.Create(
                new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                    true,
                    false,
                    ImmutableArray<string>.Empty),
                new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("Notes")),
                    false,
                    true,
                    ImmutableArray<string>.Empty));

            var uniqueIndexes = ImmutableArray.Create(
                new UniqueIndexDecisionReport(
                    new IndexCoordinate(new SchemaName("dbo"), new TableName("Orders"), new IndexName("IX_Orders_Customer")),
                    true,
                    false,
                    ImmutableArray<string>.Empty));

            var foreignKeys = ImmutableArray.Create(
                new ForeignKeyDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                    true,
                    ImmutableArray<string>.Empty));

            var report = new PolicyDecisionReport(
                columns,
                uniqueIndexes,
                foreignKeys,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableArray<TighteningDiagnostic>.Empty,
                ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
                toggle);

            var manifest = new SsdtManifest(
                new[]
                {
                    new TableManifestEntry(
                        Module: "Sales",
                        Schema: "dbo",
                        Table: "Orders",
                        TableFile: "dbo.Orders.sql",
                        Indexes: Array.Empty<string>(),
                        ForeignKeys: Array.Empty<string>(),
                        IncludesExtendedProperties: true)
                },
                new SsdtManifestOptions(false, false, false, 1),
                null,
                new SsdtEmissionMetadata("sha256", "hash"),
                Array.Empty<PreRemediationManifestEntry>(),
                SsdtCoverageSummary.CreateComplete(0, 0, 0),
                Array.Empty<string>());

            var pipelineResult = new BuildSsdtPipelineResult(
                snapshot,
                ImmutableArray<ProfilingInsight>.Empty,
                report,
                manifest,
                ImmutableArray<PipelineInsight>.Empty,
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
