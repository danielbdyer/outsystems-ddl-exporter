using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Json;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record TighteningAnalysisPipelineRequest(
    ModelExecutionScope Scope,
    string OutputDirectory) : ICommand<TighteningAnalysisPipelineResult>;

public sealed record TighteningAnalysisPipelineResult(
    PolicyDecisionReport Report,
    ProfileSnapshot Profile,
    ImmutableArray<string> SummaryLines,
    string SummaryPath,
    string DecisionLogPath,
    PipelineExecutionLog ExecutionLog,
    ImmutableArray<string> Warnings);

public sealed class TighteningAnalysisPipeline : ICommandHandler<TighteningAnalysisPipelineRequest, TighteningAnalysisPipelineResult>
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly TighteningPolicy _tighteningPolicy;
    private readonly PolicyDecisionLogWriter _decisionLogWriter;
    private readonly IProfileSnapshotDeserializer _profileDeserializer;
    private readonly TimeProvider _timeProvider;

    public TighteningAnalysisPipeline(
        IPipelineBootstrapper bootstrapper,
        TighteningPolicy tighteningPolicy,
        PolicyDecisionLogWriter decisionLogWriter,
        IProfileSnapshotDeserializer profileDeserializer,
        TimeProvider timeProvider)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _tighteningPolicy = tighteningPolicy ?? throw new ArgumentNullException(nameof(tighteningPolicy));
        _decisionLogWriter = decisionLogWriter ?? throw new ArgumentNullException(nameof(decisionLogWriter));
        _profileDeserializer = profileDeserializer ?? throw new ArgumentNullException(nameof(profileDeserializer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Result<TighteningAnalysisPipelineResult>> HandleAsync(
        TighteningAnalysisPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Scope.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.analyze.model.missing",
                "Model path must be provided for tightening analysis.");
        }

        if (string.IsNullOrWhiteSpace(request.Scope.ProfilePath))
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.missing",
                "Profile path must be provided for tightening analysis.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return ValidationError.Create(
                "pipeline.analyze.output.missing",
                "Output directory must be provided for tightening analysis.");
        }

        var log = new PipelineExecutionLogBuilder(_timeProvider);
        var telemetry = CreateTelemetry(request);
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.Scope.ModelPath,
            request.Scope.ModuleFilter,
            request.Scope.SupplementalModels,
            telemetry,
            (_, token) => LoadProfileAsync(request.Scope.ProfilePath, token),
            request.Scope.SqlOptions,
            request.Scope.InlineModel,
            request.Scope.ModelWarnings);

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);

        if (bootstrapResult.IsFailure)
        {
            return Result<TighteningAnalysisPipelineResult>.Failure(bootstrapResult.Errors);
        }

        var bootstrap = bootstrapResult.Value;
        var decisions = _tighteningPolicy.Decide(bootstrap.FilteredModel, bootstrap.Profile, request.Scope.TighteningOptions);
        var report = PolicyDecisionReporter.Create(decisions);

        log.Record(
            "policy.decisions.synthesized",
            "Synthesized tightening decisions.",
            new PipelineLogMetadataBuilder()
                .WithCount("columns.total", report.ColumnCount)
                .WithCount("columns.tightened", report.TightenedColumnCount)
                .WithCount("columns.remediation", report.RemediationColumnCount)
                .WithCount("indexes.unique", report.UniqueIndexCount)
                .WithCount("indexes.uniqueEnforced", report.UniqueIndexesEnforcedCount)
                .WithCount("foreignKeys.total", report.ForeignKeyCount)
                .WithCount("foreignKeys.created", report.ForeignKeysCreatedCount)
                .Build());

        Directory.CreateDirectory(request.OutputDirectory);

        var summaryLines = PolicyDecisionSummaryFormatter.FormatForConsole(report);
        if (summaryLines.IsDefaultOrEmpty || summaryLines.Length == 0)
        {
            summaryLines = ImmutableArray.Create(
                "No column tightening actions were taken based on the current profile evidence.");
        }

        var summaryPath = Path.Combine(request.OutputDirectory, "tightening-summary.txt");
        await File.WriteAllLinesAsync(summaryPath, summaryLines, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        var decisionLogResult = await _decisionLogWriter
            .WriteAsync(request.OutputDirectory, report, cancellationToken)
            .ConfigureAwait(false);
        if (decisionLogResult.IsFailure)
        {
            return Result<TighteningAnalysisPipelineResult>.Failure(decisionLogResult.Errors);
        }

        var decisionLogPath = decisionLogResult.Value;

        log.Record(
            "analysis.outputs.written",
            "Wrote tightening analysis artifacts.",
            new PipelineLogMetadataBuilder()
                .WithPath("summary", summaryPath)
                .WithPath("decisionLog", decisionLogPath)
                .Build());

        return new TighteningAnalysisPipelineResult(
            report,
            bootstrap.Profile,
            summaryLines,
            summaryPath,
            decisionLogPath,
            log.Build(),
            bootstrap.Warnings);
    }

    private PipelineBootstrapTelemetry CreateTelemetry(TighteningAnalysisPipelineRequest request)
    {
        return new PipelineBootstrapTelemetry(
            "Received tightening analysis request.",
            new PipelineLogMetadataBuilder()
                .WithPath("model", request.Scope.ModelPath)
                .WithPath("profile", request.Scope.ProfilePath)
                .WithFlag("moduleFilter.hasFilter", request.Scope.ModuleFilter.HasFilter)
                .WithCount("moduleFilter.modules", request.Scope.ModuleFilter.Modules.Length)
                .WithValue("tightening.mode", request.Scope.TighteningOptions.Policy.Mode.ToString())
                .WithMetric("tightening.nullBudget", request.Scope.TighteningOptions.Policy.NullBudget)
                .Build(),
            "Loading profiling snapshot from disk.",
            new PipelineLogMetadataBuilder()
                .WithPath("profile", request.Scope.ProfilePath)
                .Build(),
            "Loaded profiling snapshot from disk.");
    }

    private async Task<Result<ProfileCaptureResult>> LoadProfileAsync(string profilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.missing",
                "Profile path must be provided for tightening analysis.");
        }

        try
        {
            await using var stream = new FileStream(
                profilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            cancellationToken.ThrowIfCancellationRequested();
            var snapshotResult = _profileDeserializer.Deserialize(stream);
            if (snapshotResult.IsFailure)
            {
                return Result<ProfileCaptureResult>.Failure(snapshotResult.Errors);
            }

            return Result<ProfileCaptureResult>.Success(
                new ProfileCaptureResult(snapshotResult.Value, MultiEnvironmentProfileReport.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.loadFailed",
                $"Failed to read profiling snapshot '{profilePath}': {ex.Message}");
        }
    }
}
