using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEmissionStep : IBuildSsdtStep<DecisionsSynthesized, EmissionReady>
{
    private readonly SmoModelFactory _smoModelFactory;
    private readonly SsdtEmitter _ssdtEmitter;
    private readonly PolicyDecisionLogWriter _decisionLogWriter;
    private readonly EmissionFingerprintCalculator _fingerprintCalculator;
    private readonly OpportunityLogWriter _opportunityWriter;

    public BuildSsdtEmissionStep(
        SmoModelFactory smoModelFactory,
        SsdtEmitter ssdtEmitter,
        PolicyDecisionLogWriter decisionLogWriter,
        EmissionFingerprintCalculator fingerprintCalculator,
        OpportunityLogWriter opportunityWriter)
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

        var smoModel = _smoModelFactory.Create(
            model,
            decisions,
            profile,
            state.Request.SmoOptions,
            supplementalEntities,
            state.Request.TypeMappingPolicy);

        var smoTableCount = smoModel.Tables.Length;
        var smoColumnCount = smoModel.Tables.Sum(static table => table.Columns.Length);
        var smoIndexCount = smoModel.Tables.Sum(static table => table.Indexes.Length);
        var smoForeignKeyCount = smoModel.Tables.Sum(static table => table.ForeignKeys.Length);
        state.Log.Record(
            "smo.model.created",
            "Materialized SMO table graph.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tables"] = smoTableCount.ToString(CultureInfo.InvariantCulture),
                ["columns"] = smoColumnCount.ToString(CultureInfo.InvariantCulture),
                ["indexes"] = smoIndexCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = smoForeignKeyCount.ToString(CultureInfo.InvariantCulture)
            });

        var emissionMetadata = _fingerprintCalculator.Compute(smoModel, decisions, state.Request.SmoOptions);
        var emissionOptions = BuildEmissionOptions(state, report, emissionMetadata);

        var coverageResult = EmissionCoverageCalculator.Compute(
            model,
            supplementalEntities,
            decisions,
            smoModel,
            emissionOptions);

        var manifest = await _ssdtEmitter
            .EmitAsync(
                smoModel,
                state.Request.OutputDirectory,
                emissionOptions,
                emissionMetadata,
                report,
                coverage: coverageResult.Summary,
                unsupported: coverageResult.Unsupported,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        state.Log.Record(
            "ssdt.emission.completed",
            "Emitted SSDT artifacts.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["outputDirectory"] = state.Request.OutputDirectory,
                ["tableArtifacts"] = manifest.Tables.Count.ToString(CultureInfo.InvariantCulture),
                ["includesPolicySummary"] = (manifest.PolicySummary is not null) ? "true" : "false"
            });

        var decisionLogPath = await _decisionLogWriter
            .WriteAsync(state.Request.OutputDirectory, report, cancellationToken)
            .ConfigureAwait(false);

        state.Log.Record(
            "policy.log.persisted",
            "Persisted policy decision log.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["path"] = decisionLogPath,
                ["diagnostics"] = report.Diagnostics.Length.ToString(CultureInfo.InvariantCulture)
            });

        var opportunityArtifacts = await _opportunityWriter
            .WriteAsync(state.Request.OutputDirectory, state.Opportunities, cancellationToken)
            .ConfigureAwait(false);

        state.Log.Record(
            "opportunities.persisted",
            "Persisted tightening opportunities.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["report"] = opportunityArtifacts.ReportPath,
                ["safeScript"] = opportunityArtifacts.SafeScriptPath,
                ["remediationScript"] = opportunityArtifacts.RemediationScriptPath,
                ["count"] = state.Opportunities.TotalCount.ToString(CultureInfo.InvariantCulture)
            });

        return Result<EmissionReady>.Success(new EmissionReady(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Insights,
            manifest,
            decisionLogPath,
            opportunityArtifacts));
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
}
