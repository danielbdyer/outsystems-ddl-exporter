using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
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
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
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
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
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
        var console = new TestConsole();
        var args = "dmm-compare --config config.json --modules ModuleA --include-system-modules --include-inactive-modules --cache-root ./cache --refresh-cache --connection-string DataSource --model model.json --profile profile.json --dmm baseline.dmm --out output --max-degree-of-parallelism 2";
        var exitCode = await parser.InvokeAsync(args, console);

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

        var result = application.LastResult!;
        Assert.Equal("diff.json", result.DiffOutputPath);
        Assert.Equal("diff.json", result.PipelineResult.DiffArtifactPath);
        Assert.True(result.PipelineResult.Comparison.IsMatch);

        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("DMM parity confirmed. Diff artifact written to diff.json.", output);
        Assert.Equal(string.Empty, console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesDiffDetailsWhenMismatchDetected()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeCompareApplicationService
        {
            NextComparison = new DmmComparisonResult(
                false,
                new[]
                {
                    new DmmDifference("dbo", "Customer", "Nullability", Column: "Email", Expected: "NOT NULL", Actual: "NULL")
                },
                Array.Empty<DmmDifference>()),
            NextDiffPath = "diff.json"
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<DmmCompareCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DmmCompareCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("dmm-compare --model model.json --profile profile.json --dmm baseline.dmm", console);

        Assert.Equal(2, exitCode);
        Assert.Contains("Model requires additional SSDT coverage:", console.Error.ToString());
        Assert.Contains("dbo.Customer.Email – Nullability expected NOT NULL actual NULL", console.Error.ToString());
        Assert.Contains("Diff artifact written to diff.json.", console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenApplicationFails()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeCompareApplicationService
        {
            ShouldFail = true,
            FailureErrors = new[]
            {
                ValidationError.Create("cli.compare", "Comparison failed.")
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<DmmCompareCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DmmCompareCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("dmm-compare --model model.json --profile profile.json --dmm baseline.dmm", console);
        Assert.Equal(1, exitCode);
        Assert.Null(application.LastResult);
        Assert.Contains("cli.compare: Comparison failed.", console.Error.ToString());
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
        var application = new FakeCompareApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<DmmCompareCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DmmCompareCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("dmm-compare", console);
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

    private sealed class FakeCompareApplicationService : IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>
    {
        public CompareWithDmmApplicationInput? LastInput { get; private set; }

        public CompareWithDmmApplicationResult? LastResult { get; private set; }

        public bool ShouldFail { get; init; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public DmmComparisonResult? NextComparison { get; init; }

        public string NextDiffPath { get; init; } = "diff.json";

        public Task<Result<CompareWithDmmApplicationResult>> RunAsync(CompareWithDmmApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            if (ShouldFail)
            {
                return Task.FromResult(Result<CompareWithDmmApplicationResult>.Failure(FailureErrors ?? Array.Empty<ValidationError>()));
            }

            var snapshot = ProfileSnapshot.Create(
                Array.Empty<Osm.Domain.Profiling.ColumnProfile>(),
                Array.Empty<UniqueCandidateProfile>(),
                Array.Empty<CompositeUniqueCandidateProfile>(),
                Array.Empty<ForeignKeyReality>()).Value;
            var comparison = NextComparison ?? new DmmComparisonResult(true, Array.Empty<DmmDifference>(), Array.Empty<DmmDifference>());
            var pipeline = new DmmComparePipelineResult(
                snapshot,
                comparison,
                NextDiffPath,
                null,
                PipelineExecutionLog.Empty,
                ImmutableArray<string>.Empty);
            var result = new CompareWithDmmApplicationResult(pipeline, NextDiffPath);
            LastResult = result;
            return Task.FromResult(Result<CompareWithDmmApplicationResult>.Success(result));
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(
            ICliConfigurationService configurationService,
            IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult> applicationService)
        {
            _verb = new DmmCompareVerb(configurationService, applicationService);
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }
}
