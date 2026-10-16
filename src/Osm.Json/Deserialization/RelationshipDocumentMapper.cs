using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using RelationshipConstraintColumnDocument = ModelJsonDeserializer.RelationshipConstraintColumnDocument;
using RelationshipConstraintDocument = ModelJsonDeserializer.RelationshipConstraintDocument;
using RelationshipDocument = ModelJsonDeserializer.RelationshipDocument;

internal sealed class RelationshipDocumentMapper
{
    private readonly DocumentMapperContext _context;

    public RelationshipDocumentMapper(DocumentMapperContext context)
    {
        _context = context;
    }

    public Result<ImmutableArray<RelationshipModel>> Map(RelationshipDocument[]? docs, DocumentPathContext path)
        => _context.MapArray<RelationshipDocument, RelationshipModel>(docs, path, (doc, relationshipPath) =>
        {
            var attributeResult = AttributeName.Create(doc.ViaAttributeName);
            if (attributeResult.IsFailure)
            {
                return Result<RelationshipModel>.Failure(
                    _context.WithPath(relationshipPath.Property("viaAttributeName"), attributeResult.Errors));
            }

            var entityResult = EntityName.Create(doc.TargetEntityName);
            if (entityResult.IsFailure)
            {
                return Result<RelationshipModel>.Failure(
                    _context.WithPath(relationshipPath.Property("toEntity_name"), entityResult.Errors));
            }

            var tableResult = TableName.Create(doc.TargetEntityPhysicalName);
            if (tableResult.IsFailure)
            {
                return Result<RelationshipModel>.Failure(
                    _context.WithPath(relationshipPath.Property("toEntity_physicalName"), tableResult.Errors));
            }

            var hasConstraint = doc.HasDbConstraint switch
            {
                null => (bool?)null,
                0 => false,
                _ => true
            };

            var relationshipResult = RelationshipModel.Create(
                attributeResult.Value,
                entityResult.Value,
                tableResult.Value,
                doc.DeleteRuleCode,
                hasConstraint,
                MapActualConstraints(doc));

            return relationshipResult.IsFailure
                ? Result<RelationshipModel>.Failure(_context.WithPath(relationshipPath, relationshipResult.Errors))
                : relationshipResult;
        });

    private static IEnumerable<RelationshipActualConstraint> MapActualConstraints(RelationshipDocument doc)
    {
        if (doc.ActualConstraints is null || doc.ActualConstraints.Length == 0)
        {
            return Array.Empty<RelationshipActualConstraint>();
        }

        var constraints = new List<RelationshipActualConstraint>(doc.ActualConstraints.Length);
        foreach (var constraint in doc.ActualConstraints)
        {
            var columns = constraint.Columns is null || constraint.Columns.Length == 0
                ? ImmutableArray<RelationshipActualConstraintColumn>.Empty
                : constraint.Columns
                    .Select(c => RelationshipActualConstraintColumn.Create(
                        c.OwnerPhysical,
                        c.OwnerAttribute,
                        c.ReferencedPhysical,
                        c.ReferencedAttribute,
                        c.Ordinal))
                    .ToImmutableArray();

            constraints.Add(RelationshipActualConstraint.Create(
                constraint.Name ?? string.Empty,
                constraint.ReferencedSchema,
                constraint.ReferencedTable,
                constraint.OnDelete,
                constraint.OnUpdate,
                columns));
        }

        return constraints;
    }
}
