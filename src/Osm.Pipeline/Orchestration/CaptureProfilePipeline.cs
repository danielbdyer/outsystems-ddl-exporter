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
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    SupplementalModelOptions SupplementalModels,
    string ProfilerProvider,
    string? FixtureProfilePath,
    ResolvedSqlOptions SqlOptions,
    TighteningOptions TighteningOptions,
    TypeMappingPolicy TypeMappingPolicy,
    SmoBuildOptions SmoOptions,
    string OutputDirectory,
    SqlMetadataLog? SqlMetadataLog = null) : ICommand<CaptureProfilePipelineResult>;

public sealed record CaptureProfilePipelineResult(
    ProfileSnapshot Profile,
    CaptureProfileManifest Manifest,
    string ProfilePath,
    string ManifestPath,
    ImmutableArray<ProfilingInsight> Insights,
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
    private readonly IPipelineBootstrapTelemetryFactory _telemetryFactory;

    public CaptureProfilePipeline(
        TimeProvider timeProvider,
        IPipelineBootstrapper bootstrapper,
        IDataProfilerFactory profilerFactory,
        IProfileSnapshotSerializer profileSerializer,
        IPipelineBootstrapTelemetryFactory telemetryFactory)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _profilerFactory = profilerFactory ?? throw new ArgumentNullException(nameof(profilerFactory));
        _profileSerializer = profileSerializer ?? throw new ArgumentNullException(nameof(profileSerializer));
        _telemetryFactory = telemetryFactory ?? throw new ArgumentNullException(nameof(telemetryFactory));
    }

    public async Task<Result<CaptureProfilePipelineResult>> HandleAsync(
        CaptureProfilePipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ModelPath))
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
        var scope = ModelExecutionScope.Create(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            request.TighteningOptions,
            request.SmoOptions);
        var telemetry = _telemetryFactory.Create(
            scope,
            new PipelineCommandDescriptor(
                "Received profile capture request.",
                "Capturing profiling snapshot.",
                "Captured profiling snapshot."),
            new PipelineBootstrapTelemetryExtras(
                ProfilerProvider: request.ProfilerProvider,
                FixtureProfilePath: request.FixtureProfilePath));
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (model, token) => CaptureProfileAsync(request, model, token));

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
            log.Build(),
            bootstrap.Warnings.ToImmutableArray());
    }

    private async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
        CaptureProfilePipelineRequest request,
        OsmModel model,
        CancellationToken cancellationToken)
    {
        var profilerRequest = new BuildSsdtPipelineRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.OutputDirectory,
            request.TighteningOptions,
            request.SupplementalModels,
            request.ProfilerProvider,
            request.FixtureProfilePath,
            request.SqlOptions,
            request.SmoOptions,
            request.TypeMappingPolicy,
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
            request.ModuleFilter.HasFilter,
            request.ModuleFilter.Modules.Select(module => module.Value).ToArray(),
            request.ModuleFilter.IncludeSystemModules,
            request.ModuleFilter.IncludeInactiveModules);

        var supplementalSummary = new CaptureProfileSupplementalSummary(
            request.SupplementalModels.IncludeUsers,
            request.SupplementalModels.Paths.ToArray());

        var snapshotSummary = new CaptureProfileSnapshotSummary(
            bootstrap.Profile.Columns.Length,
            bootstrap.Profile.UniqueCandidates.Length,
            bootstrap.Profile.CompositeUniqueCandidates.Length,
            bootstrap.Profile.ForeignKeys.Length,
            bootstrap.FilteredModel.Modules.Length);

        var insights = bootstrap.Insights
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
            .ToArray();

        return new CaptureProfileManifest(
            request.ModelPath,
            profilePath,
            request.ProfilerProvider,
            moduleSummary,
            supplementalSummary,
            snapshotSummary,
            insights,
            bootstrap.Warnings.ToArray(),
            capturedAt);
    }
}
