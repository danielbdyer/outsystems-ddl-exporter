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

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtEmissionStep : IBuildSsdtStep
{
    private readonly SmoModelFactory _smoModelFactory;
    private readonly SsdtEmitter _ssdtEmitter;
    private readonly PolicyDecisionLogWriter _decisionLogWriter;
    private readonly EmissionFingerprintCalculator _fingerprintCalculator;

    public BuildSsdtEmissionStep(
        SmoModelFactory smoModelFactory,
        SsdtEmitter ssdtEmitter,
        PolicyDecisionLogWriter decisionLogWriter,
        EmissionFingerprintCalculator fingerprintCalculator)
    {
        _smoModelFactory = smoModelFactory ?? throw new ArgumentNullException(nameof(smoModelFactory));
        _ssdtEmitter = ssdtEmitter ?? throw new ArgumentNullException(nameof(ssdtEmitter));
        _decisionLogWriter = decisionLogWriter ?? throw new ArgumentNullException(nameof(decisionLogWriter));
        _fingerprintCalculator = fingerprintCalculator ?? throw new ArgumentNullException(nameof(fingerprintCalculator));
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var model = context.FilteredModel ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before emission.");
        var profile = context.Profile ?? throw new InvalidOperationException("Profiling must complete before emission.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Policy decisions must be synthesized before emission.");
        var report = context.DecisionReport ?? throw new InvalidOperationException("Policy decision report must be synthesized before emission.");

        var supplementalEntities = context.SupplementalEntities;
        var smoModel = _smoModelFactory.Create(
            model,
            decisions,
            profile,
            context.Request.SmoOptions,
            supplementalEntities,
            context.Request.TypeMappingPolicy);

        var smoTableCount = smoModel.Tables.Length;
        var smoColumnCount = smoModel.Tables.Sum(static table => table.Columns.Length);
        var smoIndexCount = smoModel.Tables.Sum(static table => table.Indexes.Length);
        var smoForeignKeyCount = smoModel.Tables.Sum(static table => table.ForeignKeys.Length);
        context.Log.Record(
            "smo.model.created",
            "Materialized SMO table graph.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tables"] = smoTableCount.ToString(CultureInfo.InvariantCulture),
                ["columns"] = smoColumnCount.ToString(CultureInfo.InvariantCulture),
                ["indexes"] = smoIndexCount.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = smoForeignKeyCount.ToString(CultureInfo.InvariantCulture)
            });

        var emissionMetadata = _fingerprintCalculator.Compute(smoModel, decisions, context.Request.SmoOptions);
        var emissionOptions = BuildEmissionOptions(context, report, emissionMetadata);

        var coverageResult = EmissionCoverageCalculator.Compute(
            model,
            supplementalEntities,
            decisions,
            smoModel,
            emissionOptions);

        var manifest = await _ssdtEmitter
            .EmitAsync(
                smoModel,
                context.Request.OutputDirectory,
                emissionOptions,
                emissionMetadata,
                report,
                coverage: coverageResult.Summary,
                unsupported: coverageResult.Unsupported,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        context.Log.Record(
            "ssdt.emission.completed",
            "Emitted SSDT artifacts.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["outputDirectory"] = context.Request.OutputDirectory,
                ["tableArtifacts"] = manifest.Tables.Count.ToString(CultureInfo.InvariantCulture),
                ["includesPolicySummary"] = (manifest.PolicySummary is not null) ? "true" : "false"
            });

        var decisionLogPath = await _decisionLogWriter
            .WriteAsync(context.Request.OutputDirectory, report, cancellationToken)
            .ConfigureAwait(false);

        context.Log.Record(
            "policy.log.persisted",
            "Persisted policy decision log.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["path"] = decisionLogPath,
                ["diagnostics"] = report.Diagnostics.Length.ToString(CultureInfo.InvariantCulture)
            });

        context.SetEmissionArtifacts(manifest, decisionLogPath);
        return Result<BuildSsdtPipelineContext>.Success(context);
    }

    private SmoBuildOptions BuildEmissionOptions(
        BuildSsdtPipelineContext context,
        PolicyDecisionReport report,
        SsdtEmissionMetadata metadata)
    {
        var emissionOptions = context.Request.SmoOptions;
        if (!emissionOptions.Header.Enabled)
        {
            return emissionOptions;
        }

        var headerOptions = emissionOptions.Header with
        {
            Source = context.Request.ModelPath,
            Profile = context.Request.ProfilePath ?? context.Request.ProfilerProvider,
            Decisions = BuildDecisionSummary(context.Request.TighteningOptions, report),
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
