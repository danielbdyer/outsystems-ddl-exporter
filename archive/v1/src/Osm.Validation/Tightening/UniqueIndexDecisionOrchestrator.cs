using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Validation.Tightening;

internal sealed class UniqueIndexDecisionOrchestrator
{
    private readonly OpportunityBuilder _opportunityBuilder;

    public UniqueIndexDecisionOrchestrator(OpportunityBuilder opportunityBuilder)
    {
        _opportunityBuilder = opportunityBuilder ?? throw new ArgumentNullException(nameof(opportunityBuilder));
    }

    public UniqueIndexAggregation Evaluate(
        OsmModel model,
        UniqueIndexDecisionStrategy uniqueStrategy,
        IReadOnlyDictionary<ColumnCoordinate, ColumnAnalysisBuilder> columnAnalyses)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (uniqueStrategy is null)
        {
            throw new ArgumentNullException(nameof(uniqueStrategy));
        }

        if (columnAnalyses is null)
        {
            throw new ArgumentNullException(nameof(columnAnalyses));
        }

        var uniqueIndexBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, UniqueIndexDecision>();
        var indexModuleBuilder = ImmutableDictionary.CreateBuilder<IndexCoordinate, string>();

        foreach (var entity in model.Modules.SelectMany(static module => module.Entities))
        {
            foreach (var index in entity.Indexes.Where(static i => i.IsUnique))
            {
                var analysis = uniqueStrategy.Evaluate(entity, index);
                uniqueIndexBuilder[analysis.Index] = analysis.Decision;
                indexModuleBuilder[analysis.Index] = entity.Module.Value;

                foreach (var column in analysis.Columns)
                {
                    if (!columnAnalyses.TryGetValue(column, out var builder))
                    {
                        continue;
                    }

                    builder.AddUniqueDecision(analysis.Decision);

                    var opportunity = _opportunityBuilder.TryCreate(analysis, column);
                    if (opportunity is not null)
                    {
                        builder.AddOpportunity(opportunity);
                    }
                }
            }
        }

        return new UniqueIndexAggregation(uniqueIndexBuilder.ToImmutable(), indexModuleBuilder.ToImmutable());
    }
}

internal sealed record UniqueIndexAggregation(
    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> Decisions,
    ImmutableDictionary<IndexCoordinate, string> IndexModules);

