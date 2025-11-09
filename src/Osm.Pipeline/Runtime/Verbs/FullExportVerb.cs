using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Runtime.Verbs;

public sealed record FullExportVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public FullExportOverrides Overrides { get; init; } = FullExportOverrides.Empty;
    public ModuleFilterOverrides ModuleFilter { get; init; } = new(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
    public SqlOptionsOverrides Sql { get; init; } = new(null, null, null, null, null, null, null, null, null);
    public CacheOptionsOverrides Cache { get; init; } = new(null, null);
    public TighteningOverrides? Tightening { get; init; }
}

public sealed record FullExportVerbResult(
    CliConfigurationContext Configuration,
    FullExportApplicationResult ApplicationResult);

public sealed class FullExportVerb : PipelineVerb<FullExportVerbOptions, FullExportVerbResult>
{
    public const string VerbName = "full-export";
    public const string RunManifestFileName = "full-export.manifest.json";

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<FullExportApplicationInput, FullExportApplicationResult> _applicationService;
    private readonly TimeProvider _timeProvider;

    public FullExportVerb(
        ICliConfigurationService configurationService,
        IApplicationService<FullExportApplicationInput, FullExportApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public override string Name => VerbName;

    protected override async Task<Result<FullExportVerbResult>> ExecuteAsync(
        FullExportVerbOptions options,
        CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var configurationResult = await _configurationService
            .LoadAsync(options.ConfigurationPath, cancellationToken)
            .ConfigureAwait(false);

        if (configurationResult.IsFailure)
        {
            return Result<FullExportVerbResult>.Failure(configurationResult.Errors);
        }

        var overrides = options.Overrides ?? FullExportOverrides.Empty;
        var input = new FullExportApplicationInput(
            configurationResult.Value,
            overrides,
            options.ModuleFilter,
            options.Sql,
            options.Cache,
            options.Tightening);

        var applicationResult = await _applicationService
            .RunAsync(input, cancellationToken)
            .ConfigureAwait(false);

        if (applicationResult.IsFailure)
        {
            return Result<FullExportVerbResult>.Failure(applicationResult.Errors);
        }

        return new FullExportVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(FullExportVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var application = result.ApplicationResult;
        var build = application.Build;
        var buildPipeline = build.PipelineResult;

        if (!string.IsNullOrWhiteSpace(application.Extraction.OutputPath))
        {
            artifacts.Add(new PipelineArtifact("model-json", application.Extraction.OutputPath, "application/json"));
        }

        var profilePipeline = application.Profile.PipelineResult;
        if (!string.IsNullOrWhiteSpace(profilePipeline.ProfilePath))
        {
            artifacts.Add(new PipelineArtifact("profile", profilePipeline.ProfilePath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(profilePipeline.ManifestPath))
        {
            artifacts.Add(new PipelineArtifact("profile-manifest", profilePipeline.ManifestPath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(buildPipeline.DecisionLogPath))
        {
            artifacts.Add(new PipelineArtifact("decision-log", buildPipeline.DecisionLogPath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(buildPipeline.OpportunitiesPath))
        {
            artifacts.Add(new PipelineArtifact("opportunities", buildPipeline.OpportunitiesPath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(buildPipeline.ValidationsPath))
        {
            artifacts.Add(new PipelineArtifact("validations", buildPipeline.ValidationsPath, "application/json"));
        }

        if (!string.IsNullOrWhiteSpace(buildPipeline.SafeScriptPath))
        {
            artifacts.Add(new PipelineArtifact("opportunity-safe", buildPipeline.SafeScriptPath, "application/sql"));
        }

        if (!string.IsNullOrWhiteSpace(buildPipeline.RemediationScriptPath))
        {
            artifacts.Add(new PipelineArtifact("opportunity-remediation", buildPipeline.RemediationScriptPath, "application/sql"));
        }

        if (!buildPipeline.StaticSeedScriptPaths.IsDefaultOrEmpty)
        {
            foreach (var seedPath in buildPipeline.StaticSeedScriptPaths)
            {
                if (!string.IsNullOrWhiteSpace(seedPath))
                {
                    artifacts.Add(new PipelineArtifact("static-seed", seedPath, "application/sql"));
                }
            }
        }

        if (!buildPipeline.TelemetryPackagePaths.IsDefaultOrEmpty)
        {
            foreach (var packagePath in buildPipeline.TelemetryPackagePaths)
            {
                if (!string.IsNullOrWhiteSpace(packagePath))
                {
                    artifacts.Add(new PipelineArtifact("telemetry-package", packagePath, "application/zip"));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(build.OutputDirectory))
        {
            artifacts.Add(new PipelineArtifact("manifest", Path.Combine(build.OutputDirectory, "manifest.json"), "application/json"));
        }

        if (buildPipeline.EvidenceCache is { } cache)
        {
            artifacts.Add(new PipelineArtifact("evidence-cache", cache.CacheDirectory));
            artifacts.Add(new PipelineArtifact("evidence-manifest", Path.Combine(cache.CacheDirectory, "manifest.json"), "application/json"));
        }

        var manifestArtifact = PersistRunManifest(result, artifacts);
        if (manifestArtifact is not null)
        {
            artifacts.Add(manifestArtifact);
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        FullExportVerbOptions options,
        Result<FullExportVerbResult> outcome)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            builder["configOverride"] = options.ConfigurationPath;
        }

        if (outcome.IsSuccess)
        {
            var result = outcome.Value;
            var application = result.ApplicationResult;
            builder["configPath"] = result.Configuration.ConfigPath;
            builder["build.outputDirectory"] = application.Build.OutputDirectory;
            builder["build.modelPath"] = application.Build.ModelPath;
            builder["build.profilePath"] = application.Build.ProfilePath;
            builder["build.profilerProvider"] = application.Build.ProfilerProvider;
            builder["profile.outputDirectory"] = application.Profile.OutputDirectory;
            builder["profile.profilerProvider"] = application.Profile.ProfilerProvider;
            builder["extract.outputPath"] = application.Extraction.OutputPath;
            builder["extract.extractedAtUtc"] = application.Extraction.ExtractionResult.ExtractedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        return builder.ToImmutable();
    }

    private PipelineArtifact? PersistRunManifest(FullExportVerbResult result, List<PipelineArtifact> artifacts)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (artifacts is null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        var buildOutput = result.ApplicationResult.Build.OutputDirectory;
        var manifestPath = ResolveManifestPath(buildOutput);
        var manifestArtifact = new PipelineArtifact("full-export-manifest", manifestPath, "application/json");

        var artifactSnapshot = new List<PipelineArtifact>(artifacts.Count + 1);
        artifactSnapshot.AddRange(artifacts);
        artifactSnapshot.Add(manifestArtifact);

        var manifest = FullExportRunManifest.Create(result, artifactSnapshot, _timeProvider);

        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        using (var stream = new FileStream(
                   manifestPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            JsonSerializer.Serialize(stream, manifest, ManifestSerializerOptions);
        }

        return manifestArtifact;
    }

    private static string ResolveManifestPath(string? buildOutput)
    {
        if (!string.IsNullOrWhiteSpace(buildOutput))
        {
            return Path.Combine(buildOutput, RunManifestFileName);
        }

        return Path.Combine(Environment.CurrentDirectory, RunManifestFileName);
    }
}
