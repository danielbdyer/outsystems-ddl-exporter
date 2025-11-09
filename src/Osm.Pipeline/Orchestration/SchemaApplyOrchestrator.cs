using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed class SchemaApplyOrchestrator
{
    private readonly ISchemaDataApplier _schemaDataApplier;

    public SchemaApplyOrchestrator(ISchemaDataApplier schemaDataApplier)
    {
        _schemaDataApplier = schemaDataApplier ?? throw new ArgumentNullException(nameof(schemaDataApplier));
    }

    public Task<Result<SchemaApplyResult>> ExecuteAsync(
        BuildSsdtPipelineResult build,
        SchemaApplyOptions applyOptions,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(build, applyOptions, log: null, cancellationToken);

    public async Task<Result<SchemaApplyResult>> ExecuteAsync(
        BuildSsdtPipelineResult build,
        SchemaApplyOptions applyOptions,
        PipelineExecutionLogBuilder? log,
        CancellationToken cancellationToken = default)
    {
        if (build is null)
        {
            throw new ArgumentNullException(nameof(build));
        }

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
            log?.Record(
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

            log?.Record(
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
            log?.Record(
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

        log?.Record("fullExport.apply.completed", "Schema apply completed successfully.", metadata.Build());

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
}
