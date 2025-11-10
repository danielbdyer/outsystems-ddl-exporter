using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Pipeline.Profiling;

internal enum ForeignKeyResolutionKind
{
    Resolved,
    Ambiguous,
    Missing
}

internal sealed record ForeignKeyResolution(
    ForeignKeyResolutionKind Kind,
    EntityModel? TargetEntity,
    AttributeModel? TargetAttribute,
    bool HasDatabaseConstraint,
    ImmutableArray<EntityModel> Candidates)
{
    public static ForeignKeyResolution Resolved(
        EntityModel entity,
        AttributeModel attribute,
        bool hasDatabaseConstraint)
    {
        return new ForeignKeyResolution(
            ForeignKeyResolutionKind.Resolved,
            entity,
            attribute,
            hasDatabaseConstraint,
            ImmutableArray<EntityModel>.Empty);
    }

    public static ForeignKeyResolution Ambiguous(
        bool hasDatabaseConstraint,
        IEnumerable<EntityModel> candidates)
    {
        return new ForeignKeyResolution(
            ForeignKeyResolutionKind.Ambiguous,
            null,
            null,
            hasDatabaseConstraint,
            candidates.ToImmutableArray());
    }

    public static ForeignKeyResolution Missing(bool hasDatabaseConstraint)
    {
        return new ForeignKeyResolution(
            ForeignKeyResolutionKind.Missing,
            null,
            null,
            hasDatabaseConstraint,
            ImmutableArray<EntityModel>.Empty);
    }
}

internal sealed class ForeignKeyMappingResolver
{
    private readonly Dictionary<string, ImmutableArray<EntityModel>> _entitiesByLogicalName;
    private readonly Dictionary<string, ImmutableArray<EntityModel>> _entitiesByPhysicalName;
    private readonly Dictionary<(string Schema, string Table), EntityModel> _entitiesBySchemaAndTable;
    private readonly NamingOverrideOptions _namingOverrides;

    public ForeignKeyMappingResolver(OsmModel model, NamingOverrideOptions namingOverrides)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        _namingOverrides = namingOverrides ?? throw new ArgumentNullException(nameof(namingOverrides));

        _entitiesByLogicalName = model.Modules
            .SelectMany(static module => module.Entities)
            .GroupBy(static entity => entity.LogicalName.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);

        _entitiesByPhysicalName = model.Modules
            .SelectMany(static module => module.Entities)
            .GroupBy(static entity => entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);

