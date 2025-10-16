using System;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Text.Json;
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

namespace Osm.Cli.Tests;

public sealed class BuildSsdtCommandTests
{
    [Fact]
    public async Task BuildSsdt_WritesTextOutputByDefault()
    {
        await using var provider = CreateServiceProvider(out _);
        var parser = CreateParser(provider);
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync("build-ssdt", console);

        Assert.Equal(0, exitCode);
        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("Using model at model.json.", output);
        Assert.Contains("Emitted 0 tables to output.", output);
        Assert.Contains($"Manifest written to {System.IO.Path.Combine("output", "manifest.json")}", output);
        Assert.Contains("Columns tightened: 0/0", output);
        Assert.Contains("Unique indexes enforced: 0/0", output);
        Assert.Contains("Foreign keys created: 0/0", output);
        Assert.Contains("Decision log written to decision.log", output);
    }

    [Fact]
    public async Task BuildSsdt_EmitsJsonPayloadWhenRequested()
    {
        await using var provider = CreateServiceProvider(out _);
        var parser = CreateParser(provider);
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync("build-ssdt --output-format json", console);

        Assert.Equal(0, exitCode);
        var payload = console.Out.ToString();
        Assert.False(string.IsNullOrWhiteSpace(payload));

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;

        Assert.Equal("build-ssdt", root.GetProperty("command").GetString());

        var model = root.GetProperty("model");
        Assert.Equal("model.json", model.GetProperty("path").GetString());
        Assert.False(model.GetProperty("wasExtracted").GetBoolean());
        Assert.Equal(0, model.GetProperty("warnings").GetArrayLength());

        var profile = root.GetProperty("profile");
        Assert.Equal("fixture", profile.GetProperty("provider").GetString());
        Assert.Equal("profile.json", profile.GetProperty("path").GetString());

        var output = root.GetProperty("output");
        Assert.Equal("output", output.GetProperty("directory").GetString());
        Assert.Equal(System.IO.Path.Combine("output", "manifest.json"), output.GetProperty("manifestPath").GetString());
        Assert.Equal("decision.log", output.GetProperty("policyDecisionsPath").GetString());
        Assert.Equal(0, output.GetProperty("staticSeedScripts").GetArrayLength());

        var telemetry = root.GetProperty("telemetry");
        Assert.Equal(0, telemetry.GetProperty("tablesEmitted").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("columnsTightened").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("columnCount").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("uniqueIndexesEnforced").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("uniqueIndexCount").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("foreignKeysCreated").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("foreignKeyCount").GetInt32());

        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
        Assert.Equal(0, root.GetProperty("diagnostics").GetArrayLength());
        Assert.False(root.TryGetProperty("evidenceCache", out _));
        Assert.False(root.TryGetProperty("report", out _));
    }

    private static Parser CreateParser(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    private static ServiceProvider CreateServiceProvider(out FakeBuildApplicationService application)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService, FakeConfigurationService>();
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<BuildSsdtCommandFactory>();

        application = new FakeBuildApplicationService();
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);

        return services.BuildServiceProvider();
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class FakeBuildApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
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

            var applicationResult = new BuildSsdtApplicationResult(
                pipelineResult,
                "fixture",
                "profile.json",
                "output",
                "model.json",
                false,
                ImmutableArray<string>.Empty);

            return Task.FromResult(Result<BuildSsdtApplicationResult>.Success(applicationResult));
        }
    }
}
