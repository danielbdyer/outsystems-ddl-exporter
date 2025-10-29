using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

internal sealed class ColumnDecisionAggregator
{
    public ColumnDecisionAggregation Aggregate(
        OsmModel model,
        EntityAttributeIndex attributeIndex,
        IReadOnlyDictionary<ColumnCoordinate, ColumnProfile> columnProfiles,
        IReadOnlyDictionary<ColumnCoordinate, UniqueCandidateProfile> uniqueProfiles,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        ForeignKeyTargetIndex foreignKeyTargets,
        UniqueIndexEvidenceAggregator uniqueEvidence,
        IReadOnlyList<ITighteningAnalyzer> analyzers)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (attributeIndex is null)
        {
            throw new ArgumentNullException(nameof(attributeIndex));
        }

        if (columnProfiles is null)
        {
            throw new ArgumentNullException(nameof(columnProfiles));
        }

        if (uniqueProfiles is null)
        {
            throw new ArgumentNullException(nameof(uniqueProfiles));
        }

        if (foreignKeyReality is null)
        {
            throw new ArgumentNullException(nameof(foreignKeyReality));
        }

        if (foreignKeyTargets is null)
        {
            throw new ArgumentNullException(nameof(foreignKeyTargets));
        }

        if (uniqueEvidence is null)
        {
            throw new ArgumentNullException(nameof(uniqueEvidence));
        }

        if (analyzers is null)
        {
            throw new ArgumentNullException(nameof(analyzers));
        }

        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();
        var columnModuleBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        var analysisBuilders = new Dictionary<ColumnCoordinate, ColumnAnalysisBuilder>();

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            foreach (var attribute in attributeIndex.GetAttributes(entity))
            {
                var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
                var builder = new ColumnAnalysisBuilder(coordinate);
                analysisBuilders[coordinate] = builder;

                var columnProfile = columnProfiles.TryGetValue(coordinate, out var profile) ? profile : null;
                var uniqueProfile = uniqueProfiles.TryGetValue(coordinate, out var uniqueCandidate) ? uniqueCandidate : null;
                var foreignKey = foreignKeyReality.TryGetValue(coordinate, out var foreignKeyCandidate) ? foreignKeyCandidate : null;

                var context = new EntityContext(
                    entity,
                    attribute,
                    coordinate,
                    columnProfile,
                    uniqueProfile,
                    foreignKey,
                    foreignKeyTargets.GetTarget(coordinate),
                    uniqueEvidence.SingleColumnClean.Contains(coordinate),
                    uniqueEvidence.SingleColumnDuplicates.Contains(coordinate),
                    uniqueEvidence.CompositeClean.Contains(coordinate),
                    uniqueEvidence.CompositeDuplicates.Contains(coordinate));

                foreach (var analyzer in analyzers)
                {
                    analyzer.Analyze(context, builder);
                }

                nullabilityBuilder[coordinate] = builder.Nullability;
                columnModuleBuilder[coordinate] = entity.Module.Value;

                if (builder.ForeignKey is not null)
                {
                    foreignKeyBuilder[coordinate] = builder.ForeignKey;
                }
            }
        }

        return new ColumnDecisionAggregation(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            columnModuleBuilder.ToImmutable(),
            analysisBuilders);
    }
}

internal sealed record ColumnDecisionAggregation(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> Nullability,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeys,
    ImmutableDictionary<ColumnCoordinate, string> ColumnModules,
    IReadOnlyDictionary<ColumnCoordinate, ColumnAnalysisBuilder> ColumnAnalyses);

