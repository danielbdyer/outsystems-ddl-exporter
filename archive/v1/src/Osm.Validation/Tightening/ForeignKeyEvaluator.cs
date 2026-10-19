using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tightening;

internal sealed class ForeignKeyEvaluator : ITighteningAnalyzer
{
    private readonly ForeignKeyOptions _options;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _foreignKeys;
    private readonly ForeignKeyTargetIndex _targetIndex;
    private readonly TighteningMode _mode;

    public ForeignKeyEvaluator(
        ForeignKeyOptions options,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeys,
        ForeignKeyTargetIndex targetIndex,
        TighteningMode mode)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _foreignKeys = foreignKeys ?? throw new ArgumentNullException(nameof(foreignKeys));
        _targetIndex = targetIndex ?? throw new ArgumentNullException(nameof(targetIndex));
        _mode = mode;
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
            OpportunityType.ForeignKey,
            "FOREIGN KEY",
            summary,
            risk,
            evaluation.Decision.Rationales,
            column: context.Column,
            disposition: OpportunityDisposition.NeedsRemediation);

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
        var scriptWithNoCheck = false;

        if (!attribute.Reference.IsReference)
        {
            return new ForeignKeyEvaluation(
                ForeignKeyDecision.Create(coordinate, createConstraint, scriptWithNoCheck, rationales.ToImmutableArray()),
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

        if (!hasConstraint && !hasOrphan && !crossSchemaBlocked && !crossCatalogBlocked && _options.EnableCreation)
        {
            createConstraint = true;
            rationales.Add(TighteningRationales.PolicyEnableCreation);
        }
        else
        {
            if (!_options.EnableCreation && !hasConstraint && !hasOrphan)
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

            if (!hasConstraint && createConstraint == false && _mode == TighteningMode.Cautious && _options.EnableCreation)
            {
                if (!crossSchemaBlocked && !crossCatalogBlocked && (hasOrphan || ignoreRule))
                {
                    createConstraint = true;
                    scriptWithNoCheck = true;
                    rationales.Add(TighteningRationales.ForeignKeyNoCheckRecommended);
                }
            }
        }

        var decision = ForeignKeyDecision.Create(coordinate, createConstraint, scriptWithNoCheck, rationales.ToImmutableArray());

        return new ForeignKeyEvaluation(
            decision,
            hasOrphan,
            ignoreRule,
            crossSchemaBlocked,
            crossCatalogBlocked);
    }

    private static bool ShouldCreateOpportunity(ForeignKeyEvaluation evaluation)
        => !evaluation.Decision.CreateConstraint || evaluation.Decision.ScriptWithNoCheck;

    private static string BuildForeignKeySummary(ForeignKeyEvaluation evaluation)
    {
        if (evaluation.Decision.ScriptWithNoCheck)
        {
            return "Foreign key constraint will be scripted WITH NOCHECK to honor the model while remediation occurs.";
        }

        if (evaluation.HasOrphan)
        {
            return "Foreign key constraint was not created. Resolve orphaned rows before enforcement can proceed.";
        }

        if (evaluation.IgnoreRule)
        {
            return "Foreign key constraint was not created. Delete rule 'Ignore' prevents constraint enforcement.";
        }

        if (evaluation.CrossSchemaBlocked || evaluation.CrossCatalogBlocked)
        {
            return "Foreign key constraint was not created. Cross-database references are blocked by policy. Allow cross-database enforcement or adjust the schema.";
        }

        return "Foreign key constraint was not created. Enable policy or gather evidence before constraint creation can proceed.";
    }

    private bool IsIgnoreRule(string? deleteRule)
        => IsIgnoreRule(deleteRule, _options);

    internal static bool IsIgnoreRule(string? deleteRule, ForeignKeyOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (options.TreatMissingDeleteRuleAsIgnore)
        {
            return string.IsNullOrWhiteSpace(deleteRule);
        }

        return false;
    }

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
