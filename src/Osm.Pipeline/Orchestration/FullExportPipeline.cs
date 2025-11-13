using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;

namespace Osm.Pipeline.Orchestration;

public sealed record SchemaApplyOptions(
    bool Enabled,
    string? ConnectionString,
    SqlAuthenticationSettings Authentication,
    int? CommandTimeoutSeconds,
    bool ApplySafeScript = true,
    bool ApplyStaticSeeds = true,
    StaticSeedSynchronizationMode StaticSeedSynchronizationMode = StaticSeedSynchronizationMode.NonDestructive)
{
    public static SchemaApplyOptions Disabled { get; } = new(
        false,
        null,
        new SqlAuthenticationSettings(null, null, null, null),
        null,
        ApplySafeScript: false,
        ApplyStaticSeeds: false,
        StaticSeedSynchronizationMode.NonDestructive);
}

public sealed record SchemaApplyResult(
    bool Attempted,
    bool SafeScriptApplied,
    bool StaticSeedsApplied,
    ImmutableArray<string> AppliedScripts,
    ImmutableArray<string> AppliedSeedScripts,
    ImmutableArray<string> SkippedScripts,
    ImmutableArray<string> Warnings,
    int PendingRemediationCount,
    string? SafeScriptPath,
    string? RemediationScriptPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    TimeSpan Duration,
    StaticSeedSynchronizationMode StaticSeedSynchronizationMode,
    StaticSeedValidationSummary StaticSeedValidation);

public sealed record UatUsersPipelineOptions(
    bool Enabled,
    string? ConnectionString,
    string? UserSchema,
    string? UserTable,
    string? UserIdColumn,
    IReadOnlyList<string>? IncludeColumns,
    string? UserMapPath,
    string? UatUserInventoryPath,
    string? QaUserInventoryPath,
    string? SnapshotPath,
    string? UserEntityIdentifier);

public sealed record FullExportPipelineRequest(
    ExtractModelPipelineRequest ExtractModel,
    CaptureProfilePipelineRequest CaptureProfile,
    BuildSsdtPipelineRequest Build,
    SchemaApplyOptions ApplyOptions,
    UatUsersPipelineOptions? UatUsers = null) : ICommand<FullExportPipelineResult>;

public sealed record FullExportPipelineResult(
    ModelExtractionResult Extraction,
    CaptureProfilePipelineResult Profile,
    BuildSsdtPipelineResult Build,
    SchemaApplyResult Apply,
    PipelineExecutionLog ExecutionLog,
    UatUsersApplicationResult UatUsers);

