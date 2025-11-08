using System;
using System.Collections.Generic;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tests.Policy;

internal static class TighteningEvaluatorTestHelper
{
    public static AttributeModel CreateAttribute(
        string logicalName,
        string columnName,
        string dataType = "INT",
        bool isMandatory = false,
        bool isIdentifier = false,
        bool isReference = false,
        string? deleteRule = null,
        bool hasDatabaseConstraint = false,
        EntityName? targetEntity = null,
        TableName? targetPhysical = null,
        string? defaultValue = null)
    {
        var reference = isReference
            ? AttributeReference.Create(
                true,
                targetEntityId: null,
                targetEntity ?? new EntityName("Target"),
                targetPhysical ?? new TableName("OSUSR_TARGET"),
                deleteRule,
                hasDatabaseConstraint).Value
            : AttributeReference.None;

        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType,
            isMandatory,
            isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reference,
            defaultValue: defaultValue).Value;
    }

    public static EntityModel CreateEntity(
        string moduleName,
        string logicalName,
        string tableName,
        IEnumerable<AttributeModel> attributes,
        SchemaName? schema = null,
        string? catalog = null,
        IEnumerable<IndexModel>? indexes = null)
    {
        var module = new ModuleName(moduleName);
        var entity = EntityModel.Create(
            module,
            new EntityName(logicalName),
            new TableName(tableName),
            schema ?? new SchemaName("dbo"),
            catalog,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes,
            indexes,
            allowMissingPrimaryKey: true);

        if (entity.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", entity.Errors));
        }

        return entity.Value;
    }

    public static ModuleModel CreateModule(string name, params EntityModel[] entities)
    {
        var module = ModuleModel.Create(new ModuleName(name), isSystemModule: false, isActive: true, entities);
        if (module.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", module.Errors));
        }

        return module.Value;
    }

    public static OsmModel CreateModel(params ModuleModel[] modules)
    {
        var model = OsmModel.Create(DateTime.UtcNow, modules);
        if (model.IsFailure)
        {
            throw new InvalidOperationException(string.Join(",", model.Errors));
        }

        return model.Value;
    }

    public static ForeignKeyTargetIndex CreateForeignKeyTargetIndex(
        OsmModel model,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        var attributeIndex = EntityAttributeIndex.Create(model);
        return ForeignKeyTargetIndex.Create(attributeIndex, entityLookup);
    }

    public static ColumnProfile CreateColumnProfile(
        ColumnCoordinate coordinate,
        bool isNullablePhysical,
        long rowCount,
        long nullCount)
        => ColumnProfile.Create(
            coordinate.Schema,
            coordinate.Table,
            coordinate.Column,
            isNullablePhysical,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount,
            nullCount,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, rowCount)).Value;

    public static UniqueCandidateProfile CreateUniqueCandidate(ColumnCoordinate coordinate, bool hasDuplicate)
        => UniqueCandidateProfile.Create(
            coordinate.Schema,
            coordinate.Table,
            coordinate.Column,
            hasDuplicate,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 0)).Value;

    public static ForeignKeyReality CreateForeignKeyReality(
        ColumnCoordinate from,
        ColumnCoordinate to,
        bool hasConstraint,
        bool hasOrphan)
    {
        var reference = ForeignKeyReference.Create(
            from.Schema,
            from.Table,
            from.Column,
            to.Schema,
            to.Table,
            to.Column,
            hasConstraint).Value;

        return ForeignKeyReality.Create(
            reference,
            hasOrphan,
            hasOrphan ? 1 : 0,
            isNoCheck: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 0),
            orphanSample: null).Value;
    }
}
