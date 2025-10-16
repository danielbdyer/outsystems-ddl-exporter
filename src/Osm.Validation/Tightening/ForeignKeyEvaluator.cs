using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;

namespace Osm.Validation.Tightening;

internal sealed class ForeignKeyEvaluator
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
            return ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray());
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

        return ForeignKeyDecision.Create(coordinate, createConstraint, rationales.ToImmutableArray());
    }

    private static bool IsIgnoreRule(string? deleteRule)
        => string.IsNullOrWhiteSpace(deleteRule) || string.Equals(deleteRule, "Ignore", StringComparison.OrdinalIgnoreCase);

    private static bool SchemaEquals(SchemaName left, SchemaName right)
        => string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
