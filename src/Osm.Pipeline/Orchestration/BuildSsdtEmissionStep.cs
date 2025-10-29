using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;

namespace Osm.Pipeline.Orchestration;

public interface ISmoModelFactory
{
    SmoModel Create(
        OsmModel model,
        PolicyDecisionSet decisions,
        ProfileSnapshot? profile = null,
        SmoBuildOptions? options = null,
        IEnumerable<EntityModel>? supplementalEntities = null,
        TypeMappingPolicy? typeMappingPolicy = null);
}

public interface ISsdtEmitter
{
    Task<Result<SsdtManifest>> EmitAsync(
        SmoModel model,
        string outputDirectory,
        SmoBuildOptions options,
        SsdtEmissionMetadata emission,
        PolicyDecisionReport? decisionReport = null,
        IReadOnlyList<PreRemediationManifestEntry>? preRemediation = null,
        SsdtCoverageSummary? coverage = null,
        SsdtPredicateCoverage? predicateCoverage = null,
        IReadOnlyList<string>? unsupported = null,
        CancellationToken cancellationToken = default);
}

public interface IOpportunityLogWriter
{
    Task<Result<OpportunityArtifacts>> WriteAsync(
        string outputDirectory,
        OpportunitiesReport report,
        CancellationToken cancellationToken = default);
}

public interface IPolicyDecisionLogWriter
{
    Task<Result<string>> WriteAsync(
        string outputDirectory,
        PolicyDecisionReport report,
        CancellationToken cancellationToken = default,
        SsdtPredicateCoverage? predicateCoverage = null);
}

public sealed class BuildSsdtEmissionStep : IBuildSsdtStep<DecisionsSynthesized, EmissionReady>
{
    private readonly ISmoModelFactory _smoModelFactory;
    private readonly ISsdtEmitter _ssdtEmitter;
    private readonly IPolicyDecisionLogWriter _decisionLogWriter;
    private readonly EmissionFingerprintCalculator _fingerprintCalculator;
    private readonly IOpportunityLogWriter _opportunityWriter;

    public BuildSsdtEmissionStep(
        SmoModelFactory smoModelFactory,
        SsdtEmitter ssdtEmitter,
        PolicyDecisionLogWriter decisionLogWriter,
        EmissionFingerprintCalculator fingerprintCalculator,
        OpportunityLogWriter opportunityWriter)
        : this(
            new SmoModelFactoryAdapter(smoModelFactory),
            new SsdtEmitterAdapter(ssdtEmitter),
            new PolicyDecisionLogWriterAdapter(decisionLogWriter),
            fingerprintCalculator,
            new OpportunityLogWriterAdapter(opportunityWriter))
    {
    }

    public BuildSsdtEmissionStep(
        ISmoModelFactory smoModelFactory,
        ISsdtEmitter ssdtEmitter,
        IPolicyDecisionLogWriter decisionLogWriter,
        EmissionFingerprintCalculator fingerprintCalculator,
        IOpportunityLogWriter opportunityWriter)
    {
        _smoModelFactory = smoModelFactory ?? throw new ArgumentNullException(nameof(smoModelFactory));
        _ssdtEmitter = ssdtEmitter ?? throw new ArgumentNullException(nameof(ssdtEmitter));
        _decisionLogWriter = decisionLogWriter ?? throw new ArgumentNullException(nameof(decisionLogWriter));
        _fingerprintCalculator = fingerprintCalculator ?? throw new ArgumentNullException(nameof(fingerprintCalculator));
        _opportunityWriter = opportunityWriter ?? throw new ArgumentNullException(nameof(opportunityWriter));
    }

