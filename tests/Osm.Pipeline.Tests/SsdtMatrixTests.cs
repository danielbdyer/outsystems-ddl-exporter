using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Sql;
using Osm.Pipeline.StaticData;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;
using Osm.Pipeline.Configuration;

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

        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        var request = new BuildSsdtPipelineRequest(
            scope,
            output.Path,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: staticDataProvider,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);

        var pipeline = CreatePipeline();
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

    private static BuildSsdtPipeline CreatePipeline()
    {
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var evidenceCacheStep = new BuildSsdtEvidenceCacheStep(new EvidenceCacheCoordinator(new EvidenceCacheService()));
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var sqlValidationStep = new BuildSsdtSqlValidationStep(new SsdtSqlValidator());
        var staticSeedStep = new BuildSsdtStaticSeedStep(CreateSeedGenerator());
        var dynamicInsertStep = new BuildSsdtDynamicInsertStep(new DynamicEntityInsertGenerator(new SqlLiteralFormatter()));
        var telemetryPackagingStep = new BuildSsdtTelemetryPackagingStep();

        return new BuildSsdtPipeline(
            TimeProvider.System,
            bootstrapStep,
            evidenceCacheStep,
            policyStep,
            emissionStep,
            sqlProjectStep,
            sqlValidationStep,
            staticSeedStep,
            dynamicInsertStep,
            telemetryPackagingStep);
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
    }

    private static IDataProfilerFactory CreateProfilerFactory()
    {
        return new DataProfilerFactory(
            new ProfileSnapshotDeserializer(),
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));
    }

    private static StaticEntitySeedScriptGenerator CreateSeedGenerator()
    {
        var literalFormatter = new SqlLiteralFormatter();
        var sqlBuilder = new StaticSeedSqlBuilder(literalFormatter);
        var templateService = new StaticEntitySeedTemplateService();
        return new StaticEntitySeedScriptGenerator(templateService, sqlBuilder);
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
