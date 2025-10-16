using System;
using System.Collections.Generic;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tightening.Signals;

internal sealed record ForeignKeySupportSignal()
    : NullabilitySignal("S3_FK_SUPPORT", "Foreign key has enforced relationship or can be created safely")
{
    protected override SignalEvaluation EvaluateCore(in NullabilitySignalContext context)
    {
        if (!context.Attribute.Reference.IsReference)
        {
            return SignalEvaluation.Create(Code, Description, result: false);
        }

        var rationales = new List<string>();

        if (IsIgnoreRule(context.Attribute.Reference.DeleteRuleCode))
        {
            rationales.Add(TighteningRationales.DeleteRuleIgnore);
        }

        if (context.ForeignKeyReality?.HasOrphan == true)
        {
            rationales.Add(TighteningRationales.DataHasOrphans);
        }

        var supports = context.ForeignKeyReality is ForeignKeyReality reality
            && !reality.HasOrphan
            && !IsIgnoreRule(context.Attribute.Reference.DeleteRuleCode)
            && ForeignKeySupportsTightening(
                context.Entity,
                reality,
                context.Options.ForeignKeys,
                context.ForeignKeyTarget);

        if (supports)
        {
            rationales.Add(TighteningRationales.ForeignKeyEnforced);
        }

        return SignalEvaluation.Create(Code, Description, supports, rationales);
    }

    private static bool ForeignKeySupportsTightening(
        EntityModel entity,
        ForeignKeyReality reality,
        ForeignKeyOptions options,
        EntityModel? target)
    {
        if (reality.Reference.HasDatabaseConstraint)
        {
            return true;
        }

        if (!options.EnableCreation)
        {
            return false;
        }

        if (target is null)
        {
            return false;
        }

        if (!options.AllowCrossSchema && !SchemaEquals(entity.Schema, target.Schema))
        {
            return false;
        }

        if (!options.AllowCrossCatalog && !CatalogEquals(entity.Catalog, target.Catalog))
        {
            return false;
        }

        return true;
    }

    private static bool SchemaEquals(SchemaName left, SchemaName right)
        => string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoreRule(string? deleteRule)
        => string.IsNullOrWhiteSpace(deleteRule) || string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase);
}
