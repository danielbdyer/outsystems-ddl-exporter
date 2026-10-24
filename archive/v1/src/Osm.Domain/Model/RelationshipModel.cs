using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record RelationshipModel(
    AttributeName ViaAttribute,
    EntityName TargetEntity,
    TableName TargetPhysicalName,
    string DeleteRuleCode,
    bool HasDatabaseConstraint,
    ImmutableArray<RelationshipActualConstraint> ActualConstraints)
{
    public static Result<RelationshipModel> Create(
        AttributeName viaAttribute,
        EntityName targetEntity,
        TableName targetPhysicalName,
        string? deleteRuleCode,
        bool? hasDatabaseConstraint,
        IEnumerable<RelationshipActualConstraint>? actualConstraints = null)
    {
        var normalizedDeleteRule = string.IsNullOrWhiteSpace(deleteRuleCode)
            ? "Ignore"
            : deleteRuleCode!.Trim();

        var constraint = hasDatabaseConstraint ?? false;
        var realizedConstraints = (actualConstraints ?? Enumerable.Empty<RelationshipActualConstraint>()).ToImmutableArray();

        return Result<RelationshipModel>.Success(new RelationshipModel(
            viaAttribute,
            targetEntity,
            targetPhysicalName,
            normalizedDeleteRule,
            constraint,
            realizedConstraints));
    }
}
