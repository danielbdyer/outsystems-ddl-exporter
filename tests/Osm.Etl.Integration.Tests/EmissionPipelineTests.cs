using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Json;
using Osm.Json.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Profiling;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Etl.Integration.Tests;

public class EmissionPipelineTests
{
    [Fact]
    public async Task BuildSsdtPipeline_MatchesEdgeCaseFixtures()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var modelPath = Path.Combine(repoRoot, "tests", "Fixtures", "model.edge-case.json");
        var profilePath = Path.Combine(repoRoot, "tests", "Fixtures", "profiling", "profile.edge-case.json");
        var configPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var expectedRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case");

        var tighteningOptions = await LoadTighteningOptionsAsync(configPath);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var output = new TempDirectory();
        var emitter = new Osm.Emission.SsdtEmitter();
        await emitter.EmitAsync(smoModel, output.Path, smoOptions, decisionReport);
        await WriteDecisionLogAsync(output.Path, decisionReport);

        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);
    }

    [Fact]
    public async Task BuildSsdtPipeline_WithRenamesMatchesFixtures()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var modelPath = Path.Combine(repoRoot, "tests", "Fixtures", "model.edge-case.json");
        var profilePath = Path.Combine(repoRoot, "tests", "Fixtures", "profiling", "profile.edge-case.json");
        var configPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var expectedRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case-rename");

        var tighteningOptions = await LoadTighteningOptionsAsync(configPath);
        var model = await LoadModelAsync(modelPath);
        var profile = await LoadProfileAsync(profilePath);

        var overrideResult = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL");
        Assert.True(overrideResult.IsSuccess, string.Join(Environment.NewLine, overrideResult.Errors.Select(e => e.Message)));

        var overrideOptions = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(overrideOptions.IsSuccess, string.Join(Environment.NewLine, overrideOptions.Errors.Select(e => e.Message)));

        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, profile, tighteningOptions);
        var decisionReport = PolicyDecisionReporter.Create(decisions);

        var smoOptions = SmoBuildOptions
            .FromEmission(tighteningOptions.Emission)
            .WithNamingOverrides(overrideOptions.Value);
        var smoModel = new SmoModelFactory().Create(model, decisions, smoOptions);

        using var output = new TempDirectory();
        var emitter = new Osm.Emission.SsdtEmitter();
        await emitter.EmitAsync(smoModel, output.Path, smoOptions, decisionReport);
        await WriteDecisionLogAsync(output.Path, decisionReport);

        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);
    }

    private static async Task<TighteningOptions> LoadTighteningOptionsAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var result = new TighteningOptionsDeserializer().Deserialize(stream);
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static async Task<Osm.Domain.Model.OsmModel> LoadModelAsync(string path)
    {
        var ingestion = new ModelIngestionService(new ModelJsonDeserializer());
        var result = await ingestion.LoadFromFileAsync(path);
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static async Task<Osm.Domain.Profiling.ProfileSnapshot> LoadProfileAsync(string path)
    {
        var profiler = new FixtureDataProfiler(path, new ProfileSnapshotDeserializer());
        var result = await profiler.CaptureAsync();
        AssertResultSucceeded(result);
        return result.Value;
    }

    private static void AssertResultSucceeded<T>(Osm.Domain.Abstractions.Result<T> result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Code}: {e.Message}"));
        throw new Xunit.Sdk.XunitException($"Expected result to succeed but failed with:{Environment.NewLine}{message}");
    }

    private static async Task WriteDecisionLogAsync(string outputDirectory, PolicyDecisionReport report)
    {
        var log = new PolicyDecisionLog(
            report.ColumnCount,
            report.TightenedColumnCount,
            report.RemediationColumnCount,
            report.UniqueIndexCount,
            report.UniqueIndexesEnforcedCount,
            report.UniqueIndexesRequireRemediationCount,
            report.ForeignKeyCount,
            report.ForeignKeysCreatedCount,
            report.ColumnRationaleCounts,
            report.UniqueIndexRationaleCounts,
            report.ForeignKeyRationaleCounts,
            report.Columns.Select(static c => new PolicyDecisionLogColumn(
                c.Column.Schema.Value,
                c.Column.Table.Value,
                c.Column.Column.Value,
                c.MakeNotNull,
                c.RequiresRemediation,
                c.Rationales.ToArray())).ToArray(),
            report.UniqueIndexes.Select(static u => new PolicyDecisionLogUniqueIndex(
                u.Index.Schema.Value,
                u.Index.Table.Value,
                u.Index.Index.Value,
                u.EnforceUnique,
                u.RequiresRemediation,
                u.Rationales.ToArray())).ToArray(),
            report.ForeignKeys.Select(static f => new PolicyDecisionLogForeignKey(
                f.Column.Schema.Value,
                f.Column.Table.Value,
                f.Column.Column.Value,
                f.CreateConstraint,
                f.Rationales.ToArray())).ToArray(),
            report.Diagnostics.Select(static d => new PolicyDecisionLogDiagnostic(
                d.LogicalName,
                d.CanonicalModule,
                d.CanonicalSchema,
                d.CanonicalPhysicalName,
                d.Code,
                d.Message,
                d.Severity.ToString(),
                d.ResolvedByOverride,
                d.Candidates.Select(static c => new PolicyDecisionLogDuplicateCandidate(
                    c.Module,
                    c.Schema,
                    c.PhysicalName)).ToArray())).ToArray());

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "policy-decisions.json"), json);
    }

    private sealed record PolicyDecisionLog(
        int ColumnCount,
        int TightenedColumnCount,
        int RemediationColumnCount,
        int UniqueIndexCount,
        int UniqueIndexesEnforcedCount,
        int UniqueIndexesRequireRemediationCount,
        int ForeignKeyCount,
        int ForeignKeysCreatedCount,
        IReadOnlyDictionary<string, int> ColumnRationales,
        IReadOnlyDictionary<string, int> UniqueIndexRationales,
        IReadOnlyDictionary<string, int> ForeignKeyRationales,
        IReadOnlyList<PolicyDecisionLogColumn> Columns,
        IReadOnlyList<PolicyDecisionLogUniqueIndex> UniqueIndexes,
        IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys,
        IReadOnlyList<PolicyDecisionLogDiagnostic> Diagnostics);

    private sealed record PolicyDecisionLogColumn(
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogUniqueIndex(
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogForeignKey(
        string Schema,
        string Table,
        string Column,
        bool CreateConstraint,
        IReadOnlyList<string> Rationales);

    private sealed record PolicyDecisionLogDiagnostic(
        string LogicalName,
        string CanonicalModule,
        string CanonicalSchema,
        string CanonicalPhysicalName,
        string Code,
        string Message,
        string Severity,
        bool ResolvedByOverride,
        IReadOnlyList<PolicyDecisionLogDuplicateCandidate> Candidates);

    private sealed record PolicyDecisionLogDuplicateCandidate(string Module, string Schema, string PhysicalName);
}
