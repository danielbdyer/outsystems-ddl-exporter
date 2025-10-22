using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.StaticData;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class BuildSsdtMatrixTests
{
    [Fact]
    public async Task MatrixScenarios_match_baselines_and_predicates()
    {
        var manifest = await LoadManifestAsync().ConfigureAwait(false);
        var scenarioResults = new Dictionary<string, ScenarioExecution>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var scenario in manifest.Scenarios)
            {
                var execution = await ExecuteScenarioAsync(scenario).ConfigureAwait(false);
                scenarioResults[scenario.Name] = execution;

                var baselinePath = FixtureFile.GetPath(scenario.Baseline);
                DirectorySnapshot.AssertMatches(baselinePath, execution.OutputPath);
            }

            foreach (var assertion in manifest.Assertions)
            {
                Assert.True(
                    scenarioResults.TryGetValue(assertion.Scenario, out var execution),
                    $"Scenario '{assertion.Scenario}' was not executed.");

                AssertPredicateSatisfied(execution.Report.Predicates, assertion);

                var artifactContent = execution.GetArtifactContent(assertion.Artifact.Path);
                foreach (var expected in assertion.Artifact.Contains)
                {
                    Assert.Contains(expected, artifactContent, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally
        {
            foreach (var execution in scenarioResults.Values)
            {
                execution.Dispose();
            }
        }
    }

    private static async Task<BuildSsdtMatrixManifest> LoadManifestAsync()
    {
        var manifestPath = FixtureFile.GetPath(Path.Combine("emission-matrix", "matrix.json"));
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<BuildSsdtMatrixManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false);

        if (manifest is null)
        {
            throw new InvalidOperationException("Failed to deserialize build-ssdt matrix manifest.");
        }

        return manifest;
    }

    private static async Task<ScenarioExecution> ExecuteScenarioAsync(MatrixScenario scenario)
    {
        var modelPath = FixtureFile.GetPath(scenario.Model);
        var profilePath = scenario.Profile is null ? null : FixtureFile.GetPath(scenario.Profile);
        var staticDataPath = scenario.StaticData is null ? null : FixtureFile.GetPath(scenario.StaticData);

        var output = new TempDirectory();
        var staticProvider = staticDataPath is null ? null : new FixtureStaticEntityDataProvider(staticDataPath);

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
            StaticDataProvider: staticProvider,
            SeedOutputDirectoryHint: null);

        var pipeline = new BuildSsdtPipeline();
        var result = await pipeline.HandleAsync(request).ConfigureAwait(false);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        var pipelineResult = result.Value;

        var emission = EmissionOutput.Load(output.Path);
        return new ScenarioExecution(output, pipelineResult, emission);
    }

    private static void AssertPredicateSatisfied(PredicateTelemetry telemetry, MatrixAssertion assertion)
    {
        var predicate = assertion.Predicate;
        var target = assertion.Target;
        bool Match(string? expected, string actual)
            => expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        switch (target.Kind.ToUpperInvariant())
        {
            case "TABLE":
            {
                var table = telemetry.Tables.FirstOrDefault(entry =>
                    Match(target.Module, entry.Module) &&
                    Match(target.Schema, entry.Schema) &&
                    Match(target.Table, entry.PhysicalName));
                Assert.NotNull(table);
                Assert.Contains(predicate, table!.Predicates, StringComparer.Ordinal);
                break;
            }
            case "COLUMN":
            {
                var column = telemetry.Columns.FirstOrDefault(entry =>
                    Match(target.Module, entry.Module) &&
                    Match(target.Schema, entry.Schema) &&
                    Match(target.Table, entry.Table) &&
                    Match(target.Column, entry.Column));
                Assert.NotNull(column);
                Assert.Contains(predicate, column!.Predicates, StringComparer.Ordinal);
                break;
            }
            case "INDEX":
            {
                var index = telemetry.Indexes.FirstOrDefault(entry =>
                    Match(target.Module, entry.Module) &&
                    Match(target.Schema, entry.Schema) &&
                    Match(target.Table, entry.Table) &&
                    Match(target.Index, entry.Index));
                Assert.NotNull(index);
                Assert.Contains(predicate, index!.Predicates, StringComparer.Ordinal);
                break;
            }
            case "EXTENDEDPROPERTY":
            {
                var property = telemetry.ExtendedProperties.FirstOrDefault(entry =>
                    Match(target.Scope, entry.Scope) &&
                    Match(target.Module, entry.Module ?? string.Empty) &&
                    Match(target.Schema, entry.Schema ?? string.Empty) &&
                    Match(target.Table, entry.Table ?? string.Empty) &&
                    Match(target.Column, entry.Column ?? string.Empty));
                Assert.NotNull(property);
                Assert.Contains(predicate, property!.Predicates, StringComparer.Ordinal);
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported assertion target kind '{target.Kind}'.");
        }
    }

    private sealed record BuildSsdtMatrixManifest(
        ImmutableArray<MatrixScenario> Scenarios,
        ImmutableArray<MatrixAssertion> Assertions);

    private sealed record MatrixScenario(
        string Name,
        string Model,
        string? Profile,
        string? StaticData,
        string Baseline);

    private sealed record MatrixAssertion(
        string Name,
        string Scenario,
        string Predicate,
        MatrixAssertionTarget Target,
        MatrixAssertionArtifact Artifact);

    private sealed record MatrixAssertionTarget(
        string Kind,
        string? Module,
        string? Schema,
        string? Table,
        string? Column,
        string? Index,
        string? Scope);

    private sealed record MatrixAssertionArtifact(string Path, ImmutableArray<string> Contains);

    private sealed class ScenarioExecution : IDisposable
    {
        private readonly TempDirectory _workspace;
        private readonly BuildSsdtPipelineResult _result;
        private readonly Dictionary<string, string> _artifactCache = new(StringComparer.OrdinalIgnoreCase);

        public ScenarioExecution(TempDirectory workspace, BuildSsdtPipelineResult result, EmissionOutput emission)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _result = result ?? throw new ArgumentNullException(nameof(result));
            Emission = emission ?? throw new ArgumentNullException(nameof(emission));
        }

        public PolicyDecisionReport Report => _result.DecisionReport;

        public EmissionOutput Emission { get; }

        public string OutputPath => _workspace.Path;

        public string GetArtifactContent(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("Artifact path must be provided.", nameof(relativePath));
            }

            if (_artifactCache.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }

            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_workspace.Path, normalized);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Artifact '{relativePath}' was not produced for scenario.", fullPath);
            }

            var content = File.ReadAllText(fullPath);
            _artifactCache[relativePath] = content;
            return content;
        }

        public void Dispose() => _workspace.Dispose();
    }
}
