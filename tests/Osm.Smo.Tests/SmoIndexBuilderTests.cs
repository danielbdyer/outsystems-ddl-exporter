using System;
using System.Linq;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class SmoIndexBuilderTests
{
    [Fact]
    public void BuildIndexes_generates_primary_and_unique_metadata()
    {
        var (model, decisions, _) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);

        var indexes = SmoIndexBuilder.BuildIndexes(customerContext, decisions, includePlatformAuto: false, SmoFormatOptions.Default);

        var primaryKey = indexes.Single(index => index.IsPrimaryKey);
        Assert.Equal("PK_Customer_Id", primaryKey.Name);
        Assert.True(primaryKey.IsUnique);
        Assert.All(primaryKey.Columns, column => Assert.False(column.IsIncluded));

        var uniqueIndex = indexes.Single(index => index.Name.Equals("IDX_Customer_Email", StringComparison.Ordinal));
        Assert.True(uniqueIndex.IsUnique);
        Assert.False(uniqueIndex.IsPrimaryKey);
        Assert.Equal(85, uniqueIndex.Metadata.FillFactor);
        Assert.True(uniqueIndex.Metadata.IgnoreDuplicateKey);
        Assert.Equal("[EMAIL] IS NOT NULL", uniqueIndex.Metadata.FilterDefinition);

        var nonUnique = indexes.Single(index => index.Name.Equals("IDX_Customer_Name", StringComparison.Ordinal));
        Assert.False(nonUnique.IsUnique);
        Assert.True(nonUnique.Metadata.StatisticsNoRecompute);
        Assert.True(nonUnique.Metadata.AllowRowLocks);
    }
}
