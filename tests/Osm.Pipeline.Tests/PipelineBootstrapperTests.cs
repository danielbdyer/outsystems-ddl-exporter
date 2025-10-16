using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class PipelineBootstrapperTests
{
    [Fact]
    public async Task BootstrapAsync_AppendsProfilingWarnings()
    {
        var bootstrapper = new PipelineBootstrapper();
        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var modelPath = FixtureFile.GetPath("model.micro-unique.json");
        var telemetry = new PipelineBootstrapTelemetry(
            "test",
            ImmutableDictionary<string, string?>.Empty,
            "profiling",
            ImmutableDictionary<string, string?>.Empty,
            "done");

        var profile = ProfileFixtures.LoadSnapshot("profiling/profile.micro-unique.json");
        var warnings = ImmutableArray.Create("Null-count profiling timed out for table [dbo].[OSUSR_U_USER]; using conservative fallback values.");

        var request = new PipelineBootstrapRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            telemetry,
            (_, _) => Task.FromResult(Result<ProfilingCaptureResult>.Success(new ProfilingCaptureResult(profile, warnings))));

        var result = await bootstrapper.BootstrapAsync(log, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(warnings[0], result.Value.Warnings);

        var entries = log.Build().Entries;
        var profilingEntry = Assert.Single(entries, entry => entry.Step == "profiling.capture.completed");
        Assert.Equal("1", profilingEntry.Metadata["warningCount"]);
        Assert.Equal(warnings[0], profilingEntry.Metadata["warningExample"]);
    }
}
