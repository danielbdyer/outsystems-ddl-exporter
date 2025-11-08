using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtTelemetryPackagingStep : IBuildSsdtStep<StaticSeedsGenerated, TelemetryPackaged>
{
    private const string PackageFileName = "pipeline-telemetry.zip";

    public Task<Result<TelemetryPackaged>> ExecuteAsync(
        StaticSeedsGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var outputDirectory = state.Request.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Output directory must be provided before telemetry packaging.");
        }

        var normalizedOutput = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(normalizedOutput);

        var artifacts = new (string Name, string Path)[]
        {
            ("manifest", Path.Combine(normalizedOutput, "manifest.json")),
            ("decisionLog", Path.GetFullPath(state.DecisionLogPath)),
            ("opportunities.safe", Path.GetFullPath(state.OpportunityArtifacts.SafeScriptPath)),
            ("opportunities.remediation", Path.GetFullPath(state.OpportunityArtifacts.RemediationScriptPath)),
        };

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(artifact.Path) || !File.Exists(artifact.Path))
            {
                return Task.FromResult(Result<TelemetryPackaged>.Failure(ValidationError.Create(
                    "pipeline.buildSsdt.telemetry.missingArtifact",
                    $"Expected telemetry artifact '{artifact.Name}' was not found at '{artifact.Path}'.")));
            }
        }

        var packagePath = Path.Combine(normalizedOutput, PackageFileName);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        var packagedEntries = new List<string>(artifacts.Length);
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            foreach (var artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativeEntry = GetRelativePath(normalizedOutput, artifact.Path);
                packagedEntries.Add(relativeEntry);
                archive.CreateEntryFromFile(artifact.Path, relativeEntry, CompressionLevel.Optimal);
            }
        }

        var packagePaths = ImmutableArray.Create(packagePath);
        state.Log.Record(
            "pipeline.execution",
            "Packaged pipeline telemetry artifacts.",
            new PipelineLogMetadataBuilder()
                .WithPath("telemetry.package", packagePath)
                .WithValue("telemetry.entries", string.Join(";", packagedEntries))
                .Build());

        var nextState = new TelemetryPackaged(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            state.Manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            state.SqlValidation,
            state.StaticSeedScriptPaths,
            packagePaths);

        return Task.FromResult(Result<TelemetryPackaged>.Success(nextState));
    }

    private static string GetRelativePath(string root, string path)
    {
        var absoluteRoot = Path.GetFullPath(root);
        var absolutePath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(absoluteRoot, absolutePath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return Path.GetFileName(absolutePath);
        }

        return relative.Replace('\\', '/');
    }
}
