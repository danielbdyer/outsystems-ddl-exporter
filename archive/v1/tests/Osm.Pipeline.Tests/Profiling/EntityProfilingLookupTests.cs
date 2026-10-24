using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class EntityProfilingLookupTests
{
    [Fact]
    public void Create_ShouldSelectDeterministicCanonicalEntity_WhenDuplicatesPresent()
    {
        var salesEntity = CreateCustomerEntity("Sales", "OSUSR_S_CUSTOMER");
        var supportEntity = CreateCustomerEntity("Support", "OSUSR_T_CUSTOMER");
        var model = CreateModel(CreateModule(salesEntity), CreateModule(supportEntity));

        var lookup = EntityProfilingLookup.Create(model, NamingOverrideOptions.Empty);

        var logicalName = salesEntity.LogicalName;
        Assert.True(lookup.TryGet(logicalName, out var entry));
        Assert.Same(salesEntity, entry.Entity);
        Assert.NotSame(supportEntity, entry.Entity);
        Assert.Same(GetIdentifier(salesEntity), entry.PreferredIdentifier);
    }

    [Fact]
    public void Create_ShouldHonorModuleScopedOverrides_WhenDuplicatesPresent()
    {
        var salesEntity = CreateCustomerEntity("Sales", "OSUSR_S_CUSTOMER");
        var supportEntity = CreateCustomerEntity("Support", "OSUSR_T_CUSTOMER");
        var model = CreateModel(CreateModule(salesEntity), CreateModule(supportEntity));

        var overrideRule = NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_SUPPORT").Value;
        var overrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        var lookup = EntityProfilingLookup.Create(model, overrides);

        var logicalName = salesEntity.LogicalName;
        Assert.True(lookup.TryGet(logicalName, out var entry));
        Assert.Same(supportEntity, entry.Entity);
        Assert.Same(GetIdentifier(supportEntity), entry.PreferredIdentifier);
    }

    [Fact]
    public void BuildPlans_ShouldUseOverridesWhenSelectingForeignKeyTargets()
    {
        var salesEntity = CreateCustomerEntity("Sales", "OSUSR_S_CUSTOMER");
        var supportEntity = CreateCustomerEntity("Support", "OSUSR_T_CUSTOMER");
        var orderEntity = CreateOrderEntity(salesEntity);
        var model = CreateModel(CreateModule(salesEntity), CreateModule(supportEntity), CreateModule(orderEntity));

        var overrideRule = NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_SUPPORT").Value;
        var overrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [(orderEntity.Schema.Value, orderEntity.PhysicalName.Value, "ID")] = new ColumnMetadata(false, false, true, null),
            [(orderEntity.Schema.Value, orderEntity.PhysicalName.Value, "CUSTOMERID")] = new ColumnMetadata(true, false, false, null)
        };

        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [(orderEntity.Schema.Value, orderEntity.PhysicalName.Value)] = 0
        };

        var builder = new ProfilingPlanBuilder(model, overrides);
        var plans = builder.BuildPlans(metadata, rowCounts);

        Assert.True(plans.TryGetValue((orderEntity.Schema.Value, orderEntity.PhysicalName.Value), out var plan));
        var foreignKey = Assert.Single(plan.ForeignKeys);

        Assert.Equal(supportEntity.Schema.Value, foreignKey.TargetSchema);
        Assert.Equal(supportEntity.PhysicalName.Value, foreignKey.TargetTable);
        Assert.Equal(GetIdentifier(supportEntity).ColumnName.Value, foreignKey.TargetColumn);
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

    private static EntityModel CreateOrderEntity(EntityModel customerEntity)
    {
        var module = ModuleName.Create("Orders").Value;
        var entityName = EntityName.Create("Order").Value;
        var table = TableName.Create("OSUSR_O_ORDER").Value;

        var idAttribute = CreateIdentifierAttribute("Id", "ID");
        var reference = AttributeReference.Create(
            true,
            targetEntityId: null,
            customerEntity.LogicalName,
            customerEntity.PhysicalName,
            deleteRuleCode: null,
            hasDatabaseConstraint: true).Value;

        var customerAttribute = AttributeModel.Create(
            AttributeName.Create("CustomerId").Value,
            ColumnName.Create("CUSTOMERID").Value,
            "INT",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: reference).Value;

        return EntityModel.Create(
            module,
            entityName,
            table,
            customerEntity.Schema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, customerAttribute }).Value;
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

    private static ModuleModel CreateModule(EntityModel entity)
    {
        return ModuleModel.Create(entity.Module, isSystemModule: false, isActive: true, new[] { entity }).Value;
    }

    private static OsmModel CreateModel(params ModuleModel[] modules)
    {
        return OsmModel.Create(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), modules).Value;
    }

    private static AttributeModel GetIdentifier(EntityModel entity)
    {
        return entity.Attributes.First(attribute => attribute.IsIdentifier);
    }
}
