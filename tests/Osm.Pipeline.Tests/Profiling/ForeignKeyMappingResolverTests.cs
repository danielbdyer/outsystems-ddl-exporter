using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class ForeignKeyMappingResolverTests
{
    [Fact]
    public void Resolve_ShouldRespectActualConstraintWhenPhysicalNameDiffers()
    {
        var alphaCustomer = CreateCustomerEntity("Alpha", "OSUSR_AA_CUSTOMER");
        var betaCustomer = CreateCustomerEntity("Beta", "OSUSR_BB_CUSTOMER");
        var actualConstraint = RelationshipActualConstraint.Create(
            "FK_Order_Customer",
            betaCustomer.Schema.Value,
            betaCustomer.PhysicalName.Value,
            onDeleteAction: null,
            onUpdateAction: null,
            new[]
            {
                RelationshipActualConstraintColumn.Create(
                    ownerColumn: "CUSTOMERID",
                    ownerAttribute: "CustomerId",
                    referencedColumn: betaCustomer.Attributes.First(a => a.IsIdentifier).ColumnName.Value,
                    referencedAttribute: betaCustomer.Attributes.First(a => a.IsIdentifier).LogicalName.Value,
                    ordinal: 0)
            });

        var order = CreateOrderEntity(
            moduleName: "Orders",
            physicalName: "OSUSR_O_ORDER",
            targetEntity: alphaCustomer,
            referencePhysicalName: alphaCustomer.PhysicalName.Value,
            hasDatabaseConstraint: true,
            actualConstraints: new[] { actualConstraint });

        var model = CreateModel(
            CreateModule(alphaCustomer),
            CreateModule(betaCustomer),
            CreateModule(order));
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var resolution = resolver.Resolve(GetModule(model, order.Module.Value), order, order.Attributes.First(a => !a.IsIdentifier));

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(betaCustomer, resolution.TargetEntity);
        Assert.Equal(betaCustomer.Attributes.First(a => a.IsIdentifier), resolution.TargetAttribute);
        Assert.True(resolution.HasDatabaseConstraint);
    }

    [Fact]
    public void Resolve_ShouldReturnAmbiguousWhenDuplicatesWithoutConstraint()
    {
        var alphaCustomer = CreateCustomerEntity("Alpha", "OSUSR_AA_CUSTOMER");
        var betaCustomer = CreateCustomerEntity("Beta", "OSUSR_BB_CUSTOMER");
        var order = CreateOrderEntity(
            moduleName: "Orders",
            physicalName: "OSUSR_O_ORDER",
            targetEntity: alphaCustomer,
            referencePhysicalName: "OSUSR_UNKNOWN",
            hasDatabaseConstraint: false);

        var model = CreateModel(
            CreateModule(alphaCustomer),
            CreateModule(betaCustomer),
            CreateModule(order));
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var resolution = resolver.Resolve(GetModule(model, order.Module.Value), order, order.Attributes.First(a => !a.IsIdentifier));

        Assert.Equal(ForeignKeyResolutionKind.Ambiguous, resolution.Kind);
        Assert.False(resolution.HasDatabaseConstraint);
        Assert.Contains(alphaCustomer, resolution.Candidates);
        Assert.Contains(betaCustomer, resolution.Candidates);
    }

    [Fact]
    public void Resolve_ShouldPreferSameModuleWhenDuplicatesExist()
    {
        var alphaCustomer = CreateCustomerEntity("Alpha", "OSUSR_AA_CUSTOMER");
        var betaCustomer = CreateCustomerEntity("Beta", "OSUSR_BB_CUSTOMER");
        var order = CreateOrderEntity(
            moduleName: "Alpha",
            physicalName: "OSUSR_AA_ORDER",
            targetEntity: alphaCustomer,
            referencePhysicalName: "OSUSR_UNKNOWN",
            hasDatabaseConstraint: false);

        var alphaModule = CreateModule(alphaCustomer, order);
        var betaModule = CreateModule(betaCustomer);
        var model = CreateModel(alphaModule, betaModule);
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var resolution = resolver.Resolve(alphaModule, order, order.Attributes.First(a => !a.IsIdentifier));

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(alphaCustomer, resolution.TargetEntity);
    }

    [Fact]
    public void Resolve_ShouldHonorModuleScopedOverrides()
    {
        var salesCustomer = CreateCustomerEntity("Sales", "OSUSR_S_CUSTOMER");
        var supportCustomer = CreateCustomerEntity("Support", "OSUSR_T_CUSTOMER");
        var order = CreateOrderEntity(
            moduleName: "Orders",
            physicalName: "OSUSR_O_ORDER",
            targetEntity: salesCustomer,
            referencePhysicalName: salesCustomer.PhysicalName.Value,
            hasDatabaseConstraint: true);

        var overrideRule = NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_SUPPORT").Value;
        var overrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        var model = CreateModel(
            CreateModule(salesCustomer),
            CreateModule(supportCustomer),
            CreateModule(order));
        var resolver = new ForeignKeyMappingResolver(model, overrides);

        var resolution = resolver.Resolve(GetModule(model, order.Module.Value), order, order.Attributes.First(a => !a.IsIdentifier));

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(supportCustomer, resolution.TargetEntity);
    }

    [Fact]
    public void Resolve_ShouldPreferCrossModulePrefixWhenExplicit()
    {
        var alphaCustomer = CreateCustomerEntity("Alpha", "OSUSR_AA_CUSTOMER");
        var betaCustomer = CreateCustomerEntity("Beta", "OSUSR_BB_CUSTOMER");
        var order = CreateOrderEntity(
            moduleName: "Alpha",
            physicalName: "OSUSR_AA_ORDER",
            targetEntity: alphaCustomer,
            referencePhysicalName: "OSUSR_BB_UNKNOWN",
            hasDatabaseConstraint: false);

        var alphaModule = CreateModule(alphaCustomer, order);
        var betaModule = CreateModule(betaCustomer);
        var model = CreateModel(alphaModule, betaModule);
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var resolution = resolver.Resolve(alphaModule, order, order.Attributes.First(a => !a.IsIdentifier));

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(betaCustomer, resolution.TargetEntity);
    }

    [Fact]
    public void Resolve_ShouldUseRelationshipPhysicalNameWhenAttributeNameDiffersByCase()
    {
        var localCategory = CreateEntity("CatalogBo3", "Category", "OSUSR_BO3_CATEGORY1");
        var remoteCategory = CreateEntity("CatalogRtj", "Category", "OSUSR_RTJ_CATEGORY");
        var referencing = CreateOrderEntity(
            moduleName: "CatalogBo3",
            physicalName: "OSUSR_BO3_ORDER",
            targetEntity: localCategory,
            referencePhysicalName: remoteCategory.PhysicalName.Value,
            hasDatabaseConstraint: false,
            actualConstraints: null,
            attributeLogicalName: "PARENTCATEGORYID",
            relationshipViaAttributeName: "ParentCategoryId",
            columnName: "PARENTCATEGORYID",
            attributeReferencePhysicalName: remoteCategory.PhysicalName.Value,
            relationshipPhysicalName: localCategory.PhysicalName.Value);

        var model = CreateModel(
            CreateModule(localCategory, referencing),
            CreateModule(remoteCategory));
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var referenceAttribute = referencing.Attributes.First(attribute => !attribute.IsIdentifier);
        var resolution = resolver.Resolve(GetModule(model, referencing.Module.Value), referencing, referenceAttribute);

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(localCategory, resolution.TargetEntity);
    }

    private static EntityModel CreateCustomerEntity(string moduleName, string physicalName)
    {
        return CreateEntity(moduleName, "Customer", physicalName);
    }

    private static EntityModel CreateOrderEntity(
        string moduleName,
        string physicalName,
        EntityModel targetEntity,
        string referencePhysicalName,
        bool hasDatabaseConstraint,
        IEnumerable<RelationshipActualConstraint>? actualConstraints = null,
        string attributeLogicalName = "CustomerId",
        string relationshipViaAttributeName = "CustomerId",
        string columnName = "CUSTOMERID",
        string? attributeReferencePhysicalName = null,
        string? relationshipPhysicalName = null)
    {
        var module = ModuleName.Create(moduleName).Value;
        var entityName = EntityName.Create("Order").Value;
        var table = TableName.Create(physicalName).Value;
        var schema = SchemaName.Create("dbo").Value;

        var idAttribute = CreateIdentifierAttribute("Id", "ID");
        var referenceAttribute = AttributeModel.Create(
            AttributeName.Create(attributeLogicalName).Value,
            ColumnName.Create(columnName).Value,
            "INT",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.Create(
                true,
                targetEntityId: null,
                targetEntity.LogicalName,
                TableName.Create(attributeReferencePhysicalName ?? referencePhysicalName).Value,
                deleteRuleCode: null,
                hasDatabaseConstraint: hasDatabaseConstraint).Value).Value;

        var relationship = RelationshipModel.Create(
            AttributeName.Create(relationshipViaAttributeName).Value,
            targetEntity.LogicalName,
            TableName.Create(relationshipPhysicalName ?? referencePhysicalName).Value,
            deleteRuleCode: null,
            hasDatabaseConstraint: hasDatabaseConstraint,
            actualConstraints).Value;

        return EntityModel.Create(
            module,
            entityName,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, referenceAttribute },
            relationships: new[] { relationship }).Value;
    }

    private static EntityModel CreateEntity(string moduleName, string logicalName, string physicalName)
    {
        var module = ModuleName.Create(moduleName).Value;
        var entityName = EntityName.Create(logicalName).Value;
        var table = TableName.Create(physicalName).Value;
        var schema = SchemaName.Create("dbo").Value;
        var idAttribute = CreateIdentifierAttribute("Id", "ID");
        var nameAttribute = CreateAttribute("Name", "NAME");

        return EntityModel.Create(
            module,
            entityName,
            table,
            schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, nameAttribute }).Value;
    }

    private static AttributeModel CreateIdentifierAttribute(string logicalName, string columnName)
    {
        return AttributeModel.Create(
            AttributeName.Create(logicalName).Value,
            ColumnName.Create(columnName).Value,
            "INT",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName)
    {
        return AttributeModel.Create(
            AttributeName.Create(logicalName).Value,
            ColumnName.Create(columnName).Value,
            "NVARCHAR",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;
    }

    private static ModuleModel CreateModule(params EntityModel[] entities)
    {
        return ModuleModel.Create(entities[0].Module, isSystemModule: false, isActive: true, entities).Value;
    }

    private static OsmModel CreateModel(params ModuleModel[] modules)
    {
        return OsmModel.Create(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), modules).Value;
    }

    private static ModuleModel GetModule(OsmModel model, string moduleName)
    {
        return model.Modules.First(module => string.Equals(module.Name.Value, moduleName, StringComparison.OrdinalIgnoreCase));
    }
}
