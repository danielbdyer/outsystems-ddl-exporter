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
    TimeSpan Duration);

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
    private readonly ISchemaDataApplier _schemaDataApplier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FullExportPipeline> _logger;

    public FullExportPipeline(
        ICommandDispatcher dispatcher,
        ISchemaDataApplier schemaDataApplier,
        TimeProvider timeProvider,
        ILogger<FullExportPipeline> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _schemaDataApplier = schemaDataApplier ?? throw new ArgumentNullException(nameof(schemaDataApplier));
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

        var applyResult = await ExecuteApplyAsync(build, request.ApplyOptions, log, cancellationToken).ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            LogFailure(log, "fullExport.apply.failed", "Schema apply failed.", applyResult.Errors);
            return Result<FullExportPipelineResult>.Failure(applyResult.Errors);
        }

        log.Record("fullExport.completed", "Full export pipeline completed.");

        var apply = applyResult.Value;
        return new FullExportPipelineResult(extraction, profile, build, apply, log.Build());
    }

    private async Task<Result<SchemaApplyResult>> ExecuteApplyAsync(
        BuildSsdtPipelineResult build,
        SchemaApplyOptions applyOptions,
        PipelineExecutionLogBuilder log,
        CancellationToken cancellationToken)
    {
        applyOptions ??= SchemaApplyOptions.Disabled;

        var safeScriptPath = string.IsNullOrWhiteSpace(build.SafeScriptPath) ? null : build.SafeScriptPath;
        var remediationPath = string.IsNullOrWhiteSpace(build.RemediationScriptPath) ? null : build.RemediationScriptPath;
        var staticSeedPaths = build.StaticSeedScriptPaths.IsDefault
            ? ImmutableArray<string>.Empty
            : build.StaticSeedScriptPaths;

        var warningsBuilder = ImmutableArray.CreateBuilder<string>();
        var skippedBuilder = ImmutableArray.CreateBuilder<string>();

        if (build.Opportunities.ContradictionCount > 0)
        {
            var message = $"{build.Opportunities.ContradictionCount} contradiction(s) require remediation before deployment.";
            warningsBuilder.Add(message);
            log.Record(
                "fullExport.apply.remediationPending",
                message,
                new PipelineLogMetadataBuilder()
                    .WithCount("contradictions.pending", build.Opportunities.ContradictionCount)
                    .WithPath("paths.remediationScript", remediationPath)
                    .Build());
        }

        if (!applyOptions.Enabled || string.IsNullOrWhiteSpace(applyOptions.ConnectionString))
        {
            if (!string.IsNullOrWhiteSpace(safeScriptPath))
            {
                skippedBuilder.Add(safeScriptPath);
            }

            if (!staticSeedPaths.IsDefaultOrEmpty)
            {
                skippedBuilder.AddRange(staticSeedPaths);
            }

            log.Record(
                "fullExport.apply.skipped",
                "Schema apply skipped (no connection configured).",
                new PipelineLogMetadataBuilder()
                    .WithFlag("apply.enabled", false)
                    .WithPath("paths.safeScript", safeScriptPath)
                    .WithPath("paths.remediationScript", remediationPath)
                    .Build());

            return new SchemaApplyResult(
                Attempted: false,
                SafeScriptApplied: false,
                StaticSeedsApplied: false,
                AppliedScripts: ImmutableArray<string>.Empty,
                AppliedSeedScripts: ImmutableArray<string>.Empty,
                SkippedScripts: skippedBuilder.ToImmutable(),
                Warnings: warningsBuilder.ToImmutable(),
                PendingRemediationCount: build.Opportunities.ContradictionCount,
                SafeScriptPath: safeScriptPath,
                RemediationScriptPath: remediationPath,
                StaticSeedScriptPaths: staticSeedPaths,
                Duration: TimeSpan.Zero);
        }

        var applyScripts = ImmutableArray<string>.Empty;
        if (applyOptions.ApplySafeScript && !string.IsNullOrWhiteSpace(safeScriptPath))
        {
            applyScripts = ImmutableArray.Create(safeScriptPath);
        }
        else if (!string.IsNullOrWhiteSpace(safeScriptPath))
        {
            skippedBuilder.Add(safeScriptPath);
        }
        else if (applyOptions.ApplySafeScript)
        {
            warningsBuilder.Add("Safe script apply was requested but no safe script path was generated.");
        }

        var seedScripts = ImmutableArray<string>.Empty;
        if (applyOptions.ApplyStaticSeeds && !staticSeedPaths.IsDefaultOrEmpty)
        {
            if (applyOptions.StaticSeedSynchronizationMode != StaticSeedSynchronizationMode.NonDestructive)
            {
                warningsBuilder.Add(
                    $"Static seed synchronization mode '{applyOptions.StaticSeedSynchronizationMode}' is not supported for automated apply. Scripts were not executed.");
                skippedBuilder.AddRange(staticSeedPaths);
            }
            else
            {
                seedScripts = staticSeedPaths;
            }
        }
        else if (!staticSeedPaths.IsDefaultOrEmpty)
        {
            skippedBuilder.AddRange(staticSeedPaths);
        }

        if (applyScripts.IsDefaultOrEmpty && seedScripts.IsDefaultOrEmpty)
        {
            log.Record(
                "fullExport.apply.noop",
                "Schema apply skipped (no scripts selected).",
                new PipelineLogMetadataBuilder()
                    .WithFlag("apply.enabled", true)
                    .WithPath("paths.safeScript", safeScriptPath)
                    .WithPath("paths.remediationScript", remediationPath)
                    .Build());

            return new SchemaApplyResult(
                Attempted: false,
                SafeScriptApplied: false,
                StaticSeedsApplied: false,
                AppliedScripts: ImmutableArray<string>.Empty,
                AppliedSeedScripts: ImmutableArray<string>.Empty,
                SkippedScripts: skippedBuilder.ToImmutable(),
                Warnings: warningsBuilder.ToImmutable(),
                PendingRemediationCount: build.Opportunities.ContradictionCount,
                SafeScriptPath: safeScriptPath,
                RemediationScriptPath: remediationPath,
                StaticSeedScriptPaths: staticSeedPaths,
                Duration: TimeSpan.Zero);
        }

        var connectionOptions = new SqlConnectionOptions(
            applyOptions.Authentication.Method,
            applyOptions.Authentication.TrustServerCertificate,
            applyOptions.Authentication.ApplicationName,
            applyOptions.Authentication.AccessToken);

        var applyRequest = new SchemaDataApplyRequest(
            applyOptions.ConnectionString!,
            connectionOptions,
            applyOptions.CommandTimeoutSeconds,
            applyScripts,
            seedScripts);

        var outcome = await _schemaDataApplier
            .ApplyAsync(applyRequest, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.IsFailure)
        {
            return Result<SchemaApplyResult>.Failure(outcome.Errors);
        }

        var value = outcome.Value;
        var metadata = new PipelineLogMetadataBuilder()
            .WithFlag("apply.enabled", true)
            .WithCount("scripts.applied", value.AppliedScripts.Length)
            .WithCount("seeds.applied", value.AppliedSeedScripts.Length)
            .WithCount("batches.executed", value.ExecutedBatchCount)
            .WithMetric("duration.ms", value.Duration.TotalMilliseconds)
            .WithPath("paths.safeScript", safeScriptPath)
            .WithPath("paths.remediationScript", remediationPath)
            .WithCount("contradictions.pending", build.Opportunities.ContradictionCount)
            .WithCount(
                "staticSeeds.total",
                staticSeedPaths.IsDefaultOrEmpty ? 0 : staticSeedPaths.Length);

        log.Record("fullExport.apply.completed", "Schema apply completed successfully.", metadata.Build());

        return new SchemaApplyResult(
            Attempted: true,
            SafeScriptApplied: value.AppliedScripts.Length > 0,
            StaticSeedsApplied: value.AppliedSeedScripts.Length > 0,
            AppliedScripts: value.AppliedScripts,
            AppliedSeedScripts: value.AppliedSeedScripts,
            SkippedScripts: skippedBuilder.ToImmutable(),
            Warnings: warningsBuilder.ToImmutable(),
            PendingRemediationCount: build.Opportunities.ContradictionCount,
            SafeScriptPath: safeScriptPath,
            RemediationScriptPath: remediationPath,
            StaticSeedScriptPaths: staticSeedPaths,
            Duration: value.Duration);
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
