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
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record TighteningAnalysisPipelineRequest(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    TighteningOptions TighteningOptions,
    SupplementalModelOptions SupplementalModels,
    string ProfilePath,
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
    private readonly IPipelineBootstrapTelemetryFactory _telemetryFactory;
    private readonly TimeProvider _timeProvider;

    public TighteningAnalysisPipeline(
        IPipelineBootstrapper bootstrapper,
        TighteningPolicy tighteningPolicy,
        PolicyDecisionLogWriter decisionLogWriter,
        IProfileSnapshotDeserializer profileDeserializer,
        IPipelineBootstrapTelemetryFactory telemetryFactory,
        TimeProvider timeProvider)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _tighteningPolicy = tighteningPolicy ?? throw new ArgumentNullException(nameof(tighteningPolicy));
        _decisionLogWriter = decisionLogWriter ?? throw new ArgumentNullException(nameof(decisionLogWriter));
        _profileDeserializer = profileDeserializer ?? throw new ArgumentNullException(nameof(profileDeserializer));
        _telemetryFactory = telemetryFactory ?? throw new ArgumentNullException(nameof(telemetryFactory));
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

        if (string.IsNullOrWhiteSpace(request.ModelPath))
        {
            return ValidationError.Create(
                "pipeline.analyze.model.missing",
                "Model path must be provided for tightening analysis.");
        }

        if (string.IsNullOrWhiteSpace(request.ProfilePath))
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
        var scope = ModelExecutionScope.Create(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            request.TighteningOptions,
            smoOptions: null);
        var telemetry = _telemetryFactory.Create(
            scope,
            new PipelineCommandDescriptor(
                "Received tightening analysis request.",
                "Loading profiling snapshot from disk.",
                "Loaded profiling snapshot from disk.",
                IncludeSupplementalDetails: true,
                IncludeTighteningDetails: true),
            new PipelineBootstrapTelemetryExtras(
                ProfilePath: request.ProfilePath));
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (_, token) => LoadProfileAsync(request.ProfilePath, token));

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);

        if (bootstrapResult.IsFailure)
        {
            return Result<TighteningAnalysisPipelineResult>.Failure(bootstrapResult.Errors);
        }

        var bootstrap = bootstrapResult.Value;
        var decisions = _tighteningPolicy.Decide(bootstrap.FilteredModel, bootstrap.Profile, request.TighteningOptions);
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

    private async Task<Result<ProfileSnapshot>> LoadProfileAsync(string profilePath, CancellationToken cancellationToken)
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
            return _profileDeserializer.Deserialize(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.loadFailed",
                $"Failed to read profiling snapshot '{profilePath}': {ex.Message}");
        }
    }
}
