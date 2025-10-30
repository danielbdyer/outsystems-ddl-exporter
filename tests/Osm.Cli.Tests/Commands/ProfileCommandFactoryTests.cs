using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class ProfileCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptionsAndRunsPipeline()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeProfileApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<ProfileCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ProfileCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var moduleBinder = provider.GetRequiredService<ModuleFilterOptionBinder>();
        Assert.Contains(moduleBinder.ModulesOption, command.Options);

        var sqlBinder = provider.GetRequiredService<SqlOptionBinder>();
        Assert.Contains(sqlBinder.ConnectionStringOption, command.Options);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var args = "profile --config config.json --modules ModuleA --profile profile.json --profiler-provider fixture --out output --connection-string DataSource";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        Assert.Equal("config.json", configurationService.LastPath);

        var input = application.LastInput!;
        Assert.Equal("profile.json", input.Overrides.ProfilePath);
        Assert.Equal("output", input.Overrides.OutputDirectory);
        Assert.Equal("fixture", input.Overrides.ProfilerProvider);
        Assert.Equal(new[] { "ModuleA" }, input.ModuleFilter.Modules);
        Assert.Equal("DataSource", input.Sql.ConnectionString);

        var result = application.LastResult!;
        Assert.Equal("output", result.OutputDirectory);
        Assert.Equal("fixture", result.ProfilerProvider);
        Assert.Equal("profile.json", result.FixtureProfilePath);

        var outputText = console.Out.ToString() ?? string.Empty;
        Assert.Contains("Profile written to", outputText);
        Assert.Contains("Manifest written to", outputText);
        Assert.Equal(string.Empty, console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenPipelineFails()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeProfileApplicationService
        {
            ShouldFail = true,
            FailureErrors = new[]
            {
                ValidationError.Create("cli.profile", "Pipeline execution failed.")
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<ProfileCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ProfileCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("profile --model model.json", console);

        Assert.Equal(1, exitCode);
        Assert.Null(application.LastResult);
        Assert.Contains("cli.profile: Pipeline execution failed.", console.Error.ToString());
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
        var application = new FakeProfileApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<ProfileCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ProfileCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("profile", console);

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

    private sealed class FakeProfileApplicationService : IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
    {
        public CaptureProfileApplicationInput? LastInput { get; private set; }

        public CaptureProfileApplicationResult? LastResult { get; private set; }

        public bool ShouldFail { get; init; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public Task<Result<CaptureProfileApplicationResult>> RunAsync(CaptureProfileApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            if (ShouldFail)
            {
                return Task.FromResult(Result<CaptureProfileApplicationResult>.Failure(FailureErrors ?? Array.Empty<ValidationError>()));
            }

            LastResult = CreateResult();
            return Task.FromResult(Result<CaptureProfileApplicationResult>.Success(LastResult));
        }

        private static CaptureProfileApplicationResult CreateResult()
        {
            var snapshot = ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json"));
            var manifest = new CaptureProfileManifest(
                "model.json",
                "output/profile.json",
                "fixture",
                new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true),
                new CaptureProfileSupplementalSummary(true, Array.Empty<string>()),
                new CaptureProfileSnapshotSummary(snapshot.Columns.Length, snapshot.UniqueCandidates.Length, snapshot.CompositeUniqueCandidates.Length, snapshot.ForeignKeys.Length, 1),
                Array.Empty<CaptureProfileInsight>(),
                Array.Empty<string>(),
                Array.Empty<CaptureProfileCoverageAnomaly>(),
                DateTimeOffset.UtcNow);

            var pipelineResult = new CaptureProfilePipelineResult(
                snapshot,
                manifest,
                "output/profile.json",
                "output/manifest.json",
                ImmutableArray<ProfilingInsight>.Empty,
                ImmutableArray<ProfilingCoverageAnomaly>.Empty,
                PipelineExecutionLog.Empty,
                ImmutableArray<string>.Empty);

            return new CaptureProfileApplicationResult(
                pipelineResult,
                "output",
                "model.json",
                "fixture",
                "profile.json");
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(
            ICliConfigurationService configurationService,
            IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult> applicationService)
        {
            _verb = new ProfileVerb(configurationService, applicationService);
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }
}