    public async Task<Result<EmissionReady>> ExecuteAsync(
        DecisionsSynthesized state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var model = state.Bootstrap.FilteredModel
            ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before emission.");
        var profile = state.Bootstrap.Profile
            ?? throw new InvalidOperationException("Profiling must complete before emission.");
        var supplementalEntities = state.Bootstrap.SupplementalEntities;
        var decisions = state.Decisions;
        var report = state.Report;

        var smoModelResult = CreateSmoModel(state, model, decisions, profile, supplementalEntities);
        RecordSmoModelCreated(state.Log, smoModelResult);

        var emissionResult = await EmitSsdtArtifactsAsync(
                state,
                report,
                smoModelResult.Model,
                model,
                supplementalEntities,
                decisions,
                cancellationToken)
            .ConfigureAwait(false);
        if (emissionResult.IsFailure)
        {
            RecordSsdtEmissionFailure(state.Log, state.Request.OutputDirectory, emissionResult.Errors);
            return Result<EmissionReady>.Failure(emissionResult.Errors);
        }

        RecordSsdtEmission(state.Log, emissionResult.Value);

        var decisionLogResult = await PersistDecisionLogAsync(
                state,
                report,
                emissionResult.Value.Coverage.PredicateCoverage,
                cancellationToken)
            .ConfigureAwait(false);
        if (decisionLogResult.IsFailure)
        {
            RecordDecisionLogFailure(state.Log, state.Request.OutputDirectory, decisionLogResult.Errors);
            return Result<EmissionReady>.Failure(decisionLogResult.Errors);
        }

        RecordDecisionLogPersisted(state.Log, decisionLogResult.Value);

        var opportunityResult = await PersistOpportunityArtifactsAsync(state, cancellationToken)
            .ConfigureAwait(false);
        if (opportunityResult.IsFailure)
        {
            RecordOpportunitiesFailure(state.Log, state.Request.OutputDirectory, opportunityResult.Errors);
            return Result<EmissionReady>.Failure(opportunityResult.Errors);
        }

        RecordOpportunitiesPersisted(state.Log, opportunityResult.Value);

        return Result<EmissionReady>.Success(new EmissionReady(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Insights,
            emissionResult.Value.Manifest,
            decisionLogResult.Value.Path,
            opportunityResult.Value.Artifacts));
    }

    private SmoModelCreationResult CreateSmoModel(
        DecisionsSynthesized state,
        OsmModel model,
        PolicyDecisionSet decisions,
        ProfileSnapshot profile,
        ImmutableArray<EntityModel> supplementalEntities)
    {
        var smoModel = _smoModelFactory.Create(
            model,
            decisions,
            profile,
            state.Request.SmoOptions,
            supplementalEntities,
            state.Request.TypeMappingPolicy);

        var metadata = new PipelineLogMetadataBuilder()
            .WithCount("tables", smoModel.Tables.Length)
            .WithCount("columns", smoModel.Tables.Sum(static table => table.Columns.Length))
            .WithCount("indexes", smoModel.Tables.Sum(static table => table.Indexes.Length))
            .WithCount("foreignKeys", smoModel.Tables.Sum(static table => table.ForeignKeys.Length))
            .Build();

        return new SmoModelCreationResult(smoModel, metadata);
    }

    private async Task<Result<SsdtEmissionResult>> EmitSsdtArtifactsAsync(
        DecisionsSynthesized state,
        PolicyDecisionReport report,
        SmoModel smoModel,
        OsmModel model,
        ImmutableArray<EntityModel> supplementalEntities,
        PolicyDecisionSet decisions,
        CancellationToken cancellationToken)
    {
        var emissionMetadata = _fingerprintCalculator.Compute(smoModel, decisions, state.Request.SmoOptions);
        var emissionOptions = BuildEmissionOptions(state, report, emissionMetadata);

        var coverageResult = EmissionCoverageCalculator.Compute(
            model,
            supplementalEntities,
            decisions,
            smoModel,
            emissionOptions);

        var manifestResult = await _ssdtEmitter
            .EmitAsync(
                smoModel,
                state.Request.OutputDirectory,
                emissionOptions,
                emissionMetadata,
                report,
                coverage: coverageResult.Summary,
                predicateCoverage: coverageResult.PredicateCoverage,
                unsupported: coverageResult.Unsupported,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<SsdtEmissionResult>.Failure(manifestResult.Errors);
        }

        var manifest = manifestResult.Value;

        var metadata = new PipelineLogMetadataBuilder()
            .WithPath("output", state.Request.OutputDirectory)
            .WithCount("tableArtifacts", manifest.Tables.Count)
            .WithFlag("policySummary.included", manifest.PolicySummary is not null)
            .Build();

        return Result<SsdtEmissionResult>.Success(new SsdtEmissionResult(manifest, coverageResult, metadata));
    }

