using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Json;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Smo;

namespace Osm.Pipeline.Orchestration;

public sealed record CaptureProfilePipelineRequest(
    ModelExecutionScope Scope,
    string ProfilerProvider,
    string OutputDirectory,
    string? FixtureProfilePath,
    SqlMetadataLog? SqlMetadataLog = null) : ICommand<CaptureProfilePipelineResult>;

public sealed record CaptureProfilePipelineResult(
    ProfileSnapshot Profile,
    CaptureProfileManifest Manifest,
    string ProfilePath,
    string ManifestPath,
    ImmutableArray<ProfilingInsight> Insights,
    ImmutableArray<ProfilingCoverageAnomaly> CoverageAnomalies,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);

public sealed record CaptureProfileManifest(
    string ModelPath,
    string ProfilePath,
    string ProfilerProvider,
    CaptureProfileModuleSummary ModuleFilter,
    CaptureProfileSupplementalSummary SupplementalModels,
    CaptureProfileSnapshotSummary Snapshot,
    IReadOnlyList<CaptureProfileInsight> Insights,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CaptureProfileCoverageAnomaly> CoverageAnomalies,
    DateTimeOffset CapturedAtUtc);

public sealed record CaptureProfileModuleSummary(
    bool HasFilter,
    IReadOnlyList<string> Modules,
    bool IncludeSystemModules,
    bool IncludeInactiveModules);

public sealed record CaptureProfileSupplementalSummary(
    bool IncludeUsers,
    IReadOnlyList<string> Paths);

public sealed record CaptureProfileSnapshotSummary(
    int ColumnCount,
    int UniqueCandidateCount,
    int CompositeUniqueCandidateCount,
    int ForeignKeyCount,
    int ModuleCount);

public sealed record CaptureProfileInsight(
    string Severity,
    string Category,
    string Message,
    CaptureProfileInsightCoordinate? Coordinate);

public sealed record CaptureProfileInsightCoordinate(
    string Schema,
    string Table,
    string? Column,
    string? RelatedSchema,
    string? RelatedTable,
    string? RelatedColumn);

public sealed record CaptureProfileCoverageAnomaly(
    string Type,
    string Message,
    string Remediation,
    CaptureProfileInsightCoordinate Coordinate,
    IReadOnlyList<string> Columns,
    string Outcome);

