using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        AssertDirectoryMatchesFixture(expectedRoot, output.Path);
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

    private static void AssertDirectoryMatchesFixture(string expectedRoot, string actualRoot)
    {
        var expectedFiles = ReadAllFiles(expectedRoot);
        var actualFiles = ReadAllFiles(actualRoot);

        Assert.Empty(expectedFiles.Keys.Except(actualFiles.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(actualFiles.Keys.Except(expectedFiles.Keys, StringComparer.OrdinalIgnoreCase));

        foreach (var (relativePath, expectedContent) in expectedFiles)
        {
            Assert.True(actualFiles.TryGetValue(relativePath, out var actualContent), $"Missing file '{relativePath}' in actual output.");
            Assert.Equal(expectedContent, actualContent);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadAllFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => NormalizeRelativePath(Path.GetRelativePath(root, path)),
                path => NormalizeContent(path, File.ReadAllText(path)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string NormalizeContent(string path, string content)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(content);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd();
        }

        return content.Replace("\r\n", "\n").TrimEnd();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
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
                f.Rationales.ToArray())).ToArray());

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
        IReadOnlyList<PolicyDecisionLogForeignKey> ForeignKeys);

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
}
