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
using Osm.Pipeline.Profiling;
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
        Assert.Contains("Profiling telemetry:", outputText);
        Assert.Contains("Tables profiled: 2 (sampled: 1, full scan: 1)", outputText);
        Assert.Contains("Top slow tables:", outputText);
        Assert.Contains(" - dbo.Orders: 48.5 ms [full scan (5000 rows)]", outputText);
        Assert.Contains(" - dbo.Users: 28.5 ms [sampled (250 rows)]", outputText);
        Assert.Contains("Probe outcomes:", outputText);
        Assert.Contains(" - nullCounts: Succeeded:2", outputText);
        Assert.Contains(" - uniqueCandidates: Succeeded:2", outputText);
        Assert.Contains(" - foreignKeys: Succeeded:2", outputText);
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
            var capturedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var telemetryEntries = ImmutableArray.Create(
                new TableProfilingTelemetry(
                    "dbo",
                    "Users",
                    RowCount: 1_000,
                    Sampled: true,
                    SampleSize: 250,
                    SamplingParameter: 250,
                    ColumnCount: 12,
                    UniqueCandidateCount: 1,
                    ForeignKeyCount: 2,
                    NullCountDurationMilliseconds: 10.5,
                    UniqueCandidateDurationMilliseconds: 5.25,
                    ForeignKeyDurationMilliseconds: 7.75,
                    ForeignKeyMetadataDurationMilliseconds: 3.0,
                    TotalDurationMilliseconds: 28.5,
                    NullCountStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 250),
                    UniqueCandidateStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 250),
                    ForeignKeyStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 250)),
                new TableProfilingTelemetry(
                    "dbo",
                    "Orders",
                    RowCount: 5_000,
                    Sampled: false,
                    SampleSize: 5_000,
                    SamplingParameter: 0,
                    ColumnCount: 18,
                    UniqueCandidateCount: 2,
                    ForeignKeyCount: 3,
                    NullCountDurationMilliseconds: 12.0,
                    UniqueCandidateDurationMilliseconds: 9.0,
                    ForeignKeyDurationMilliseconds: 15.0,
                    ForeignKeyMetadataDurationMilliseconds: 4.5,
                    TotalDurationMilliseconds: 48.5,
                    NullCountStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 5_000),
                    UniqueCandidateStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 5_000),
                    ForeignKeyStatus: ProfilingProbeStatus.CreateSucceeded(capturedAt, 5_000)));
            var telemetry = ProfilingRunTelemetry.Create(telemetryEntries);

            var manifest = new CaptureProfileManifest(
                "model.json",
                "output/profile.json",
                "fixture",
                new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true),
                new CaptureProfileSupplementalSummary(true, Array.Empty<string>()),
                new CaptureProfileSnapshotSummary(snapshot.Columns.Length, snapshot.UniqueCandidates.Length, snapshot.CompositeUniqueCandidates.Length, snapshot.ForeignKeys.Length, 1),
                Array.Empty<CaptureProfileInsight>(),
                Array.Empty<string>(),
                telemetry.Summary,
                DateTimeOffset.UtcNow);

            var pipelineResult = new CaptureProfilePipelineResult(
                snapshot,
                manifest,
                "output/profile.json",
                "output/manifest.json",
                telemetry,
                ImmutableArray<ProfilingInsight>.Empty,
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
