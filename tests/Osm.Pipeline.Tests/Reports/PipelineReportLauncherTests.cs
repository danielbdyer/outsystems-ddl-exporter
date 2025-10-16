using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Reports;
using Osm.Validation.Tightening;
using Osm.Validation.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Reports;

public class PipelineReportLauncherTests
{
    [Fact]
    public async Task GenerateAsync_WritesReportToOutputDirectory()
    {
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        var launcher = new PipelineReportLauncher(new NullLogger<PipelineReportLauncher>());
        var applicationResult = new BuildSsdtApplicationResult(
            CreatePipelineResult(output),
            ProfilerProvider: "fixture",
            ProfilePath: "profile.json",
            OutputDirectory: output,
            ModelPath: "model.json",
            ModelWasExtracted: false,
            ModelExtractionWarnings: ImmutableArray<string>.Empty);

        var reportPath = await launcher.GenerateAsync(applicationResult, CancellationToken.None);

        Assert.True(File.Exists(reportPath));
        var content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("OutSystems DDL Exporter Report", content);
    }

    private static BuildSsdtPipelineResult CreatePipelineResult(string output)
    {
        var manifest = new SsdtManifest(
            new[]
            {
                new TableManifestEntry(
                    "Module",
                    "dbo",
                    "Table",
                    "Tables/dbo.Table.sql",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    IncludesExtendedProperties: false)
            },
            new SsdtManifestOptions(
                IncludePlatformAutoIndexes: true,
                EmitBareTableOnly: false,
                SanitizeModuleNames: true,
                ModuleParallelism: 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(1, 0, 0),
            Array.Empty<string>());

        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));

        return new BuildSsdtPipelineResult(
            ProfileSnapshot.Create(Array.Empty<ColumnProfile>(), Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), Array.Empty<ForeignKeyReality>()).Value,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            manifest,
            ImmutableArray<PipelineInsight>.Empty,
            Path.Combine(output, "policy-decisions.json"),
            ImmutableArray<string>.Empty,
            null,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty);
    }
}
