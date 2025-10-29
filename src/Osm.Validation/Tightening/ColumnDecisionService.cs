using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

internal sealed class ColumnDecisionService
{
    private readonly TighteningLookupContext _context;
    private readonly IReadOnlyList<ITighteningAnalyzer> _analyzers;

    public ColumnDecisionService(TighteningLookupContext context, IEnumerable<ITighteningAnalyzer> analyzers)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (analyzers is null)
        {
            throw new ArgumentNullException(nameof(analyzers));
        }

        _analyzers = analyzers is IReadOnlyList<ITighteningAnalyzer> analyzerList
            ? analyzerList
            : new List<ITighteningAnalyzer>(analyzers);
    }

    public ColumnDecisionResult Analyze()
    {
        var nullabilityBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, NullabilityDecision>();
        var foreignKeyBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyDecision>();
        var moduleBuilder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        var analysisBuilders = new Dictionary<ColumnCoordinate, ColumnAnalysisBuilder>();

        foreach (var module in _context.Model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var attributes = _context.AttributeIndex.GetAttributes(entity);
                foreach (var attribute in attributes)
                {
                    var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
                    if (!analysisBuilders.TryGetValue(coordinate, out var builder))
                    {
                        builder = new ColumnAnalysisBuilder(coordinate);
                        analysisBuilders[coordinate] = builder;
                    }

                    var entityContext = _context.CreateEntityContext(entity, attribute, coordinate);

                    foreach (var analyzer in _analyzers)
                    {
                        analyzer.Analyze(entityContext, builder);
                    }

                    nullabilityBuilder[coordinate] = builder.Nullability;
                    moduleBuilder[coordinate] = entity.Module.Value;

                    if (builder.ForeignKey is not null)
                    {
                        foreignKeyBuilder[coordinate] = builder.ForeignKey;
                    }
                }
            }
        }

        return new ColumnDecisionResult(
            nullabilityBuilder.ToImmutable(),
            foreignKeyBuilder.ToImmutable(),
            moduleBuilder.ToImmutable(),
            analysisBuilders);
    }
}

internal sealed record ColumnDecisionResult(
    ImmutableDictionary<ColumnCoordinate, NullabilityDecision> NullabilityDecisions,
    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> ForeignKeyDecisions,
    ImmutableDictionary<ColumnCoordinate, string> ColumnModules,
    IReadOnlyDictionary<ColumnCoordinate, ColumnAnalysisBuilder> AnalysisBuilders);
