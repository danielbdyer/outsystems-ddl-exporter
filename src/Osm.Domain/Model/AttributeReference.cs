using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record AttributeReference(
    bool IsReference,
    int? TargetEntityId,
    EntityName? TargetEntity,
    TableName? TargetPhysicalName,
    string? DeleteRuleCode,
    bool HasDatabaseConstraint)
{
    public static readonly AttributeReference None = new(false, null, null, null, null, false);

    public static Result<AttributeReference> Create(
        bool isReference,
        int? targetEntityId,
        EntityName? targetEntity,
        TableName? targetPhysicalName,
        string? deleteRuleCode,
        bool? hasDatabaseConstraint)
    {
        if (!isReference)
        {
            return Result<AttributeReference>.Success(None);
        }

        if (targetEntity is null)
        {
            return Result<AttributeReference>.Failure(ValidationError.Create("attribute.reference.target.missing", "Referenced entity name must be provided when attribute is a reference."));
        }

        if (targetPhysicalName is null)
        {
            return Result<AttributeReference>.Failure(ValidationError.Create("attribute.reference.physical.missing", "Referenced entity physical name must be provided when attribute is a reference."));
        }

        var normalizedDeleteRule = string.IsNullOrWhiteSpace(deleteRuleCode) ? null : deleteRuleCode.Trim();
        var hasConstraint = hasDatabaseConstraint ?? false;

        return Result<AttributeReference>.Success(new AttributeReference(
            true,
            targetEntityId,
            targetEntity,
            targetPhysicalName,
            normalizedDeleteRule,
            hasConstraint));
    }
}