    private async Task<Result<DecisionLogPersistenceResult>> PersistDecisionLogAsync(
        DecisionsSynthesized state,
        PolicyDecisionReport report,
        SsdtPredicateCoverage predicateCoverage,
        CancellationToken cancellationToken)
    {
        var decisionLogResult = await _decisionLogWriter
            .WriteAsync(
                state.Request.OutputDirectory,
                report,
                cancellationToken,
                predicateCoverage)
            .ConfigureAwait(false);
        if (decisionLogResult.IsFailure)
        {
            return Result<DecisionLogPersistenceResult>.Failure(decisionLogResult.Errors);
        }

        var decisionLogPath = decisionLogResult.Value;

        var metadata = new PipelineLogMetadataBuilder()
            .WithPath("decision", decisionLogPath)
            .WithCount("diagnostics", report.Diagnostics.Length)
            .Build();

        return Result<DecisionLogPersistenceResult>.Success(new DecisionLogPersistenceResult(decisionLogPath, metadata));
    }

    private async Task<Result<OpportunityPersistenceResult>> PersistOpportunityArtifactsAsync(
        DecisionsSynthesized state,
        CancellationToken cancellationToken)
    {
        var artifactsResult = await _opportunityWriter
            .WriteAsync(state.Request.OutputDirectory, state.Opportunities, cancellationToken)
            .ConfigureAwait(false);
        if (artifactsResult.IsFailure)
        {
            return Result<OpportunityPersistenceResult>.Failure(artifactsResult.Errors);
        }

        var opportunityArtifacts = artifactsResult.Value;

        var metadata = new PipelineLogMetadataBuilder()
            .WithPath("report", opportunityArtifacts.ReportPath)
            .WithPath("safeScript", opportunityArtifacts.SafeScriptPath)
            .WithPath("remediationScript", opportunityArtifacts.RemediationScriptPath)
            .WithCount("total", state.Opportunities.TotalCount)
            .Build();

        return Result<OpportunityPersistenceResult>.Success(new OpportunityPersistenceResult(opportunityArtifacts, metadata));
    }

    private static void RecordSmoModelCreated(
        PipelineExecutionLogBuilder log,
        SmoModelCreationResult result)
    {
        log.Record("smo.model.created", "Materialized SMO table graph.", result.Metadata);
    }

    private static void RecordSsdtEmission(
        PipelineExecutionLogBuilder log,
        SsdtEmissionResult result)
    {
        log.Record("ssdt.emission.completed", "Emitted SSDT artifacts.", result.Metadata);
    }

    private static void RecordSsdtEmissionFailure(
        PipelineExecutionLogBuilder log,
        string outputDirectory,
        ImmutableArray<ValidationError> errors)
    {
        var metadataBuilder = new PipelineLogMetadataBuilder()
            .WithPath("output", outputDirectory);

        if (!errors.IsDefaultOrEmpty && errors.Length > 0)
        {
            metadataBuilder.WithValue("error.code", errors[0].Code);
        }

        log.Record("ssdt.emission.failed", "Failed to emit SSDT artifacts.", metadataBuilder.Build());
    }

    private static void RecordDecisionLogPersisted(
        PipelineExecutionLogBuilder log,
        DecisionLogPersistenceResult result)
    {
        log.Record("policy.log.persisted", "Persisted policy decision log.", result.Metadata);
    }

    private static void RecordDecisionLogFailure(
        PipelineExecutionLogBuilder log,
        string outputDirectory,
        ImmutableArray<ValidationError> errors)
    {
        var metadataBuilder = new PipelineLogMetadataBuilder()
            .WithPath("output", outputDirectory);

        if (!errors.IsDefaultOrEmpty && errors.Length > 0)
        {
            metadataBuilder.WithValue("error.code", errors[0].Code);
        }

        log.Record("policy.log.failed", "Failed to persist policy decision log.", metadataBuilder.Build());
    }

    private static void RecordOpportunitiesPersisted(
        PipelineExecutionLogBuilder log,
        OpportunityPersistenceResult result)
    {
        log.Record("opportunities.persisted", "Persisted tightening opportunities.", result.Metadata);
    }

    private static void RecordOpportunitiesFailure(
        PipelineExecutionLogBuilder log,
        string outputDirectory,
        ImmutableArray<ValidationError> errors)
    {
        var metadataBuilder = new PipelineLogMetadataBuilder()
            .WithPath("output", outputDirectory);

        if (!errors.IsDefaultOrEmpty && errors.Length > 0)
        {
            metadataBuilder.WithValue("error.code", errors[0].Code);
        }

        log.Record("opportunities.persist.failed", "Failed to persist tightening opportunities.", metadataBuilder.Build());
    }

