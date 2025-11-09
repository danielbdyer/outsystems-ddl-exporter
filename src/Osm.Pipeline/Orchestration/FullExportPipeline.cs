using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

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

public sealed record FullExportPipelineRequest(
    ExtractModelPipelineRequest ExtractModel,
    CaptureProfilePipelineRequest CaptureProfile,
    BuildSsdtPipelineRequest Build,
    SchemaApplyOptions ApplyOptions) : ICommand<FullExportPipelineResult>;

public sealed record FullExportPipelineResult(
    ModelExtractionResult Extraction,
    CaptureProfilePipelineResult Profile,
    BuildSsdtPipelineResult Build,
    SchemaApplyResult Apply,
    PipelineExecutionLog ExecutionLog);

public sealed class FullExportPipeline : ICommandHandler<FullExportPipelineRequest, FullExportPipelineResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly SchemaApplyOrchestrator _schemaApplyOrchestrator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FullExportPipeline> _logger;

    public FullExportPipeline(
        ICommandDispatcher dispatcher,
        SchemaApplyOrchestrator schemaApplyOrchestrator,
        TimeProvider timeProvider,
        ILogger<FullExportPipeline> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _schemaApplyOrchestrator = schemaApplyOrchestrator ?? throw new ArgumentNullException(nameof(schemaApplyOrchestrator));
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

        var extractionResult = await _dispatcher
            .DispatchAsync<ExtractModelPipelineRequest, ModelExtractionResult>(request.ExtractModel, cancellationToken)
            .ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            LogFailure(log, "fullExport.extract.failed", "Model extraction failed.", extractionResult.Errors);
            return Result<FullExportPipelineResult>.Failure(extractionResult.Errors);
        }

        var extraction = extractionResult.Value;
        log.Record(
            "fullExport.extract.completed",
            "Model extraction completed successfully.",
            new PipelineLogMetadataBuilder()
                .WithTimestamp("extractedAtUtc", extraction.ExtractedAtUtc)
                .WithCount("warnings", extraction.Warnings.Count)
                .Build());

        var profileResult = await _dispatcher
            .DispatchAsync<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>(request.CaptureProfile, cancellationToken)
            .ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            LogFailure(log, "fullExport.profile.failed", "Profile capture failed.", profileResult.Errors);
            return Result<FullExportPipelineResult>.Failure(profileResult.Errors);
        }

        var profile = profileResult.Value;
        log.Record(
            "fullExport.profile.completed",
            "Profile capture completed successfully.",
            new PipelineLogMetadataBuilder()
                .WithCount("warnings", profile.Warnings.Length)
                .WithPath("profile.path", profile.ProfilePath)
                .Build());

        var buildResult = await _dispatcher
            .DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(request.Build, cancellationToken)
            .ConfigureAwait(false);
        if (buildResult.IsFailure)
        {
            LogFailure(log, "fullExport.build.failed", "SSDT emission failed.", buildResult.Errors);
            return Result<FullExportPipelineResult>.Failure(buildResult.Errors);
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

        var applyResult = await _schemaApplyOrchestrator
            .ExecuteAsync(build, request.ApplyOptions, log, cancellationToken)
            .ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            LogFailure(log, "fullExport.apply.failed", "Schema apply failed.", applyResult.Errors);
            return Result<FullExportPipelineResult>.Failure(applyResult.Errors);
        }

        log.Record("fullExport.completed", "Full export pipeline completed.");

        var apply = applyResult.Value;
        return new FullExportPipelineResult(extraction, profile, build, apply, log.Build());
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
