using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public static class OutSystemsInternalModel
{
    public static ImmutableArray<EntityModel> Entities { get; } = BuildEntities();

    public static EntityModel Users => Entities.First(e =>
        string.Equals(e.PhysicalName.Value, "OSUSR_U_USER", StringComparison.OrdinalIgnoreCase));

    private static ImmutableArray<EntityModel> BuildEntities()
    {
        var builder = ImmutableArray.CreateBuilder<EntityModel>();
        builder.Add(CreateUsersEntity());
        return builder.ToImmutable();
    }

    private static EntityModel CreateUsersEntity()
    {
        var module = ModuleName.Create("Users").Value;
        var entityName = EntityName.Create("User").Value;
        var tableName = TableName.Create("OSUSR_U_USER").Value;
        var schema = SchemaName.Create("dbo").Value;

        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;

        var usernameAttribute = AttributeModel.Create(
            AttributeName.Create("Username").Value,
            ColumnName.Create("USERNAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 50).Value;

        var emailAttribute = AttributeModel.Create(
            AttributeName.Create("Email").Value,
            ColumnName.Create("EMAIL").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 255).Value;

        var entityResult = EntityModel.Create(
            module,
            entityName,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, usernameAttribute, emailAttribute });

        if (entityResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to construct Users entity: {string.Join(", ", entityResult.Errors.Select(e => e.Code))}");
        }

        return entityResult.Value;
    }
}