    private SmoBuildOptions BuildEmissionOptions(
        DecisionsSynthesized state,
        PolicyDecisionReport report,
        SsdtEmissionMetadata metadata)
    {
        var emissionOptions = state.Request.SmoOptions;
        if (!emissionOptions.Header.Enabled)
        {
            return emissionOptions;
        }

        var headerOptions = emissionOptions.Header with
        {
            Source = state.Request.ModelPath,
            Profile = state.Request.ProfilePath ?? state.Request.ProfilerProvider,
            Decisions = BuildDecisionSummary(state.Request.TighteningOptions, report),
            FingerprintAlgorithm = metadata.Algorithm,
            FingerprintHash = metadata.Hash,
            AdditionalItems = emissionOptions.Header.AdditionalItems,
        };

        return emissionOptions.WithHeaderOptions(headerOptions);
    }

    private static string BuildDecisionSummary(TighteningOptions options, PolicyDecisionReport report)
    {
        var parts = new List<string>(7)
        {
            $"Mode={options.Policy.Mode}",
            $"NullBudget={options.Policy.NullBudget.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"Columns={report.ColumnCount}",
            $"Tightened={report.TightenedColumnCount}",
            $"Unique={report.UniqueIndexCount}",
            $"FK={report.ForeignKeyCount}",
            $"FKEnabled={options.ForeignKeys.EnableCreation}",
        };

        return string.Join("; ", parts);
    }

    private sealed record SmoModelCreationResult(
        SmoModel Model,
        IReadOnlyDictionary<string, string?> Metadata);

    private sealed record SsdtEmissionResult(
        SsdtManifest Manifest,
        EmissionCoverageResult Coverage,
        IReadOnlyDictionary<string, string?> Metadata);

    private sealed record DecisionLogPersistenceResult(
        string Path,
        IReadOnlyDictionary<string, string?> Metadata);

    private sealed record OpportunityPersistenceResult(
        OpportunityArtifacts Artifacts,
        IReadOnlyDictionary<string, string?> Metadata);

    private sealed class SmoModelFactoryAdapter : ISmoModelFactory
    {
        private readonly SmoModelFactory _inner;

        public SmoModelFactoryAdapter(SmoModelFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public SmoModel Create(
            OsmModel model,
            PolicyDecisionSet decisions,
            ProfileSnapshot? profile = null,
            SmoBuildOptions? options = null,
            IEnumerable<EntityModel>? supplementalEntities = null,
            TypeMappingPolicy? typeMappingPolicy = null)
        {
            return _inner.Create(model, decisions, profile, options, supplementalEntities, typeMappingPolicy);
        }
    }

    private sealed class SsdtEmitterAdapter : ISsdtEmitter
    {
        private readonly SsdtEmitter _inner;

        public SsdtEmitterAdapter(SsdtEmitter inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<Result<SsdtManifest>> EmitAsync(
            SmoModel model,
            string outputDirectory,
            SmoBuildOptions options,
            SsdtEmissionMetadata emission,
            PolicyDecisionReport? decisionReport = null,
            IReadOnlyList<PreRemediationManifestEntry>? preRemediation = null,
            SsdtCoverageSummary? coverage = null,
            SsdtPredicateCoverage? predicateCoverage = null,
            IReadOnlyList<string>? unsupported = null,
            CancellationToken cancellationToken = default)
        {
            return _inner.EmitAsync(
                model,
                outputDirectory,
                options,
                emission,
                decisionReport,
                preRemediation,
                coverage,
                predicateCoverage,
                unsupported,
                cancellationToken);
        }
    }

    private sealed class PolicyDecisionLogWriterAdapter : IPolicyDecisionLogWriter
    {
        private readonly PolicyDecisionLogWriter _inner;

        public PolicyDecisionLogWriterAdapter(PolicyDecisionLogWriter inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<Result<string>> WriteAsync(
            string outputDirectory,
            PolicyDecisionReport report,
            CancellationToken cancellationToken = default,
            SsdtPredicateCoverage? predicateCoverage = null)
        {
            return _inner.WriteAsync(outputDirectory, report, cancellationToken, predicateCoverage);
        }
    }

    private sealed class OpportunityLogWriterAdapter : IOpportunityLogWriter
    {
        private readonly OpportunityLogWriter _inner;

        public OpportunityLogWriterAdapter(OpportunityLogWriter inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<Result<OpportunityArtifacts>> WriteAsync(
            string outputDirectory,
            OpportunitiesReport report,
            CancellationToken cancellationToken = default)
        {
            return _inner.WriteAsync(outputDirectory, report, cancellationToken);
        }
    }
}
