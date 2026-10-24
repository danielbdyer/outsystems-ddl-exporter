using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Cli.Commands.Options;
using Osm.Cli.Commands;
using Osm.Pipeline.Runtime.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class VerbOptionRegistryTests
{
    [Fact]
    public void SharedModelAndProfilerOptions_BindConsistentlyAcrossVerbs()
    {
        var registry = CreateRegistry();
        var buildCommand = new Command("build-ssdt");
        registry.BuildSsdt.Configure(buildCommand);
        var profileCommand = new Command("profile");
        registry.Profile.Configure(profileCommand);

        var buildParser = new CommandLineBuilder(new RootCommand { buildCommand }).UseDefaults().Build();
        var profileParser = new CommandLineBuilder(new RootCommand { profileCommand }).UseDefaults().Build();

        var buildParse = buildParser.Parse("build-ssdt --model shared.json --profile build.snapshot --profiler-provider fixture");
        var profileParse = profileParser.Parse("profile --model shared.json --profile profile.snapshot --profiler-provider fixture");

        var buildBound = registry.BuildSsdt.Bind(buildParse);
        var profileBound = registry.Profile.Bind(profileParse);

        Assert.Equal("shared.json", buildBound.Overrides.ModelPath);
        Assert.Equal("shared.json", profileBound.Overrides.ModelPath);
        Assert.Equal("fixture", buildBound.Overrides.ProfilerProvider);
        Assert.Equal("fixture", profileBound.Overrides.ProfilerProvider);
    }

    [Fact]
    public void FullExportLoadHarnessExtension_BindsOptions()
    {
        var registry = CreateRegistry();
        var command = new Command("full-export");
        registry.FullExport.Configure(command);
        var parser = new CommandLineBuilder(new RootCommand { command }).UseDefaults().Build();
        var args = string.Join(
            ' ',
            "full-export",
            "--run-load-harness",
            "--load-harness-connection-string DataSource",
            "--load-harness-report-out harness.json",
            "--load-harness-command-timeout 120");

        var parseResult = parser.Parse(args);
        var bound = registry.FullExport.Bind(parseResult);
        var loadHarness = bound.GetExtension<LoadHarnessCliOptions>();

        Assert.NotNull(loadHarness);
        Assert.True(loadHarness!.RunHarness);
        Assert.Equal("DataSource", loadHarness.ConnectionString);
        Assert.Equal("harness.json", loadHarness.ReportOutputPath);
        Assert.Equal(120, loadHarness.CommandTimeoutSeconds);
    }

    private static VerbOptionRegistry CreateRegistry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<VerbOptionRegistry>();
    }
}