public sealed class FullExportPipeline : ICommandHandler<FullExportPipelineRequest, FullExportPipelineResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly SchemaApplyOrchestrator _schemaApplyOrchestrator;
    private readonly IUatUsersPipelineRunner _uatUsersRunner;
    private readonly FullExportCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FullExportPipeline> _logger;

    public FullExportPipeline(
        ICommandDispatcher dispatcher,
        SchemaApplyOrchestrator schemaApplyOrchestrator,
        IUatUsersPipelineRunner uatUsersRunner,
        FullExportCoordinator coordinator,
        TimeProvider timeProvider,
        ILogger<FullExportPipeline> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _schemaApplyOrchestrator = schemaApplyOrchestrator ?? throw new ArgumentNullException(nameof(schemaApplyOrchestrator));
        _uatUsersRunner = uatUsersRunner ?? throw new ArgumentNullException(nameof(uatUsersRunner));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<FullExportPipelineResult>> HandleAsync(
        FullExportPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        log.Record("fullExport.started", "Full export pipeline started.");

        var extractStage = new Func<CancellationToken, Task<Result<ModelExtractionResult>>>(async ct =>
        {
            var extractionResult = await _dispatcher
                .DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(request.ExtractModel, ct)
                .ConfigureAwait(false);

            if (extractionResult.IsFailure)
            {
                LogFailure(log, "fullExport.extract.failed", "Model extraction failed.", extractionResult.Errors);
                return extractionResult;
            }

            var extraction = extractionResult.Value;
            log.Record(
                "fullExport.extract.completed",
                "Model extraction completed successfully.",
                new PipelineLogMetadataBuilder()
                    .WithTimestamp("extractedAtUtc", extraction.ExtractedAtUtc)
                    .WithCount("warnings", extraction.Warnings.Count)
                    .Build());

            return extractionResult;
        });

        var profileStage = new Func<ModelExtractionResult, CancellationToken, Task<Result<CaptureProfilePipelineResult>>>(async (extraction, ct) =>
        {
            var profileResult = await _dispatcher
                .DispatchAsync<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>(request.CaptureProfile, ct)
                .ConfigureAwait(false);

            if (profileResult.IsFailure)
            {
                LogFailure(log, "fullExport.profile.failed", "Profile capture failed.", profileResult.Errors);
                return profileResult;
            }

            var profile = profileResult.Value;
            log.Record(
                "fullExport.profile.completed",
                "Profile capture completed successfully.",
                new PipelineLogMetadataBuilder()
                    .WithCount("warnings", profile.Warnings.Length)
                    .WithPath("profile.path", profile.ProfilePath)
                    .Build());

            return profileResult;
        });

        var buildStage = new Func<ModelExtractionResult, CaptureProfilePipelineResult, CancellationToken, Task<Result<BuildSsdtPipelineResult>>>(
            async (extraction, profile, ct) =>
            {
                var buildResult = await _dispatcher
                    .DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(request.Build, ct)
                    .ConfigureAwait(false);

                if (buildResult.IsFailure)
                {
                    LogFailure(log, "fullExport.build.failed", "SSDT emission failed.", buildResult.Errors);
                    return buildResult;
                }

                var build = buildResult.Value;
                log.Record(
                    "fullExport.build.completed",
                    "SSDT emission completed successfully.",
                    new PipelineLogMetadataBuilder()
                        .WithPath("paths.output", request.Build.OutputDirectory)
                        .WithPath("paths.safeScript", build.SafeScriptPath)
                        .WithPath("paths.remediationScript", build.RemediationScriptPath)
                        .WithCount("opportunities.total", build.Opportunities.TotalCount)
                        .Build());

                return buildResult;
            });

        var applyStage = new Func<BuildSsdtPipelineResult, CancellationToken, Task<Result<SchemaApplyResult>>>(async (build, ct) =>
        {
            var applyResult = await _schemaApplyOrchestrator
                .ExecuteAsync(build, request.ApplyOptions, log, ct)
                .ConfigureAwait(false);

            if (applyResult.IsFailure)
            {
                LogFailure(log, "fullExport.apply.failed", "Schema apply failed.", applyResult.Errors);
            }

            return applyResult;
        });

        Func<ModelExtractionResult, BuildSsdtPipelineResult, ModelUserSchemaGraph, CancellationToken, Task<Result<UatUsersApplicationResult>>>? uatStage = null;
        if (request.UatUsers is { Enabled: true })
        {
            if (string.IsNullOrWhiteSpace(request.Build.OutputDirectory))
            {
                var error = ValidationError.Create(
                    "pipeline.fullExport.uatUsers.outputDirectory.missing",
                    "Build output directory is required to emit uat-users artifacts.");
                LogFailure(log, "fullExport.uatUsers.failed", "uat-users pipeline failed.", ImmutableArray.Create(error));
                return Result<FullExportPipelineResult>.Failure(error);
            }

            uatStage = async (extraction, build, schemaGraph, ct) =>
            {
                var overrides = new UatUsersOverrides(
                    request.UatUsers.Enabled,
                    request.UatUsers.ConnectionString,
                    request.UatUsers.UserSchema,
                    request.UatUsers.UserTable,
                    request.UatUsers.UserIdColumn,
                    request.UatUsers.IncludeColumns ?? Array.Empty<string>(),
                    request.UatUsers.UserMapPath,
                    request.UatUsers.UatUserInventoryPath,
                    request.UatUsers.QaUserInventoryPath,
                    request.UatUsers.SnapshotPath,
                    request.UatUsers.UserEntityIdentifier);

                var runnerRequest = new UatUsersPipelineRequest(overrides, extraction, request.Build.OutputDirectory!, schemaGraph);
                var uatUsersResult = await _uatUsersRunner
                    .RunAsync(runnerRequest, ct)
                    .ConfigureAwait(false);

                if (uatUsersResult.IsFailure)
                {
                    LogFailure(log, "fullExport.uatUsers.failed", "uat-users pipeline failed.", uatUsersResult.Errors);
                    return uatUsersResult;
                }

                var uatUsersOutcome = uatUsersResult.Value;
                var metadataBuilder = new PipelineLogMetadataBuilder()
                    .WithCount("counts.catalog", uatUsersOutcome.Context?.UserFkCatalog.Count ?? 0)
                    .WithCount("counts.allowed", uatUsersOutcome.Context?.AllowedUserIds.Count ?? 0)
                    .WithCount("counts.orphans", uatUsersOutcome.Context?.OrphanUserIds.Count ?? 0);

                if (uatUsersOutcome.Context is { } uatContext)
                {
                    metadataBuilder.WithPath(
                        "paths.artifacts.root",
                        Path.Combine(uatContext.Artifacts.Root, "uat-users"));
                    metadataBuilder.WithPath("paths.userMap", uatContext.UserMapPath);
                }

                log.Record(
                    "fullExport.uatUsers.completed",
                    "uat-users pipeline completed successfully.",
                    metadataBuilder.Build());

                return uatUsersResult;
            };
        }
        else
        {
            log.Record("fullExport.uatUsers.skipped", "uat-users pipeline disabled.");
        }

        var coordinatorRequest = new FullExportCoordinatorRequest<ModelExtractionResult, CaptureProfilePipelineResult, BuildSsdtPipelineResult>(
            extractStage,
            profileStage,
            buildStage,
            applyStage,
            request.ApplyOptions,
            extraction => extraction,
            uatStage);

        var coordinatorResult = await _coordinator
            .ExecuteAsync(coordinatorRequest, cancellationToken)
            .ConfigureAwait(false);

        if (coordinatorResult.IsFailure)
        {
            return Result<FullExportPipelineResult>.Failure(coordinatorResult.Errors);
        }

        var outcome = coordinatorResult.Value;

        log.Record("fullExport.completed", "Full export pipeline completed.");

        return new FullExportPipelineResult(outcome.Extraction, outcome.Profile, outcome.Build, outcome.Apply, log.Build(), outcome.UatUsers);
    }

    private void LogFailure(
        PipelineExecutionLogBuilder log,
        string step,
        string message,
        ImmutableArray<ValidationError> errors)
    {
        var errorCodes = errors.IsDefaultOrEmpty
            ? string.Empty
            : string.Join(";", errors.Select(error => error.Code));

        log.Record(
            step,
            message,
            new PipelineLogMetadataBuilder()
                .WithValue("errors.codes", errorCodes)
                .Build());

        _logger.LogError("{Message} (Errors: {Codes})", message, errorCodes);
    }
}
