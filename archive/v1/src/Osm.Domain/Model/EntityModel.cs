using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record EntityModel(
    ModuleName Module,
    EntityName LogicalName,
    TableName PhysicalName,
    SchemaName Schema,
    string? Catalog,
    bool IsStatic,
    bool IsExternal,
    bool IsActive,
    ImmutableArray<AttributeModel> Attributes,
    ImmutableArray<IndexModel> Indexes,
    ImmutableArray<RelationshipModel> Relationships,
    ImmutableArray<TriggerModel> Triggers,
    EntityMetadata Metadata)
{
    public static Result<EntityModel> Create(
        ModuleName module,
        EntityName logicalName,
        TableName physicalName,
        SchemaName schema,
        string? catalog,
        bool isStatic,
        bool isExternal,
        bool isActive,
        IEnumerable<AttributeModel> attributes,
        IEnumerable<IndexModel>? indexes = null,
        IEnumerable<RelationshipModel>? relationships = null,
        IEnumerable<TriggerModel>? triggers = null,
        EntityMetadata? metadata = null,
        bool allowMissingPrimaryKey = false,
        bool allowDuplicateAttributeLogicalNames = false,
        bool allowDuplicateAttributeColumnNames = false)
    {
        if (attributes is null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        var attributeArray = attributes.ToImmutableArray();
        if (attributeArray.IsDefaultOrEmpty)
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.attributes.empty", "Entity must contain at least one attribute."));
        }

        if (!allowMissingPrimaryKey && !attributeArray.Any(a => a.IsIdentifier))
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.attributes.missingPrimaryKey", "Entity must define at least one primary key attribute."));
        }

        var duplicateLogicalGroups = FindDuplicateGroups(
            attributeArray,
            static attribute => attribute.LogicalName.Value,
            StringComparer.Ordinal);
        if (!duplicateLogicalGroups.IsDefaultOrEmpty && !allowDuplicateAttributeLogicalNames)
        {
            return Result<EntityModel>.Failure(ValidationError.Create(
                "entity.attributes.duplicateLogical",
                FormatDuplicateLogicalMessage(duplicateLogicalGroups)));
        }

        var duplicateColumnGroups = FindDuplicateGroups(
            attributeArray,
            static attribute => attribute.ColumnName.Value,
            StringComparer.OrdinalIgnoreCase);
        if (!duplicateColumnGroups.IsDefaultOrEmpty && !allowDuplicateAttributeColumnNames)
        {
            return Result<EntityModel>.Failure(ValidationError.Create(
                "entity.attributes.duplicateColumn",
                FormatDuplicateColumnMessage(duplicateColumnGroups)));
        }

        var indexArray = (indexes ?? Enumerable.Empty<IndexModel>()).ToImmutableArray();
        var relationshipArray = (relationships ?? Enumerable.Empty<RelationshipModel>()).ToImmutableArray();
        var triggerArray = (triggers ?? Enumerable.Empty<TriggerModel>()).ToImmutableArray();

        if (GetDuplicates(triggerArray.Select(t => t.Name.Value), StringComparer.OrdinalIgnoreCase).Count > 0)
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.triggers.duplicateName", "Trigger names must be unique."));
        }

        var normalizedCatalog = string.IsNullOrWhiteSpace(catalog) ? null : catalog!.Trim();

        return Result<EntityModel>.Success(new EntityModel(
            module,
            logicalName,
            physicalName,
            schema,
            normalizedCatalog,
            isStatic,
            isExternal,
            isActive,
            attributeArray,
            indexArray,
            relationshipArray,
            triggerArray,
            metadata ?? EntityMetadata.Empty));
    }

    private static ImmutableArray<DuplicateAttributeGroup> FindDuplicateGroups(
        ImmutableArray<AttributeModel> attributes,
        Func<AttributeModel, string> keySelector,
        IEqualityComparer<string> comparer)
    {
        if (attributes.IsDefaultOrEmpty)
        {
            return ImmutableArray<DuplicateAttributeGroup>.Empty;
        }

        return attributes
            .GroupBy(keySelector, comparer)
            .Where(static group => group.Count() > 1)
            .Select(static group => new DuplicateAttributeGroup(
                group.Key,
                group.Select(static attribute => attribute.LogicalName.Value).ToImmutableArray(),
                group.Select(static attribute => attribute.ColumnName.Value).ToImmutableArray()))
            .ToImmutableArray();
    }

    private static string FormatDuplicateLogicalMessage(ImmutableArray<DuplicateAttributeGroup> duplicates)
    {
        var details = duplicates
            .Select(static group =>
            {
                var columns = string.Join(", ", group.ColumnNames.Select(static name => $"'{name}'"));
                return $"'{group.Key}' mapped to columns {columns}";
            });

        var suffix = duplicates.Length == 1 ? "name" : "names";
        return $"Duplicate attribute logical {suffix} detected: {string.Join("; ", details)}.";
    }

    private static string FormatDuplicateColumnMessage(ImmutableArray<DuplicateAttributeGroup> duplicates)
    {
        var details = duplicates
            .Select(static group =>
            {
                var attributes = string.Join(", ", group.LogicalNames.Select(static name => $"'{name}'"));
                return $"'{group.Key}' shared by attributes {attributes}";
            });

        var suffix = duplicates.Length == 1 ? "name" : "names";
        return $"Duplicate attribute column {suffix} detected: {string.Join("; ", details)}.";
    }

    private static IReadOnlyCollection<string> GetDuplicates(
        IEnumerable<string> values,
        IEqualityComparer<string> comparer)
    {
        var set = new HashSet<string>(comparer);
        var duplicates = new HashSet<string>(comparer);
        foreach (var value in values)
        {
            if (!set.Add(value))
            {
                duplicates.Add(value);
            }
        }

        return duplicates.Count == 0
            ? Array.Empty<string>()
            : duplicates.ToArray();
    }

    private readonly record struct DuplicateAttributeGroup(
        string Key,
        ImmutableArray<string> LogicalNames,
        ImmutableArray<string> ColumnNames);
}
