using System;
using System.Collections.Generic;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class ProfilingPlanBuilderTests
{
    [Fact]
    public void BuildPlans_ShapesPlanWithColumnsUniquesAndRowCounts()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_U_USER", "EMAIL")] = new ColumnMetadata(true, false, false, null)
        };
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER")] = 100
        };

        var lookup = EntityProfilingLookup.Create(model, NamingOverrideOptions.Empty);
        var builder = new ProfilingPlanBuilder(model, lookup);
        var plans = builder.BuildPlans(metadata, rowCounts);

        Assert.True(plans.TryGetValue(("dbo", "OSUSR_U_USER"), out var plan));
        Assert.Equal(100, plan.RowCount);
        Assert.Equal(new[] { "EMAIL", "ID" }, plan.Columns);
        Assert.Single(plan.UniqueCandidates);
        Assert.Equal("email", plan.UniqueCandidates[0].Key);
        Assert.Equal(new[] { "EMAIL" }, plan.UniqueCandidates[0].Columns);
        Assert.Empty(plan.ForeignKeys);
    }

    [Fact]
    public void BuildPlans_ShouldHonorEntityOverridesWhenSelectingForeignKeys()
    {
        var (model, namingOverrides) = CreateDuplicateEntityModel();
        var lookup = EntityProfilingLookup.Create(model, namingOverrides);
        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_SALES_ORDER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_SALES_ORDER", "CUSTOMER_ID")] = new ColumnMetadata(true, false, false, null)
        };
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_SALES_ORDER")] = 42
        };

        var builder = new ProfilingPlanBuilder(model, lookup);
        var plans = builder.BuildPlans(metadata, rowCounts);

        var plan = Assert.Single(plans.Values.Where(p => p.Table == "OSUSR_SALES_ORDER"));
        var foreignKey = Assert.Single(plan.ForeignKeys);
        Assert.Equal("dbo", foreignKey.TargetSchema);
        Assert.Equal("OSUSR_SUPPORT_CUSTOMER", foreignKey.TargetTable);
        Assert.Equal("ID", foreignKey.TargetColumn);
    }

    private static (OsmModel Model, NamingOverrideOptions NamingOverrides) CreateDuplicateEntityModel()
    {
        var salesIdentifier = CreateIdentifier();
        var supportIdentifier = CreateIdentifier();
        var orderIdentifier = CreateIdentifier();

        var salesCustomer = EntityModel.Create(
            new ModuleName("Sales"),
            new EntityName("Customer"),
            new TableName("OSUSR_SALES_CUSTOMER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                salesIdentifier,
                CreateNameAttribute()
            },
            allowMissingPrimaryKey: true).Value;

        var supportCustomer = EntityModel.Create(
            new ModuleName("Support"),
            new EntityName("Customer"),
            new TableName("OSUSR_SUPPORT_CUSTOMER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                supportIdentifier,
                CreateNameAttribute()
            },
            allowMissingPrimaryKey: true).Value;

        var referenceResult = AttributeReference.Create(
            true,
            targetEntityId: null,
            new EntityName("Customer"),
            new TableName("OSUSR_SALES_CUSTOMER"),
            deleteRuleCode: null,
            hasDatabaseConstraint: false).Value;

        var order = EntityModel.Create(
            new ModuleName("Sales"),
            new EntityName("Order"),
            new TableName("OSUSR_SALES_ORDER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                orderIdentifier,
                AttributeModel.Create(
                    new AttributeName("CustomerId"),
                    new ColumnName("CUSTOMER_ID"),
                    dataType: "INT",
                    isMandatory: false,
                    isIdentifier: false,
                    isAutoNumber: false,
                    isActive: true,
                    reference: referenceResult).Value
            },
            allowMissingPrimaryKey: true).Value;

        var salesModule = ModuleModel.Create(
            new ModuleName("Sales"),
            isSystemModule: false,
            isActive: true,
            new[] { salesCustomer, order }).Value;

        var supportModule = ModuleModel.Create(
            new ModuleName("Support"),
            isSystemModule: false,
            isActive: true,
            new[] { supportCustomer }).Value;

        var model = OsmModel.Create(DateTime.UtcNow, new[] { salesModule, supportModule }).Value;

        var overrideRule = NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_OVERRIDE").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        return (model, namingOverrides);
    }

    private static AttributeModel CreateIdentifier()
    {
        return AttributeModel.Create(
            new AttributeName("Id"),
            new ColumnName("ID"),
            dataType: "INT",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;
    }

    private static AttributeModel CreateNameAttribute()
    {
        return AttributeModel.Create(
            new AttributeName("Name"),
            new ColumnName("NAME"),
            dataType: "TEXT",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;
    }
}
