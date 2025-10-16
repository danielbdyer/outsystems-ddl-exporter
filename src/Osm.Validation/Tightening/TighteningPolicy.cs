using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

public sealed class TighteningPolicy
{
    public static TighteningDecisions Evaluate(OsmModel model, ProfileSnapshot snapshot, TighteningMode mode)
    {
        var options = CreateKernelOptions(mode);
        var decisionSet = ComputeDecisionSet(model, snapshot, options);

        return TighteningDecisions.Create(decisionSet.Nullability, decisionSet.ForeignKeys, decisionSet.UniqueIndexes);
    }

    public PolicyDecisionSet Decide(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
        => ComputeDecisionSet(model, snapshot, options);

    private static PolicyDecisionSet ComputeDecisionSet(OsmModel model, ProfileSnapshot snapshot, TighteningOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var columnProfiles = snapshot.Columns.ToDictionary(ColumnCoordinate.From, static c => c);
        var uniqueProfiles = snapshot.UniqueCandidates.ToDictionary(ColumnCoordinate.From, static u => u);
        var fkReality = snapshot.ForeignKeys.ToDictionary(f => ColumnCoordinate.From(f.Reference), static f => f);

        var lookupResolution = EntityLookupResolver.Resolve(model, options.Emission.NamingOverrides);
        var entityLookup = lookupResolution.Lookup;
        var attributeIndex = EntityAttributeIndex.Create(model);
        var foreignKeyTargets = ForeignKeyTargetIndex.Create(attributeIndex, entityLookup);

        var uniqueEvidence = UniqueIndexEvidenceAggregator.Create(
            model,
            uniqueProfiles,
            snapshot.CompositeUniqueCandidates,
            options.Uniqueness.EnforceSingleColumnUnique,
            options.Uniqueness.EnforceMultiColumnUnique);

        var uniqueStrategy = new UniqueIndexDecisionStrategy(options, columnProfiles, uniqueProfiles, uniqueEvidence);

        var nullabilityEvaluator = new NullabilityEvaluator(
            options,
            columnProfiles,
            uniqueProfiles,
            fkReality,
            foreignKeyTargets,
            uniqueEvidence.SingleColumnClean,
            uniqueEvidence.SingleColumnDuplicates,
            uniqueEvidence.CompositeClean,
            uniqueEvidence.CompositeDuplicates);

        var foreignKeyEvaluator = new ForeignKeyEvaluator(options.ForeignKeys, fkReality, foreignKeyTargets);

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();
        var uniqueIndexBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, UniqueIndexDecision>();
        var columnModuleBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        var indexModuleBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, string>();

        foreach (var entity in model.Modules.SelectMany(m => m.Entities))
        {
            foreach (var attribute in attributeIndex.GetAttributes(entity))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);

                var nullability = nullabilityEvaluator.Evaluate(entity, attribute, coordinate);

                nullabilityBuilder[coordinate] = nullability;
                columnModuleBuilder[coordinate] = entity.Module.Value;

                if (attribute.Reference.IsReference)
                {
                    var fkDecision = foreignKeyEvaluator.Evaluate(entity, attribute, coordinate);

                    foreignKeyBuilder[coordinate] = fkDecision;
                }
            }

            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
                var uniqueDecision = uniqueStrategy.Decide(entity, index);

                uniqueIndexBuilder[indexCoordinate] = uniqueDecision;
                indexModuleBuilder[indexCoordinate] = entity.Module.Value;
            }
        }

        return PolicyDecisionSet.Create(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            uniqueIndexBuilder.ToImmutable(),
            lookupResolution.Diagnostics,
            columnModuleBuilder.ToImmutable(),
            indexModuleBuilder.ToImmutable(),
            options);
    }

    private static TighteningOptions CreateKernelOptions(TighteningMode mode)
    {
        var defaults = TighteningOptions.Default;
        var policy = PolicyOptions.Create(mode, defaults.Policy.NullBudget).Value;

        return TighteningOptions.Create(
            policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;
    }
}
