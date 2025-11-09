using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;
using Xunit;

namespace Osm.Pipeline.Tests.Runtime;

public sealed class FullExportRunManifestTests
{
    [Fact]
    public void ComputeTiming_ReturnsExpectedDuration()
    {
        var entries = new List<PipelineLogEntry>
        {
            new(DateTimeOffset.Parse("2024-03-01T12:00:00Z"), "start", "started", ImmutableDictionary<string, string?>.Empty),
            new(DateTimeOffset.Parse("2024-03-01T12:00:05Z"), "mid", "running", ImmutableDictionary<string, string?>.Empty),
            new(DateTimeOffset.Parse("2024-03-01T12:00:09Z"), "end", "completed", ImmutableDictionary<string, string?>.Empty)
        };

        var log = new PipelineExecutionLog(entries);

        var timing = FullExportRunManifest.ComputeTiming(log);

        Assert.Equal(entries[0].TimestampUtc, timing.StartedAtUtc);
        Assert.Equal(entries[^1].TimestampUtc, timing.CompletedAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(9), timing.Duration);
    }

    [Fact]
    public void SerializeManifest_ProducesExpectedShape()
    {
        var generatedAt = DateTimeOffset.Parse("2024-03-05T10:30:00Z");
        var stages = ImmutableArray.Create(
            new FullExportStageManifest(
                "extract-model",
                generatedAt,
                generatedAt + TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                ImmutableArray.Create("extraction-warning"),
                ImmutableDictionary<string, string?>.Empty),
            new FullExportStageManifest(
                "build-ssdt",
                generatedAt + TimeSpan.FromMinutes(1),
                generatedAt + TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(1),
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string?>.Empty));

        var artifacts = ImmutableArray.Create(
            new FullExportManifestArtifact("model-json", "/tmp/model.json", "application/json"),
            new FullExportManifestArtifact("full-export-manifest", "/tmp/full-export.manifest.json", "application/json"));

        var manifest = new FullExportRunManifest(
            generatedAt,
            "config/full-export.json",
            stages,
            artifacts,
            ImmutableArray.Create("extraction-warning"));

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("config/full-export.json", root.GetProperty("ConfigurationPath").GetString());

        var stagesElement = root.GetProperty("Stages");
        Assert.Equal(2, stagesElement.GetArrayLength());

        var firstStage = stagesElement[0];
        Assert.Equal("extract-model", firstStage.GetProperty("Name").GetString());
        Assert.Equal("00:00:05", firstStage.GetProperty("Duration").GetString());

        var warnings = firstStage.GetProperty("Warnings");
        Assert.Contains("extraction-warning", warnings.EnumerateArray().Select(static element => element.GetString()));

        var artifactsElement = root.GetProperty("Artifacts");
        Assert.Equal(2, artifactsElement.GetArrayLength());
        Assert.Equal("model-json", artifactsElement[0].GetProperty("Name").GetString());
        Assert.Equal("/tmp/model.json", artifactsElement[0].GetProperty("Path").GetString());
        Assert.Equal("full-export-manifest", artifactsElement[1].GetProperty("Name").GetString());
    }
}
