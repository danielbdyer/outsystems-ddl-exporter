using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public sealed class AnalyzeCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptionsAndRunsVerb()
    {
        var configurationService = new FakeConfigurationService();
        var applicationService = new FakeAnalyzeApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>>(applicationService);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, applicationService));
        services.AddSingleton<AnalyzeCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<AnalyzeCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var args = "analyze --config config.json --model model.json --profile profile.json --out out";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal("config.json", configurationService.LastPath);
        var input = applicationService.LastInput!;
        Assert.Equal("model.json", input.Overrides.ModelPath);
        Assert.Equal("profile.json", input.Overrides.ProfilePath);
        Assert.Equal("out", input.Overrides.OutputDirectory);
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public string? LastPath { get; private set; }

        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            LastPath = overrideConfigPath;
            var configuration = new CliConfiguration(
                TighteningOptions.Default,
                ModelPath: null,
                ProfilePath: null,
                DmmPath: null,
                CacheConfiguration.Empty,
                ProfilerConfiguration.Empty,
                SqlConfiguration.Empty,
                ModuleFilterConfiguration.Empty,
                TypeMappingConfiguration.Empty,
                SupplementalModelConfiguration.Empty);
            return Task.FromResult(Result<CliConfigurationContext>.Success(new CliConfigurationContext(configuration, overrideConfigPath)));
        }
    }

    private sealed class FakeAnalyzeApplicationService : IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>
    {
        public AnalyzeApplicationInput? LastInput { get; private set; }

        public Task<Result<AnalyzeApplicationResult>> RunAsync(AnalyzeApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            var decisions = PolicyDecisionSet.Create(
                System.Collections.Immutable.ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
                System.Collections.Immutable.ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
                System.Collections.Immutable.ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
                System.Collections.Immutable.ImmutableArray<TighteningDiagnostic>.Empty,
                System.Collections.Immutable.ImmutableDictionary<ColumnCoordinate, string>.Empty,
                System.Collections.Immutable.ImmutableDictionary<IndexCoordinate, string>.Empty,
                TighteningOptions.Default);
            var report = PolicyDecisionReporter.Create(decisions);
            var profile = ProfileFixtures.LoadSnapshot("profiling/profile.edge-case.json");
            var outputDirectory = input.Overrides.OutputDirectory ?? "out";
            var pipelineResult = new TighteningAnalysisPipelineResult(
                report,
                profile,
                System.Collections.Immutable.ImmutableArray.Create("summary"),
                Path.Combine(outputDirectory, "summary.txt"),
                Path.Combine(outputDirectory, "policy-decisions.json"),
                PipelineExecutionLog.Empty,
                System.Collections.Immutable.ImmutableArray<string>.Empty);

            var result = new AnalyzeApplicationResult(
                pipelineResult,
                outputDirectory,
                input.Overrides.ModelPath ?? "model.json",
                input.Overrides.ProfilePath ?? "profile.json");

            return Task.FromResult(Result<AnalyzeApplicationResult>.Success(result));
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(
            ICliConfigurationService configurationService,
            IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult> applicationService)
        {
            _verb = new AnalyzeVerb(configurationService, applicationService);
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }
}