        _entitiesBySchemaAndTable = model.Modules
            .SelectMany(static module => module.Entities)
            .ToDictionary(
                entity => (entity.Schema.Value, entity.PhysicalName.Value),
                entity => entity,
                TableKeyComparer.Instance);
    }

    public ForeignKeyResolution Resolve(
        ModuleModel module,
        EntityModel entity,
        AttributeModel attribute)
    {
        if (!attribute.Reference.IsReference)
        {
            return ForeignKeyResolution.Missing(false);
        }

        var relationship = entity.Relationships
            .FirstOrDefault(r => string.Equals(r.ViaAttribute.Value, attribute.LogicalName.Value, StringComparison.Ordinal));

        var hasDatabaseConstraint = attribute.Reference.HasDatabaseConstraint || relationship?.HasDatabaseConstraint == true;

        var targetEntity = attribute.Reference.TargetEntity;
        var logicalName = targetEntity?.Value;
        var physicalName = attribute.Reference.TargetPhysicalName?.Value;
        if (relationship?.TargetPhysicalName is { } relPhysical)
        {
            physicalName ??= relPhysical.Value;
        }

        var actualConstraintEntities = ResolveFromActualConstraints(relationship);
        var fromConstraints = TryResolveCandidates(
            actualConstraintEntities,
            module,
            entity,
            physicalName,
            logicalName,
            relationship,
            hasDatabaseConstraint);
        if (fromConstraints is not null)
        {
            return fromConstraints;
        }

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            var overrideResolution = TryResolveOverride(logicalName, relationship, hasDatabaseConstraint);
            if (overrideResolution is not null)
            {
                return overrideResolution;
            }
        }

        var physicalCandidates = ResolveByPhysicalName(physicalName);
        var fromPhysical = TryResolveCandidates(
            physicalCandidates,
            module,
            entity,
            physicalName,
            logicalName,
            relationship,
            hasDatabaseConstraint);
        if (fromPhysical is not null)
        {
            return fromPhysical;
        }

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            var logicalCandidates = ResolveByLogicalName(logicalName);
            var fromLogical = TryResolveCandidates(
                logicalCandidates,
                module,
                entity,
                physicalName,
                logicalName,
                relationship,
                hasDatabaseConstraint);
            if (fromLogical is not null)
            {
                return fromLogical;
            }
        }

        return ForeignKeyResolution.Missing(hasDatabaseConstraint);
    }

    private ForeignKeyResolution? TryResolveOverride(
        string logicalName,
        RelationshipModel? relationship,
        bool hasDatabaseConstraint)
    {
        if (!_entitiesByLogicalName.TryGetValue(logicalName, out var matches))
        {
            return null;
        }

        var overrideMatches = matches
            .Where(candidate => candidate.IsActive &&
                _namingOverrides.TryGetModuleScopedEntityOverride(candidate.Module.Value, logicalName, out _))
            .Distinct()
            .ToList();

        if (overrideMatches.Count == 1)
        {
            var targetAttribute = ResolveTargetAttribute(overrideMatches[0], relationship);
            if (targetAttribute is not null)
            {
                return ForeignKeyResolution.Resolved(overrideMatches[0], targetAttribute, hasDatabaseConstraint);
            }
        }

        return null;
    }

    private List<EntityModel> ResolveFromActualConstraints(RelationshipModel? relationship)
    {
        if (relationship is null || relationship.ActualConstraints.IsDefaultOrEmpty)
        {
            return new List<EntityModel>();
        }

        var results = new List<EntityModel>();
        foreach (var constraint in relationship.ActualConstraints)
        {
            if (!string.IsNullOrWhiteSpace(constraint.ReferencedSchema) &&
                !string.IsNullOrWhiteSpace(constraint.ReferencedTable) &&
                _entitiesBySchemaAndTable.TryGetValue(
                    (constraint.ReferencedSchema, constraint.ReferencedTable),
                    out var entity))
            {
                results.Add(entity);
            }
            else if (!string.IsNullOrWhiteSpace(constraint.ReferencedTable) &&
                _entitiesByPhysicalName.TryGetValue(constraint.ReferencedTable, out var physicalMatches))
            {
                results.AddRange(physicalMatches);
            }
        }

        return CollectDistinctActive(results);
    }

    private List<EntityModel> ResolveByPhysicalName(string? physicalName)
    {
        if (string.IsNullOrWhiteSpace(physicalName))
        {
            return new List<EntityModel>();
        }

        return _entitiesByPhysicalName.TryGetValue(physicalName, out var matches)
            ? CollectDistinctActive(matches)
            : new List<EntityModel>();
    }

    private List<EntityModel> ResolveByLogicalName(string logicalName)
    {
        if (_entitiesByLogicalName.TryGetValue(logicalName, out var matches))
        {
            return CollectDistinctActive(matches);
        }

        return new List<EntityModel>();
    }

    private ForeignKeyResolution? TryResolveCandidates(
        List<EntityModel> candidates,
        ModuleModel module,
        EntityModel entity,
        string? expectedPhysicalName,
        string? logicalName,
        RelationshipModel? relationship,
        bool hasDatabaseConstraint)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var filtered = candidates.Count == 1
            ? candidates
            : FilterCandidates(candidates, module, entity, expectedPhysicalName, logicalName);

        if (filtered.Count == 0)
        {
            return null;
        }

        if (filtered.Count == 1)
        {
            var candidate = filtered[0];
            var targetAttribute = ResolveTargetAttribute(candidate, relationship);
            if (targetAttribute is not null)
            {
                return ForeignKeyResolution.Resolved(candidate, targetAttribute, hasDatabaseConstraint);
            }

            return null;
        }

        if (!hasDatabaseConstraint)
        {
            return ForeignKeyResolution.Ambiguous(false, filtered);
        }

        return null;
    }

    private List<EntityModel> FilterCandidates(
        IReadOnlyCollection<EntityModel> candidates,
        ModuleModel module,
        EntityModel entity,
        string? expectedPhysicalName,
        string? logicalName)
    {
        var filtered = candidates
            .Where(static candidate => candidate.IsActive)
            .Distinct()
            .ToList();

        if (filtered.Count <= 1)
        {
            return filtered;
        }

        if (!string.IsNullOrWhiteSpace(logicalName))
        {
            var overrideMatches = filtered
                .Where(candidate => _namingOverrides.TryGetModuleScopedEntityOverride(
                    candidate.Module.Value,
                    logicalName,
                    out _))
                .ToList();

            if (overrideMatches.Count == 1)
            {
                return overrideMatches;
            }

            if (overrideMatches.Count > 1)
            {
                filtered = overrideMatches;
            }
        }

        var expectedPrefix = !string.IsNullOrWhiteSpace(expectedPhysicalName)
            ? ExtractModulePrefix(expectedPhysicalName)
            : string.Empty;

        var ownerPrefix = ExtractModulePrefix(entity.PhysicalName.Value);
        var hasExplicitPrefix = !string.IsNullOrEmpty(expectedPrefix);
        var isExplicitCrossModule = hasExplicitPrefix &&
            !string.IsNullOrEmpty(ownerPrefix) &&
            !string.Equals(expectedPrefix, ownerPrefix, StringComparison.OrdinalIgnoreCase);

        if (hasExplicitPrefix)
        {
            var prefixMatches = filtered
                .Where(candidate => string.Equals(
                    ExtractModulePrefix(candidate.PhysicalName.Value),
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (prefixMatches.Count == 1)
            {
                return prefixMatches;
            }

            if (prefixMatches.Count > 1)
            {
                filtered = prefixMatches;
            }
            else if (prefixMatches.Count == 0)
            {
                isExplicitCrossModule = false;
            }
        }

        if (!isExplicitCrossModule)
        {
            var sameModule = filtered
                .Where(candidate => string.Equals(
                    candidate.Module.Value,
                    module.Name.Value,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sameModule.Count == 1)
            {
                return sameModule;
            }

            if (sameModule.Count > 1)
            {
                filtered = sameModule;
            }
        }

        if (!hasExplicitPrefix && !string.IsNullOrEmpty(ownerPrefix))
        {
            var prefixMatches = filtered
                .Where(candidate => string.Equals(
                    ExtractModulePrefix(candidate.PhysicalName.Value),
                    ownerPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (prefixMatches.Count == 1)
            {
                return prefixMatches;
            }

            if (prefixMatches.Count > 1)
            {
                filtered = prefixMatches;
            }
        }

        return filtered;
    }

    private static AttributeModel? ResolveTargetAttribute(EntityModel targetEntity, RelationshipModel? relationship)
    {
        if (relationship is not null && !relationship.ActualConstraints.IsDefaultOrEmpty)
        {
            foreach (var constraint in relationship.ActualConstraints)
            {
                foreach (var column in constraint.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(column.ReferencedAttribute))
                    {
                        var logicalMatch = targetEntity.Attributes
                            .FirstOrDefault(attribute => string.Equals(
                                attribute.LogicalName.Value,
                                column.ReferencedAttribute,
                                StringComparison.Ordinal));

                        if (logicalMatch is not null)
                        {
                            return logicalMatch;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(column.ReferencedColumn))
                    {
                        var physicalMatch = targetEntity.Attributes
                            .FirstOrDefault(attribute => string.Equals(
                                attribute.ColumnName.Value,
                                column.ReferencedColumn,
                                StringComparison.OrdinalIgnoreCase));

                        if (physicalMatch is not null)
                        {
                            return physicalMatch;
                        }
                    }
                }
            }
        }

        return targetEntity.Attributes.FirstOrDefault(static attribute => attribute.IsIdentifier);
    }

    private static List<EntityModel> CollectDistinctActive(IEnumerable<EntityModel> entities)
    {
        return entities
            .Where(static entity => entity.IsActive)
            .Distinct()
            .ToList();
    }

    private static string ExtractModulePrefix(string physicalName)
    {
        if (string.IsNullOrWhiteSpace(physicalName))
        {
            return string.Empty;
        }

        const string Prefix = "OSUSR_";
        if (!physicalName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var remainder = physicalName.Substring(Prefix.Length);
        var underscoreIndex = remainder.IndexOf('_');
        return underscoreIndex > 0 ? remainder[..underscoreIndex] : remainder;
    }
}
