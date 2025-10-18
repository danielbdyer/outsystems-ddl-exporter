using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

internal sealed class ForeignKeyEvaluator : ITighteningAnalyzer
{
    private readonly ForeignKeyOptions _options;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _foreignKeys;
    private readonly ForeignKeyTargetIndex _targetIndex;

    public ForeignKeyEvaluator(
        ForeignKeyOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeys,
        ForeignKeyTargetIndex targetIndex)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _foreignKeys = foreignKeys ?? throw new ArgumentNullException(nameof(foreignKeys));
        _targetIndex = targetIndex ?? throw new ArgumentNullException(nameof(targetIndex));
    }

    public ForeignKeyDecision Evaluate(EntityModel entity, AttributeModel attribute, ColumnCoordinate coordinate)
    {
        var evaluation = EvaluateCore(entity, attribute, coordinate);
        return evaluation.Decision;
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

        var evaluation = EvaluateCore(context.Entity, context.Attribute, context.Column);

        if (!context.Attribute.Reference.IsReference)
        {
            return;
        }

        builder.SetForeignKey(evaluation.Decision);

        if (!ShouldCreateOpportunity(evaluation))
        {
            return;
        }

        var summary = BuildForeignKeySummary(evaluation);
        var risk = ChangeRiskClassifier.ForForeignKey(
            evaluation.Decision,
            evaluation.HasOrphan,
            evaluation.IgnoreRule,
            evaluation.CrossSchemaBlocked,
            evaluation.CrossCatalogBlocked);

        var opportunity = Opportunity.Create(
            OpportunityCategory.ForeignKey,
            "FOREIGN KEY",
            summary,
            risk,
            evaluation.Decision.Rationales,
            column: context.Column);

        builder.AddOpportunity(opportunity);
    }

    private ForeignKeyEvaluation EvaluateCore(EntityModel entity, AttributeModel attribute, ColumnCoordinate coordinate)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        var createConstraint = false;

        if (!attribute.Reference.IsReference)
        {
            return new ForeignKeyEvaluation(
                ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray()),
                HasOrphan: false,
                IgnoreRule: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        var fkReality = _foreignKeys.TryGetValue(coordinate, out var fk) ? fk : null;
        var targetEntity = _targetIndex.GetTarget(coordinate);

        var ignoreRule = IsIgnoreRule(attribute.Reference.DeleteRuleCode);
        if (ignoreRule)
        {
            rationales.Add(TighteningRationales.DeleteRuleIgnore);
        }

        var hasOrphan = fkReality?.HasOrphan ?? false;
        if (hasOrphan)
        {
            rationales.Add(TighteningRationales.DataHasOrphans);
        }

        var hasConstraint = fkReality?.Reference.HasDatabaseConstraint ?? false;
        if (hasConstraint)
        {
            createConstraint = true;
            rationales.Add(TighteningRationales.DatabaseConstraintPresent);
        }

        var crossSchema = targetEntity is not null && !SchemaEquals(entity.Schema, targetEntity.Schema);
        var crossCatalog = targetEntity is not null && !CatalogEquals(entity.Catalog, targetEntity.Catalog);

        var crossSchemaBlocked = crossSchema && !_options.AllowCrossSchema && !hasConstraint;
        var crossCatalogBlocked = crossCatalog && !_options.AllowCrossCatalog && !hasConstraint;

        if (!hasConstraint && !ignoreRule && !hasOrphan && !crossSchemaBlocked && !crossCatalogBlocked && _options.EnableCreation)
        {
            createConstraint = true;
            rationales.Add(TighteningRationales.PolicyEnableCreation);
        }
        else
        {
            if (!_options.EnableCreation && !hasConstraint && !ignoreRule && !hasOrphan)
            {
                rationales.Add(TighteningRationales.ForeignKeyCreationDisabled);
            }

            if (crossSchemaBlocked)
            {
                rationales.Add(TighteningRationales.CrossSchema);
            }

            if (crossCatalogBlocked)
            {
                rationales.Add(TighteningRationales.CrossCatalog);
            }
        }

        var decision = ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray());

        return new ForeignKeyEvaluation(
            decision,
            hasOrphan,
            ignoreRule,
            crossSchemaBlocked,
            crossCatalogBlocked);
    }

    private static bool ShouldCreateOpportunity(ForeignKeyEvaluation evaluation)
        => !evaluation.Decision.CreateConstraint;

    private static string BuildForeignKeySummary(ForeignKeyEvaluation evaluation)
    {
        if (evaluation.HasOrphan)
        {
            return "Resolve orphaned rows before enforcing the foreign key.";
        }

        if (evaluation.IgnoreRule)
        {
            return "Delete rule 'Ignore' prevents creating the foreign key.";
        }

        if (evaluation.CrossSchemaBlocked || evaluation.CrossCatalogBlocked)
        {
            return "Allow cross-database enforcement or adjust the schema before creating the foreign key.";
        }

        return "Enable policy or gather evidence before creating the foreign key.";
    }

    private static bool IsIgnoreRule(string? deleteRule)
        => string.IsNullOrWhiteSpace(deleteRule) || string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase);

    private static bool SchemaEquals(SchemaName left, SchemaName right)
        => string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private sealed record ForeignKeyEvaluation(
        ForeignKeyDecision Decision,
        bool HasOrphan,
        bool IgnoreRule,
        bool CrossSchemaBlocked,
        bool CrossCatalogBlocked);
}
