using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record RelationshipModel(
    AttributeName ViaAttribute,
    EntityName TargetEntity,
    TableName TargetPhysicalName,
    string DeleteRuleCode,
    bool HasDatabaseConstraint)
{
    public static Result<RelationshipModel> Create(
        AttributeName viaAttribute,
        EntityName targetEntity,
        TableName targetPhysicalName,
        string? deleteRuleCode,
        bool? hasDatabaseConstraint)
    {
        var normalizedDeleteRule = string.IsNullOrWhiteSpace(deleteRuleCode)
            ? "Ignore"
            : deleteRuleCode!.Trim();

        var constraint = hasDatabaseConstraint ?? false;

        return Result<RelationshipModel>.Success(new RelationshipModel(
            viaAttribute,
            targetEntity,
            targetPhysicalName,
            normalizedDeleteRule,
            constraint));
    }
}
