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

        var entityMetadata = EntityMetadata.Create(
            "End-user of the applications. Shared between spaces with the same user provider (defined in Service Studio).",
            extendedProperties: null,
            temporal: null);

        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true,
            precision: 19,
            scale: 0,
            metadata: AttributeMetadata.Create("Unique identifier of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: false,
                sqlType: "bigint",
                maxLength: null,
                precision: 19,
                scale: 0,
                collation: null,
                isIdentity: true,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var usernameAttribute = AttributeModel.Create(
            AttributeName.Create("Username").Value,
            ColumnName.Create("USERNAME").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 250,
            metadata: AttributeMetadata.Create("Login name of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: false,
                sqlType: "nvarchar",
                maxLength: 250,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var emailAttribute = AttributeModel.Create(
            AttributeName.Create("EMail").Value,
            ColumnName.Create("EMAIL").Value,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 250,
            metadata: AttributeMetadata.Create("Email contact of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: false,
                sqlType: "nvarchar",
                maxLength: 250,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var nameAttribute = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 256,
            metadata: AttributeMetadata.Create("Full name of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "nvarchar",
                maxLength: 256,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var mobilePhoneAttribute = AttributeModel.Create(
            AttributeName.Create("MobilePhone").Value,
            ColumnName.Create("MOBILEPHONE").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 20,
            metadata: AttributeMetadata.Create("Mobile phone number of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "nvarchar",
                maxLength: 20,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var passwordAttribute = AttributeModel.Create(
            AttributeName.Create("Password").Value,
            ColumnName.Create("PASSWORD").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 256,
            metadata: AttributeMetadata.Create("Login password of the user."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "nvarchar",
                maxLength: 256,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var externalIdAttribute = AttributeModel.Create(
            AttributeName.Create("External_Id").Value,
            ColumnName.Create("EXTERNAL_ID").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            length: 36,
            metadata: AttributeMetadata.Create("The user identifier in an external system to the Platform."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "nvarchar",
                maxLength: 36,
                precision: null,
                scale: null,
                collation: "Latin1_General_CI_AI",
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var isActiveAttribute = AttributeModel.Create(
            AttributeName.Create("Is_Active").Value,
            ColumnName.Create("IS_ACTIVE").Value,
            dataType: "Boolean",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            metadata: AttributeMetadata.Create("Indicates if the user is still active."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "bit",
                maxLength: null,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var creationDateAttribute = AttributeModel.Create(
            AttributeName.Create("Creation_Date").Value,
            ColumnName.Create("CREATION_DATE").Value,
            dataType: "DateTime",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            metadata: AttributeMetadata.Create("The date the user was created."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "datetime",
                maxLength: null,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var lastLoginAttribute = AttributeModel.Create(
            AttributeName.Create("Last_Login").Value,
            ColumnName.Create("LAST_LOGIN").Value,
            dataType: "DateTime",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            metadata: AttributeMetadata.Create("Last time the user logged in the application."),
            onDisk: AttributeOnDiskMetadata.Create(
                isNullable: true,
                sqlType: "datetime",
                maxLength: null,
                precision: null,
                scale: null,
                collation: null,
                isIdentity: false,
                isComputed: false,
                computedDefinition: null,
                defaultDefinition: null)).Value;

        var entityResult = EntityModel.Create(
            module,
            entityName,
            tableName,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                idAttribute,
                usernameAttribute,
                emailAttribute,
                nameAttribute,
                mobilePhoneAttribute,
                passwordAttribute,
                externalIdAttribute,
                isActiveAttribute,
                creationDateAttribute,
                lastLoginAttribute
            },
            metadata: entityMetadata);

        if (entityResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to construct Users entity: {string.Join(", ", entityResult.Errors.Select(e => e.Code))}");
        }

        return entityResult.Value;
    }
}
