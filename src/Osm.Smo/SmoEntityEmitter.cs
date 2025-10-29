using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Smo;

internal sealed class SmoEntityEmitter
{
    private readonly Lazy<IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>>> _relationshipsByAttribute;
    private readonly Lazy<IReadOnlyDictionary<string, AttributeModel>> _attributesByLogicalName;

    public SmoEntityEmitter(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        EntityEmissionIndex entityLookup,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        TypeMappingPolicy typeMappingPolicy,
        SmoFormatOptions format,
        bool includePlatformAutoIndexes)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        EntityLookup = entityLookup ?? throw new ArgumentNullException(nameof(entityLookup));
        ProfileDefaults = profileDefaults ?? throw new ArgumentNullException(nameof(profileDefaults));
        ForeignKeyReality = foreignKeyReality ?? throw new ArgumentNullException(nameof(foreignKeyReality));
        TypeMappingPolicy = typeMappingPolicy ?? throw new ArgumentNullException(nameof(typeMappingPolicy));
        Format = format ?? throw new ArgumentNullException(nameof(format));
        IncludePlatformAutoIndexes = includePlatformAutoIndexes;

        _relationshipsByAttribute = new Lazy<IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>>>(
            () => BuildRelationshipLookup(Context.Entity),
            isThreadSafe: true);
        _attributesByLogicalName = new Lazy<IReadOnlyDictionary<string, AttributeModel>>(
            () => BuildAttributeLookup(Context.Entity),
            isThreadSafe: true);
    }

    public EntityEmissionContext Context { get; }

    public PolicyDecisionSet Decisions { get; }

    public EntityEmissionIndex EntityLookup { get; }

    public IReadOnlyDictionary<ColumnCoordinate, string> ProfileDefaults { get; }

    public IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> ForeignKeyReality { get; }

    public TypeMappingPolicy TypeMappingPolicy { get; }

    public SmoFormatOptions Format { get; }

    public bool IncludePlatformAutoIndexes { get; }

    public IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>> RelationshipsByAttribute => _relationshipsByAttribute.Value;

    public IReadOnlyDictionary<string, AttributeModel> AttributesByLogicalName => _attributesByLogicalName.Value;

    public ColumnCoordinate CreateCoordinate(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        return new ColumnCoordinate(Context.Entity.Schema, Context.Entity.PhysicalName, attribute.ColumnName);
    }

    public bool ShouldEnforceNotNull(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var coordinate = CreateCoordinate(attribute);
        if (Decisions.Nullability.TryGetValue(coordinate, out var decision))
        {
            return decision.MakeNotNull;
        }

        return attribute.IsMandatory;
    }

    public DataType ResolveAttributeDataType(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var dataType = TypeMappingPolicy.Resolve(attribute);
        if (attribute.Reference.IsReference &&
            EntityLookup.TryResolveReference(attribute.Reference, Context, out var targetContext))
        {
            var referencedIdentifier = targetContext.GetPreferredIdentifier();
            if (referencedIdentifier is not null)
            {
                dataType = TypeMappingPolicy.Resolve(referencedIdentifier);
            }
        }

        return dataType;
    }

    public bool TryResolveReference(AttributeModel attribute, out EntityEmissionContext targetContext)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (!attribute.Reference.IsReference)
        {
            targetContext = default!;
            return false;
        }

        return EntityLookup.TryResolveReference(attribute.Reference, Context, out targetContext);
    }

    public string ResolveEmissionColumnName(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var logical = attribute.LogicalName.Value;
        return string.IsNullOrWhiteSpace(logical) ? attribute.ColumnName.Value : logical;
    }

    public string? ResolveDefaultExpression(AttributeModel attribute, ColumnCoordinate coordinate)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (!string.IsNullOrWhiteSpace(attribute.OnDisk.DefaultDefinition))
        {
            return attribute.OnDisk.DefaultDefinition;
        }

        if (ProfileDefaults.TryGetValue(coordinate, out var profileDefault) &&
            !string.IsNullOrWhiteSpace(profileDefault))
        {
            return profileDefault;
        }

        return string.IsNullOrWhiteSpace(attribute.DefaultValue) ? null : attribute.DefaultValue;
    }

    public ForeignKeyAction MapDeleteRule(string? deleteRule)
    {
        if (string.IsNullOrWhiteSpace(deleteRule))
        {
            return ForeignKeyAction.NoAction;
        }

        return deleteRule.Trim() switch
        {
            "Cascade" => ForeignKeyAction.Cascade,
            "Delete" => ForeignKeyAction.Cascade,
            "Protect" => ForeignKeyAction.NoAction,
            "Ignore" => ForeignKeyAction.NoAction,
            "SetNull" => ForeignKeyAction.SetNull,
            _ => ForeignKeyAction.NoAction,
        };
    }

    private static IReadOnlyDictionary<string, ImmutableArray<RelationshipModel>> BuildRelationshipLookup(EntityModel entity)
    {
        if (entity.Relationships.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, ImmutableArray<RelationshipModel>>.Empty;
        }

        return entity.Relationships
            .GroupBy(static relationship => relationship.ViaAttribute.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, AttributeModel> BuildAttributeLookup(EntityModel entity)
    {
        if (entity.Attributes.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, AttributeModel>.Empty;
        }

        return entity.Attributes
            .GroupBy(static attribute => attribute.LogicalName.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.First(),
                StringComparer.OrdinalIgnoreCase);
    }
}
