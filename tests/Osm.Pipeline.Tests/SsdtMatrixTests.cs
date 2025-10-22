using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.StaticData;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class SsdtMatrixTests
{
    public static IEnumerable<object[]> MatrixCases()
    {
        var manifestPath = FixtureFile.GetPath(Path.Combine("emission-matrix", "matrix.json"));
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<MatrixManifest>(stream, SerializerOptions);
        Assert.NotNull(manifest);

        foreach (var testCase in manifest!.Cases)
        {
            yield return new object[] { testCase };
        }
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task BuildSsdtMatrixCaseProducesExpectedArtifacts(MatrixCase testCase)
    {
        using var output = new TempDirectory();

        var modelPath = FixtureFile.GetPath(testCase.Model);
        var profilePath = FixtureFile.GetPath(testCase.Profile);

        var staticDataPath = testCase.StaticData is null
            ? null
            : FixtureFile.GetPath(testCase.StaticData);
        IStaticEntityDataProvider? staticDataProvider = staticDataPath is null
            ? null
            : new FixtureStaticEntityDataProvider(staticDataPath);

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
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicy.LoadDefault(),
            EvidenceCache: null,
            StaticDataProvider: staticDataProvider,
            SeedOutputDirectoryHint: null);

        var pipeline = new BuildSsdtPipeline();
        var result = await pipeline.HandleAsync(request);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));

        var expectedRoot = FixtureFile.GetPath(testCase.Expected);
        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);

        var policyPath = Path.Combine(output.Path, "policy-decisions.json");
        using var policyStream = File.OpenRead(policyPath);
        using var policyDocument = JsonDocument.Parse(policyStream);
        var coverage = policyDocument.RootElement.GetProperty("PredicateCoverage");
        var tables = coverage.GetProperty("Tables");
        var match = tables
            .EnumerateArray()
            .First(table =>
                string.Equals(table.GetProperty("Schema").GetString(), testCase.Entity.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(table.GetProperty("Table").GetString(), testCase.Entity.Table, StringComparison.OrdinalIgnoreCase));

        var actualPredicates = match
            .GetProperty("Predicates")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var predicate in testCase.Predicates)
        {
            Assert.Contains(predicate, actualPredicates);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public sealed record MatrixManifest([property: JsonPropertyName("cases")] IReadOnlyList<MatrixCase> Cases);

    public sealed record MatrixCase(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("entity")] MatrixEntity Entity,
        [property: JsonPropertyName("predicates")] IReadOnlyList<string> Predicates,
        [property: JsonPropertyName("staticData")] string? StaticData);

    public sealed record MatrixEntity(
        [property: JsonPropertyName("module")] string Module,
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("table")] string Table);
}
