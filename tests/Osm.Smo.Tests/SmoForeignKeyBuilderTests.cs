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
        var profileDefaults = SmoTestHelper.BuildProfileDefaults(snapshot);
        var reality = SmoTestHelper.BuildForeignKeyReality(snapshot);

        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);

        var customerEmitter = new SmoEntityEmitter(
            customerContext,
            decisions,
            contexts,
            profileDefaults,
            reality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);

        var customerForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(customerEmitter);
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
        var jobRunEmitter = new SmoEntityEmitter(
            jobRunContext,
            decisions,
            contexts,
            profileDefaults,
            reality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);
        var jobRunForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(jobRunEmitter);
        Assert.Empty(jobRunForeignKeys);
    }

    [Fact]
    public void BuildForeignKeys_emits_fallback_when_no_evidence()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadDefaultDeleteRuleArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var profileDefaults = SmoTestHelper.BuildProfileDefaults(snapshot);
        var reality = SmoTestHelper.BuildForeignKeyReality(snapshot);

        var childEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Child", StringComparison.Ordinal));
        var childContext = contexts.GetContext(childEntity);

        var childEmitter = new SmoEntityEmitter(
            childContext,
            decisions,
            contexts,
            profileDefaults,
            reality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);

        var childForeignKeys = SmoForeignKeyBuilder.BuildForeignKeys(childEmitter);
        var fallbackForeignKey = Assert.Single(childForeignKeys);
        Assert.Equal("FK_Child_Parent_ParentId", fallbackForeignKey.Name);
        Assert.Collection(fallbackForeignKey.Columns, column => Assert.Equal("ParentId", column));
        Assert.Collection(fallbackForeignKey.ReferencedColumns, column => Assert.Equal("Id", column));
        Assert.Equal("Parent", fallbackForeignKey.ReferencedLogicalTable);
        Assert.False(fallbackForeignKey.IsNoCheck);
    }
}
