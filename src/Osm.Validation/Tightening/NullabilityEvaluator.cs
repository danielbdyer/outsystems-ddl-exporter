using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Signals;

namespace Osm.Validation.Tightening;

internal sealed class NullabilityEvaluator : ITighteningAnalyzer
{
    private readonly TighteningOptions _options;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> _columnProfiles;
    private readonly IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> _uniqueProfiles;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _foreignKeys;
    private readonly ForeignKeyTargetIndex _foreignKeyTargets;
    private readonly ISet<ColumnCoordinate> _singleUniqueClean;
    private readonly ISet<ColumnCoordinate> _singleUniqueDuplicates;
    private readonly ISet<ColumnCoordinate> _compositeUniqueClean;
    private readonly ISet<ColumnCoordinate> _compositeUniqueDuplicates;
    private readonly NullabilityPolicyDefinition _policyDefinition;

    public NullabilityEvaluator(
        TighteningOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeys,
        ForeignKeyTargetIndex foreignKeyTargets,
        ISet<ColumnCoordinate> singleUniqueClean,
        ISet<ColumnCoordinate> singleUniqueDuplicates,
        ISet<ColumnCoordinate> compositeUniqueClean,
        ISet<ColumnCoordinate> compositeUniqueDuplicates)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _columnProfiles = columnProfiles ?? throw new ArgumentNullException(nameof(columnProfiles));
        _uniqueProfiles = uniqueProfiles ?? throw new ArgumentNullException(nameof(uniqueProfiles));
        _foreignKeys = foreignKeys ?? throw new ArgumentNullException(nameof(foreignKeys));
        _foreignKeyTargets = foreignKeyTargets ?? throw new ArgumentNullException(nameof(foreignKeyTargets));
        _singleUniqueClean = singleUniqueClean ?? throw new ArgumentNullException(nameof(singleUniqueClean));
        _singleUniqueDuplicates = singleUniqueDuplicates ?? throw new ArgumentNullException(nameof(singleUniqueDuplicates));
        _compositeUniqueClean = compositeUniqueClean ?? throw new ArgumentNullException(nameof(compositeUniqueClean));
        _compositeUniqueDuplicates = compositeUniqueDuplicates ?? throw new ArgumentNullException(nameof(compositeUniqueDuplicates));
        _policyDefinition = NullabilitySignalFactory.Create(_options.Policy.Mode);
    }

    public NullabilityDecision Evaluate(EntityModel entity, AttributeModel attribute, ColumnCoordinate coordinate)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var columnProfile = _columnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
        var uniqueProfile = _uniqueProfiles.TryGetValue(coordinate, out var uniqueCandidate) ? uniqueCandidate : null;
        var fkReality = _foreignKeys.TryGetValue(coordinate, out var fk) ? fk : null;
        var foreignKeyTarget = _foreignKeyTargets.GetTarget(coordinate);

        var context = new NullabilitySignalContext(
            _options,
            entity,
            attribute,
            coordinate,
            columnProfile,
            uniqueProfile,
            fkReality,
            foreignKeyTarget,
            _singleUniqueClean.Contains(coordinate),
            _singleUniqueDuplicates.Contains(coordinate),
            _compositeUniqueClean.Contains(coordinate),
            _compositeUniqueDuplicates.Contains(coordinate));

        var signalTrace = _policyDefinition.Root.Evaluate(context);
        var dataTrace = _policyDefinition.Evidence.Evaluate(context);
        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        var supplementalTraces = new List<SignalEvaluation>();

        foreach (var rationale in signalTrace.CollectRationales())
        {
            rationales.Add(rationale);
        }

        if (columnProfile is null)
        {
            rationales.Add(TighteningRationales.ProfileMissing);
        }

        var supplementaryEvaluations = new[]
        {
            _policyDefinition.UniqueSignal.Evaluate(context),
            _policyDefinition.MandatorySignal.Evaluate(context),
            _policyDefinition.DefaultSignal.Evaluate(context),
            _policyDefinition.ForeignKeySignal.Evaluate(context)
        };

        foreach (var supplementary in supplementaryEvaluations)
        {
            foreach (var rationale in supplementary.CollectRationales())
            {
                rationales.Add(rationale);
            }

            if (!signalTrace.ContainsCode(supplementary.Code))
            {
                supplementalTraces.Add(supplementary);
            }
        }

        var conditionalTriggered = signalTrace.ContainsSatisfiedCode(_policyDefinition.ConditionalSignalCodes);
        var makeNotNull = signalTrace.Result;

        if (attribute.IsMandatory)
        {
            conditionalTriggered = true;
        }

        if (_options.Policy.Mode != TighteningMode.Cautious && conditionalTriggered)
        {
            foreach (var rationale in dataTrace.CollectRationales())
            {
                rationales.Add(rationale);
            }
        }

        if (_options.Policy.Mode == TighteningMode.Aggressive && attribute.IsMandatory && !makeNotNull)
        {
            makeNotNull = true;
        }

        var requiresRemediation = false;
        var relaxationBlocked = false;

        if (_options.Policy.Mode == TighteningMode.Aggressive && makeNotNull && conditionalTriggered && !dataTrace.Result)
        {
            requiresRemediation = true;
            rationales.Add(TighteningRationales.RemediateBeforeTighten);
        }

        if (_options.Policy.Mode == TighteningMode.Cautious &&
            !_options.Policy.AllowCautiousNullabilityRelaxation &&
            attribute.IsMandatory &&
            !makeNotNull)
        {
            relaxationBlocked = true;
            makeNotNull = true;
            requiresRemediation = true;
            rationales.Add(TighteningRationales.CautiousRelaxationDisabled);
            rationales.Add(TighteningRationales.RemediateBeforeTighten);
        }

        var trace = _policyDefinition.EvidenceEmbeddedInRoot ? signalTrace : signalTrace.AppendChild(dataTrace);

        foreach (var supplementary in supplementalTraces)
        {
            trace = trace.AppendChild(supplementary);
        }

        if (trace is not null)
        {
            trace = trace with { Result = makeNotNull };

            if (relaxationBlocked)
            {
                var overrideNode = SignalEvaluation.Create(
                    "CAUTIOUS_RELAXATION_OVERRIDE",
                    "Cautious relaxation disabled; enforcing NOT NULL per configuration.",
                    result: true,
                    rationales: new[]
                    {
                        TighteningRationales.CautiousRelaxationDisabled,
                        TighteningRationales.RemediateBeforeTighten
                    });

                trace = trace.AppendChild(overrideNode);
            }
        }

        return NullabilityDecision.Create(
            coordinate,
            makeNotNull,
            requiresRemediation,
            rationales.ToImmutableArray(),
            trace);
    }

    public void Analyze(EntityContext context, ColumnAnalysisBuilder builder)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        var decision = Evaluate(context.Entity, context.Attribute, context.Column);
        builder.SetNullability(decision);

        if (!ShouldCreateOpportunity(decision))
        {
            return;
        }

        var summary = BuildNullabilitySummary(decision);
        var risk = ChangeRiskClassifier.ForNotNull(decision);
        var opportunity = Opportunity.Create(
            OpportunityType.Nullability,
            "NOT NULL",
            summary,
            risk,
            decision.Rationales,
            column: context.Column,
            disposition: decision.RequiresRemediation ? OpportunityDisposition.NeedsRemediation : OpportunityDisposition.ReadyToApply);

        builder.AddOpportunity(opportunity);
    }

    private static bool ShouldCreateOpportunity(NullabilityDecision decision)
        => decision.RequiresRemediation || !decision.MakeNotNull;

    private static string BuildNullabilitySummary(NullabilityDecision decision)
    {
        if (decision.RequiresRemediation)
        {
            return "NOT NULL was not applied. Remediate data before enforcement can proceed.";
        }

        if (decision.Rationales.Contains(TighteningRationales.ProfileMissing))
        {
            return "NOT NULL was not applied. Collect profiling evidence before enforcement can proceed.";
        }

        if (decision.Rationales.Contains(TighteningRationales.NullBudgetEpsilon))
        {
            return "NOT NULL was not applied. Column exceeds the configured null budget threshold.";
        }

        if (decision.Rationales.Contains(TighteningRationales.DataHasNulls))
        {
            return "NOT NULL was not applied. Profiling detected NULL values that contradict the logical mandatory flag.";
        }

        return "NOT NULL was not applied. Review policy blockers before enforcement can proceed.";
    }
}
