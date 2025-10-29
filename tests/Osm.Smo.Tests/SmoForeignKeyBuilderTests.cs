using System;
using System.Linq;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class SmoForeignKeyBuilderTests
{
    [Fact]
    public void BuildForeignKeys_respects_policy_and_reality()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var reality = SmoTestHelper.BuildForeignKeyReality(snapshot);

        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);

        var customerForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(customerContext, decisions, contexts, reality, SmoFormatOptions.Default);
        var cityForeignKey = Assert.Single(customerForeignKeys);
        Assert.Equal("FK_Customer_City_CityId", cityForeignKey.Name);
        Assert.False(cityForeignKey.IsNoCheck);
        Assert.Equal("City", cityForeignKey.ReferencedLogicalTable);
        Assert.Collection(cityForeignKey.Columns, column => Assert.Equal("CityId", column));
        Assert.Collection(cityForeignKey.ReferencedColumns, column => Assert.Equal("Id", column));

        var jobRunEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("JobRun", StringComparison.Ordinal));
        var jobRunContext = contexts.GetContext(jobRunEntity);
        var jobRunForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(jobRunContext, decisions, contexts, reality, SmoFormatOptions.Default);
        Assert.Empty(jobRunForeignKeys);
    }

    [Fact]
    public void BuildForeignKeys_emits_fallback_when_no_evidence()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadDefaultDeleteRuleArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var reality = SmoTestHelper.BuildForeignKeyReality(snapshot);

        var childEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Child", StringComparison.Ordinal));
        var childContext = contexts.GetContext(childEntity);

        var childForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(childContext, decisions, contexts, reality, SmoFormatOptions.Default);
        var fallbackForeignKey = Assert.Single(childForeignKeys);
        Assert.Equal("FK_Child_Parent_ParentId", fallbackForeignKey.Name);
        Assert.Collection(fallbackForeignKey.Columns, column => Assert.Equal("ParentId", column));
        Assert.Collection(fallbackForeignKey.ReferencedColumns, column => Assert.Equal("Id", column));
        Assert.Equal("Parent", fallbackForeignKey.ReferencedLogicalTable);
        Assert.False(fallbackForeignKey.IsNoCheck);
    }
}
