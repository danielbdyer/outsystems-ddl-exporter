using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Osm.Smo.TypeMapping;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_emits_manifest_seed_and_cache()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        using var cache = new TempDirectory();

        var request = new BuildSsdtPipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            output.Path,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            "fixture",
            profilePath,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null)),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicy.LoadDefault(),
            new EvidenceCachePipelineOptions(
                cache.Path,
                Refresh: false,
                Command: "build-ssdt",
                ModelPath: modelPath,
                ProfilePath: profilePath,
                DmmPath: null,
                ConfigPath: null,
                Metadata: new Dictionary<string, string?>()),
            new EmptyStaticEntityDataProvider(),
            Path.Combine(output.Path, "Seeds", "StaticEntities.seed.sql"));

        var pipeline = new BuildSsdtPipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var value = result.Value;

        Assert.NotNull(value.Manifest);
        Assert.True(File.Exists(Path.Combine(output.Path, "manifest.json")));
        Assert.True(File.Exists(value.DecisionLogPath));
        Assert.NotNull(value.StaticSeedScriptPath);
        Assert.True(File.Exists(value.StaticSeedScriptPath!));
        Assert.True(File.Exists(Path.Combine(output.Path, "policy-decisions.json")));
        Assert.NotNull(value.ExecutionLog);
        Assert.True(value.ExecutionLog.Entries.Count > 0);
        Assert.Contains(value.ExecutionLog.Entries, entry => entry.Step == "pipeline.completed");

        Assert.NotNull(value.EvidenceCache);
        Assert.True(Directory.Exists(value.EvidenceCache!.CacheDirectory));
        Assert.True(File.Exists(Path.Combine(value.EvidenceCache.CacheDirectory, "manifest.json")));

        var manifestJson = await File.ReadAllTextAsync(Path.Combine(output.Path, "manifest.json"));
        using var manifestDocument = JsonDocument.Parse(manifestJson);
        Assert.True(manifestDocument.RootElement.GetProperty("Tables").GetArrayLength() > 0);
    }

    private sealed class EmptyStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            var data = definitions
                .Select(definition => StaticEntityTableData.Create(definition, Enumerable.Empty<StaticEntityRow>()))
                .ToArray();
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success((IReadOnlyList<StaticEntityTableData>)data));
        }
    }
}
