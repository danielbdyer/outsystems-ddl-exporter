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

        if (!attribute.Reference.IsReference)
        {
            return new ForeignKeyEvaluation(
                ForeignKeyDecision.Create(coordinate, createConstraint: false, ImmutableArray<string>.Empty),
                HasOrphan: false,
                IgnoreRule: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        var fkReality = _foreignKeys.TryGetValue(coordinate, out var fk) ? fk : null;
        var targetEntity = _targetIndex.GetTarget(coordinate);

        var evaluation = EvaluateScenario(entity, attribute, fkReality, targetEntity);
        var definition = TighteningPolicyMatrix.ForeignKeys.Resolve(evaluation.Scenario);

        var rationales = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var rationale in definition.Rationales)
        {
            rationales.Add(rationale);
        }

        var decision = ForeignKeyDecision.Create(coordinate, definition.ExpectCreate, rationales.ToImmutableArray());

        return new ForeignKeyEvaluation(
            decision,
            evaluation.HasOrphan,
            evaluation.IgnoreRule,
            evaluation.CrossSchemaBlocked,
            evaluation.CrossCatalogBlocked);
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

    private ForeignKeyScenarioEvaluation EvaluateScenario(
        EntityModel entity,
        AttributeModel attribute,
        ForeignKeyReality? reality,
        EntityModel? targetEntity)
    {
        var ignoreRule = IsIgnoreRule(attribute.Reference.DeleteRuleCode);
        if (ignoreRule)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.IgnoreRule,
                IgnoreRule: true,
                HasOrphan: reality?.HasOrphan ?? false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        var hasOrphan = reality?.HasOrphan ?? false;
        if (hasOrphan)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.HasOrphan,
                IgnoreRule: false,
                HasOrphan: true,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        var hasConstraint = reality?.Reference.HasDatabaseConstraint ?? false;
        if (hasConstraint)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.ExistingConstraint,
                IgnoreRule: false,
                HasOrphan: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        if (!_options.EnableCreation)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.PolicyDisabled,
                IgnoreRule: false,
                HasOrphan: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        if (targetEntity is null)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.PolicyDisabled,
                IgnoreRule: false,
                HasOrphan: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: false);
        }

        var crossSchema = !SchemaEquals(entity.Schema, targetEntity.Schema);
        var crossCatalog = !CatalogEquals(entity.Catalog, targetEntity.Catalog);
        var crossSchemaBlocked = crossSchema && !_options.AllowCrossSchema;
        var crossCatalogBlocked = crossCatalog && !_options.AllowCrossCatalog;

        if (crossSchemaBlocked)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossSchemaBlocked,
                IgnoreRule: false,
                HasOrphan: false,
                CrossSchemaBlocked: true,
                CrossCatalogBlocked: crossCatalogBlocked);
        }

        if (crossCatalogBlocked)
        {
            return new ForeignKeyScenarioEvaluation(
                TighteningPolicyMatrix.ForeignKeyPolicyScenario.CrossCatalogBlocked,
                IgnoreRule: false,
                HasOrphan: false,
                CrossSchemaBlocked: false,
                CrossCatalogBlocked: true);
        }

        return new ForeignKeyScenarioEvaluation(
            TighteningPolicyMatrix.ForeignKeyPolicyScenario.Eligible,
            IgnoreRule: false,
            HasOrphan: false,
            CrossSchemaBlocked: false,
            CrossCatalogBlocked: false);
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

    private sealed record ForeignKeyScenarioEvaluation(
        TighteningPolicyMatrix.ForeignKeyPolicyScenario Scenario,
        bool IgnoreRule,
        bool HasOrphan,
        bool CrossSchemaBlocked,
        bool CrossCatalogBlocked);
}