public sealed class CaptureProfilePipeline : ICommandHandler<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly TimeProvider _timeProvider;
    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly IDataProfilerFactory _profilerFactory;
    private readonly IProfileSnapshotSerializer _profileSerializer;

    public CaptureProfilePipeline(
        TimeProvider timeProvider,
        IPipelineBootstrapper bootstrapper,
        IDataProfilerFactory profilerFactory,
        IProfileSnapshotSerializer profileSerializer)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _profilerFactory = profilerFactory ?? throw new ArgumentNullException(nameof(profilerFactory));
        _profileSerializer = profileSerializer ?? throw new ArgumentNullException(nameof(profileSerializer));
    }

    public async Task<Result<CaptureProfilePipelineResult>> HandleAsync(
        CaptureProfilePipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Scope.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.captureProfile.model.missing",
                "Model path must be provided for profile capture.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return ValidationError.Create(
                "pipeline.captureProfile.output.missing",
                "Output directory must be provided for profile capture.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        var telemetry = CreateTelemetry(request);
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.Scope.ModelPath,
            request.Scope.ModuleFilter,
            request.Scope.SupplementalModels,
            telemetry,
            (model, token) => CaptureProfileAsync(request, model, token),
            request.Scope.InlineModel,
            request.Scope.ModelWarnings);

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);

        if (bootstrapResult.IsFailure)
        {
            return Result<CaptureProfilePipelineResult>.Failure(bootstrapResult.Errors);
        }

        var bootstrap = bootstrapResult.Value;

        Result<CaptureProfilePipelineResult> PersistFailure(string code, string message)
            => Result<CaptureProfilePipelineResult>.Failure(ValidationError.Create(code, message));

        try
        {
            Directory.CreateDirectory(request.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PersistFailure(
                "pipeline.captureProfile.output.createFailed",
                $"Failed to create output directory '{request.OutputDirectory}': {ex.Message}");
        }

        var profilePath = Path.Combine(request.OutputDirectory, "profile.json");
        try
        {
            await using var stream = new FileStream(
                profilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await _profileSerializer.SerializeAsync(bootstrap.Profile, stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PersistFailure(
                "pipeline.captureProfile.persist.failed",
                $"Failed to write profiling snapshot to '{profilePath}': {ex.Message}");
        }

        var manifest = BuildManifest(request, bootstrap, profilePath);
        var manifestPath = Path.Combine(request.OutputDirectory, "manifest.json");

        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await JsonSerializer.SerializeAsync(stream, manifest, ManifestSerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PersistFailure(
                "pipeline.captureProfile.manifest.failed",
                $"Failed to write profiling manifest to '{manifestPath}': {ex.Message}");
        }

        log.Record(
            "profiling.persisted",
            "Persisted profiling snapshot and manifest.",
            new PipelineLogMetadataBuilder()
                .WithPath("profile", profilePath)
                .WithPath("manifest", manifestPath)
                .WithCount("profiles.columns", bootstrap.Profile.Columns.Length)
                .Build());

        return new CaptureProfilePipelineResult(
            bootstrap.Profile,
            manifest,
            profilePath,
            manifestPath,
            bootstrap.Insights,
            bootstrap.Profile.CoverageAnomalies,
            log.Build(),
            bootstrap.Warnings.ToImmutableArray());
    }

    private PipelineBootstrapTelemetry CreateTelemetry(CaptureProfilePipelineRequest request)
    {
        return new PipelineBootstrapTelemetry(
            "Received profile capture request.",
            new PipelineLogMetadataBuilder()
                .WithPath("model", request.Scope.ModelPath)
                .WithValue("profiling.provider", request.ProfilerProvider)
                .WithFlag("moduleFilter.hasFilter", request.Scope.ModuleFilter.HasFilter)
                .WithCount("moduleFilter.modules", request.Scope.ModuleFilter.Modules.Length)
                .WithFlag("supplemental.includeUsers", request.Scope.SupplementalModels.IncludeUsers)
                .WithCount("supplemental.paths", request.Scope.SupplementalModels.Paths.Count)
                .Build(),
            "Capturing profiling snapshot.",
            new PipelineLogMetadataBuilder()
                .WithValue("profiling.provider", request.ProfilerProvider)
                .WithPath("profiling.fixture", request.FixtureProfilePath)
                .Build(),
            "Captured profiling snapshot.");
    }

    private async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
        CaptureProfilePipelineRequest request,
        OsmModel model,
        CancellationToken cancellationToken)
    {
        var profilerRequest = new BuildSsdtPipelineRequest(
            request.Scope with { ProfilePath = request.FixtureProfilePath },
            request.OutputDirectory,
            request.ProfilerProvider,
            EvidenceCache: null,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            request.SqlMetadataLog);

        var profilerResult = _profilerFactory.Create(profilerRequest, model);
        if (profilerResult.IsFailure)
        {
            return Result<ProfileSnapshot>.Failure(profilerResult.Errors);
        }

        return await profilerResult.Value.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    private CaptureProfileManifest BuildManifest(
        CaptureProfilePipelineRequest request,
        PipelineBootstrapContext bootstrap,
        string profilePath)
    {
        var capturedAt = _timeProvider.GetUtcNow();

        var moduleSummary = new CaptureProfileModuleSummary(
            request.Scope.ModuleFilter.HasFilter,
            request.Scope.ModuleFilter.Modules.Select(module => module.Value).ToArray(),
            request.Scope.ModuleFilter.IncludeSystemModules,
            request.Scope.ModuleFilter.IncludeInactiveModules);

        var supplementalSummary = new CaptureProfileSupplementalSummary(
            request.Scope.SupplementalModels.IncludeUsers,
            request.Scope.SupplementalModels.Paths.ToArray());

        var snapshotSummary = new CaptureProfileSnapshotSummary(
            bootstrap.Profile.Columns.Length,
            bootstrap.Profile.UniqueCandidates.Length,
            bootstrap.Profile.CompositeUniqueCandidates.Length,
            bootstrap.Profile.ForeignKeys.Length,
            bootstrap.FilteredModel.Modules.Length);

        var manifestInsights = bootstrap.Insights
            .Select(insight => new CaptureProfileInsight(
                insight.Severity.ToString(),
                insight.Category.ToString(),
                insight.Message,
                insight.Coordinate is null
                    ? null
                    : new CaptureProfileInsightCoordinate(
                        insight.Coordinate.Schema.Value,
                        insight.Coordinate.Table.Value,
                        insight.Coordinate.Column?.Value,
                        insight.Coordinate.RelatedSchema?.Value,
                        insight.Coordinate.RelatedTable?.Value,
                        insight.Coordinate.RelatedColumn?.Value)))
            .ToList();

        var manifestWarnings = new List<string>(bootstrap.Warnings);
        var coverageEntries = new List<CaptureProfileCoverageAnomaly>();

        foreach (var anomaly in bootstrap.Profile.CoverageAnomalies)
        {
            var coordinate = new CaptureProfileInsightCoordinate(
                anomaly.Coordinate.Schema.Value,
                anomaly.Coordinate.Table.Value,
                anomaly.Coordinate.Column?.Value,
                anomaly.Coordinate.RelatedSchema?.Value,
                anomaly.Coordinate.RelatedTable?.Value,
                anomaly.Coordinate.RelatedColumn?.Value);

            coverageEntries.Add(new CaptureProfileCoverageAnomaly(
                anomaly.Type.ToString(),
                anomaly.Message,
                anomaly.RemediationHint,
                coordinate,
                anomaly.Columns,
                anomaly.Outcome.ToString()));

            manifestWarnings.Add($"Coverage anomaly: {anomaly.Message} Remediation: {anomaly.RemediationHint}");

            manifestInsights.Add(new CaptureProfileInsight(
                ProfilingInsightSeverity.Warning.ToString(),
                "Coverage",
                $"{anomaly.Message} Remediation: {anomaly.RemediationHint}",
                coordinate));
        }

        return new CaptureProfileManifest(
            request.Scope.ModelPath,
            profilePath,
            request.ProfilerProvider,
            moduleSummary,
            supplementalSummary,
            snapshotSummary,
            manifestInsights,
            manifestWarnings,
            coverageEntries,
            capturedAt);
    }
}
