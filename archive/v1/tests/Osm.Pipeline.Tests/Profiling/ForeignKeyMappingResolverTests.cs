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
    public void Resolve_ShouldHonorRelationshipPhysicalNameAndCaseInsensitiveAttributeMatch()
    {
        var expectedCategory = EntityModel.Create(
            ModuleName.Create("Catalog").Value,
            EntityName.Create("Category").Value,
            TableName.Create("OSUSR_bo3_Category1").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                CreateIdentifierAttribute("Id", "ID"),
                CreateAttribute("Name", "NAME")
            }).Value;

        var legacyCategory = EntityModel.Create(
            ModuleName.Create("Legacy").Value,
            EntityName.Create("Category").Value,
            TableName.Create("OSUSR_rtj_Category").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                CreateIdentifierAttribute("Id", "ID"),
                CreateAttribute("Name", "NAME")
            }).Value;

        var relationship = RelationshipModel.Create(
            AttributeName.Create("ParentCategoryId").Value,
            expectedCategory.LogicalName,
            TableName.Create(expectedCategory.PhysicalName.Value).Value,
            deleteRuleCode: "Ignore",
            hasDatabaseConstraint: false).Value;

        var categoryHierarchy = EntityModel.Create(
            expectedCategory.Module,
            EntityName.Create("CategoryHierarchy").Value,
            TableName.Create("OSUSR_CAT_HIERARCHY").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                CreateIdentifierAttribute("Id", "ID"),
                AttributeModel.Create(
                    AttributeName.Create("PARENTCATEGORYID").Value,
                    ColumnName.Create("PARENTCATEGORYID").Value,
                    "INT",
                    isMandatory: false,
                    isIdentifier: false,
                    isAutoNumber: false,
                    isActive: true,
                    reference: AttributeReference.Create(
                        true,
                        targetEntityId: null,
                        expectedCategory.LogicalName,
                        TableName.Create(legacyCategory.PhysicalName.Value).Value,
                        deleteRuleCode: null,
                        hasDatabaseConstraint: false).Value).Value
            },
            relationships: new[] { relationship }).Value;

        var catalogModule = CreateModule(expectedCategory, categoryHierarchy);
        var legacyModule = CreateModule(legacyCategory);
        var model = CreateModel(catalogModule, legacyModule);
        var resolver = new ForeignKeyMappingResolver(model, NamingOverrideOptions.Empty);

        var parentAttribute = categoryHierarchy.Attributes.First(attribute => !attribute.IsIdentifier);
        var resolution = resolver.Resolve(catalogModule, categoryHierarchy, parentAttribute);

        Assert.Equal(ForeignKeyResolutionKind.Resolved, resolution.Kind);
        Assert.Same(expectedCategory, resolution.TargetEntity);
        Assert.Equal(
            expectedCategory.Attributes.First(attribute => attribute.IsIdentifier),
            resolution.TargetAttribute);
    }

    private static EntityModel CreateCustomerEntity(string moduleName, string physicalName)
    {
        var module = ModuleName.Create(moduleName).Value;
        var entityName = EntityName.Create("Customer").Value;
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

    private static EntityModel CreateOrderEntity(
        string moduleName,
        string physicalName,
        EntityModel targetEntity,
        string referencePhysicalName,
        bool hasDatabaseConstraint,
        IEnumerable<RelationshipActualConstraint>? actualConstraints = null)
    {
        var module = ModuleName.Create(moduleName).Value;
        var entityName = EntityName.Create("Order").Value;
        var table = TableName.Create(physicalName).Value;
        var schema = SchemaName.Create("dbo").Value;

        var idAttribute = CreateIdentifierAttribute("Id", "ID");
        var referenceAttribute = AttributeModel.Create(
            AttributeName.Create("CustomerId").Value,
            ColumnName.Create("CUSTOMERID").Value,
            "INT",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.Create(
                true,
                targetEntityId: null,
                targetEntity.LogicalName,
                TableName.Create(referencePhysicalName).Value,
                deleteRuleCode: null,
                hasDatabaseConstraint: hasDatabaseConstraint).Value).Value;

        var relationship = RelationshipModel.Create(
            AttributeName.Create("CustomerId").Value,
            targetEntity.LogicalName,
            TableName.Create(referencePhysicalName).Value,
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
