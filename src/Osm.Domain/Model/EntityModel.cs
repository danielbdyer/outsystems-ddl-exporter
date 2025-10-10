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
        EntityMetadata? metadata = null)
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

        if (!attributeArray.Any(a => a.IsIdentifier))
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.attributes.missingPrimaryKey", "Entity must define at least one primary key attribute."));
        }

        if (HasDuplicates(attributeArray.Select(a => a.LogicalName.Value)))
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.attributes.duplicateLogical", "Duplicate attribute logical names detected."));
        }

        if (HasDuplicates(attributeArray.Select(a => a.ColumnName.Value), StringComparer.OrdinalIgnoreCase))
        {
            return Result<EntityModel>.Failure(ValidationError.Create("entity.attributes.duplicateColumn", "Duplicate attribute column names detected."));
        }

        var indexArray = (indexes ?? Enumerable.Empty<IndexModel>()).ToImmutableArray();
        var relationshipArray = (relationships ?? Enumerable.Empty<RelationshipModel>()).ToImmutableArray();
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
            metadata ?? EntityMetadata.Empty));
    }

    private static bool HasDuplicates(IEnumerable<string> values, IEqualityComparer<string>? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        var set = new HashSet<string>(comparer);
        foreach (var value in values)
        {
            if (!set.Add(value))
            {
                return true;
            }
        }

        return false;
    }
}
