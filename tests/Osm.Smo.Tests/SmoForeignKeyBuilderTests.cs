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
        Assert.Equal("FK_Customer_CityId", cityForeignKey.Name);
        Assert.False(cityForeignKey.IsNoCheck);
        Assert.Equal("City", cityForeignKey.ReferencedLogicalTable);

        var jobRunEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("JobRun", StringComparison.Ordinal));
        var jobRunContext = contexts.GetContext(jobRunEntity);
        var jobRunForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(jobRunContext, decisions, contexts, reality, SmoFormatOptions.Default);
        Assert.Empty(jobRunForeignKeys);
    }
}
