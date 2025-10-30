using System;
using System.IO;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Json;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;
using Osm.Validation.Profiling;
using Osm.Smo;

namespace Osm.Pipeline.Tests;

public class TighteningAnalysisPipelineTests
{
    [Fact]
    public async Task HandleAsync_WritesOutputs()
    {
        using var temp = new TempDirectory();
        var scope = new ModelExecutionScope(
            FixtureFile.GetPath("model.edge-case.json"),
            ModuleFilterOptions.IncludeAll,
            new SupplementalModelOptions(true, Array.Empty<string>()),
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            FixtureFile.GetPath("profiling/profile.edge-case.json"));

        var request = new TighteningAnalysisPipelineRequest(
            scope,
            temp.Path);

        var pipeline = CreatePipeline();
        var result = await pipeline.HandleAsync(request, default);

        Assert.True(result.IsSuccess);
        var payload = result.Value;
        Assert.True(File.Exists(payload.SummaryPath));
        Assert.True(File.Exists(payload.DecisionLogPath));
        Assert.NotEmpty(payload.SummaryLines);
        Assert.NotEmpty(await File.ReadAllTextAsync(payload.SummaryPath));
    }
    private static TighteningAnalysisPipeline CreatePipeline()
    {
        return new TighteningAnalysisPipeline(
            CreatePipelineBootstrapper(),
            new TighteningPolicy(),
            new PolicyDecisionLogWriter(),
            new ProfileSnapshotDeserializer(),
            TimeProvider.System);
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
    }
}
