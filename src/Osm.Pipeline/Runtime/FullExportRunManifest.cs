using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Osm.Pipeline.Application;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Pipeline.Runtime;

public sealed record FullExportRunManifest(
    DateTimeOffset GeneratedAtUtc,
    string? ConfigurationPath,
    ImmutableArray<FullExportStageManifest> Stages,
    ImmutableArray<FullExportManifestArtifact> Artifacts,
    ImmutableArray<string> Warnings)
{
    public static FullExportRunManifest Empty => new(
        DateTimeOffset.MinValue,
        null,
        ImmutableArray<FullExportStageManifest>.Empty,
        ImmutableArray<FullExportManifestArtifact>.Empty,
        ImmutableArray<string>.Empty);

    internal static FullExportRunManifest Create(
        FullExportVerbResult result,
        IEnumerable<PipelineArtifact> artifacts,
        TimeProvider timeProvider)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (artifacts is null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        var artifactArray = artifacts
            .Where(static artifact => artifact is not null)
            .Select(static artifact => new FullExportManifestArtifact(artifact.Name, artifact.Path, artifact.ContentType))
            .ToImmutableArray();

        var extractionStage = CreateExtractionStage(result.ApplicationResult.Extraction);
        var profileStage = CreateProfileStage(result.ApplicationResult.Profile);
        var buildStage = CreateBuildStage(result.ApplicationResult.Build);

        var stages = ImmutableArray.Create(extractionStage, profileStage, buildStage);
        var warnings = stages
            .SelectMany(static stage => stage.Warnings)
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        return new FullExportRunManifest(
            timeProvider.GetUtcNow(),
            result.Configuration.ConfigPath,
            stages,
            artifactArray,
            warnings);
    }

    private static FullExportStageManifest CreateExtractionStage(ExtractModelApplicationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var artifacts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(result.OutputPath))
        {
            artifacts["modelJson"] = Path.GetFullPath(result.OutputPath);
        }

        var payloadPath = result.ExtractionResult.JsonPayload.FilePath;
        if (!string.IsNullOrWhiteSpace(payloadPath))
        {
            artifacts["payload"] = Path.GetFullPath(payloadPath);
        }

        return new FullExportStageManifest(
            Name: "extract-model",
            StartedAtUtc: null,
            CompletedAtUtc: result.ExtractionResult.ExtractedAtUtc,
            Duration: null,
            Warnings: result.ExtractionResult.Warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Select(static warning => warning!)
                .ToImmutableArray(),
            Artifacts: artifacts.ToImmutable());
    }

    private static FullExportStageManifest CreateProfileStage(CaptureProfileApplicationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var pipelineResult = result.PipelineResult;
        var timing = ComputeTiming(pipelineResult.ExecutionLog);

        var artifacts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pipelineResult.ProfilePath))
        {
            artifacts["profile"] = Path.GetFullPath(pipelineResult.ProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(pipelineResult.ManifestPath))
        {
            artifacts["manifest"] = Path.GetFullPath(pipelineResult.ManifestPath);
        }

        return new FullExportStageManifest(
            Name: "capture-profile",
            StartedAtUtc: timing.StartedAtUtc,
            CompletedAtUtc: timing.CompletedAtUtc,
            Duration: timing.Duration,
            Warnings: pipelineResult.Warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Select(static warning => warning!)
                .ToImmutableArray(),
            Artifacts: artifacts.ToImmutable());
    }

    private static FullExportStageManifest CreateBuildStage(BuildSsdtApplicationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var pipelineResult = result.PipelineResult;
        var timing = ComputeTiming(pipelineResult.ExecutionLog);

        var artifacts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(result.OutputDirectory))
        {
            artifacts["outputDirectory"] = Path.GetFullPath(result.OutputDirectory);
        }

        AddPathIfPresent(artifacts, "manifest", CombineIfPresent(result.OutputDirectory, "manifest.json"));
        AddPathIfPresent(artifacts, "decisionLog", pipelineResult.DecisionLogPath);
        AddPathIfPresent(artifacts, "opportunities", pipelineResult.OpportunitiesPath);
        AddPathIfPresent(artifacts, "validations", pipelineResult.ValidationsPath);
        AddPathIfPresent(artifacts, "safeScript", pipelineResult.SafeScriptPath);
        AddPathIfPresent(artifacts, "remediationScript", pipelineResult.RemediationScriptPath);

        if (!pipelineResult.StaticSeedScriptPaths.IsDefaultOrEmpty)
        {
            artifacts["staticSeedScripts"] = string.Join(";", pipelineResult.StaticSeedScriptPaths);
        }

        if (!pipelineResult.TelemetryPackagePaths.IsDefaultOrEmpty)
        {
            artifacts["telemetryPackages"] = string.Join(";", pipelineResult.TelemetryPackagePaths);
        }

        var warnings = ImmutableArray.CreateBuilder<string>();
        if (!pipelineResult.Warnings.IsDefaultOrEmpty)
        {
            warnings.AddRange(pipelineResult.Warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning))!);
        }

        if (!result.ModelExtractionWarnings.IsDefaultOrEmpty)
        {
            warnings.AddRange(result.ModelExtractionWarnings.Where(static warning => !string.IsNullOrWhiteSpace(warning))!);
        }

        return new FullExportStageManifest(
            Name: "build-ssdt",
            StartedAtUtc: timing.StartedAtUtc,
            CompletedAtUtc: timing.CompletedAtUtc,
            Duration: timing.Duration,
            Warnings: warnings.ToImmutable(),
            Artifacts: artifacts.ToImmutable());
    }

    private static void AddPathIfPresent(
        ImmutableDictionary<string, string?>.Builder artifacts,
        string key,
        string? path)
    {
        if (artifacts is null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must be provided.", nameof(key));
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            artifacts[key] = Path.GetFullPath(path!);
        }
    }

    private static string? CombineIfPresent(string? directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return Path.Combine(directory, fileName);
    }

    internal static StageTiming ComputeTiming(PipelineExecutionLog log)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (log.Entries.Count == 0)
        {
            return StageTiming.Empty;
        }

        var ordered = log.Entries
            .Where(static entry => entry is not null)
            .OrderBy(static entry => entry.TimestampUtc)
            .ToArray();

        if (ordered.Length == 0)
        {
            return StageTiming.Empty;
        }

        var started = ordered.First().TimestampUtc;
        var completed = ordered.Last().TimestampUtc;
        var duration = completed >= started ? completed - started : (TimeSpan?)null;
        return new StageTiming(started, completed, duration);
    }

    internal sealed record StageTiming(
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        TimeSpan? Duration)
    {
        public static StageTiming Empty { get; } = new(null, null, null);
    }
}

public sealed record FullExportStageManifest(
    string Name,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TimeSpan? Duration,
    ImmutableArray<string> Warnings,
    ImmutableDictionary<string, string?> Artifacts);

public sealed record FullExportManifestArtifact(
    string Name,
    string Path,
    string? ContentType);
