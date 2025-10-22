using System;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class SmoColumnBuilderTests
{
    [Fact]
    public void BuildColumns_aligns_reference_types_and_defaults()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);
        var profileDefaults = SmoTestHelper.BuildProfileDefaults(snapshot);
        var columns = SmoColumnBuilder.BuildColumns(customerContext, decisions, profileDefaults, TypeMappingPolicy.Default, contexts);

        var idColumn = columns.Single(c => c.LogicalName.Equals("Id", StringComparison.Ordinal));
        Assert.False(idColumn.Nullable);
        Assert.True(idColumn.IsIdentity);
        var idAttribute = customerContext.EmittableAttributes.Single(a => a.LogicalName.Value.Equals("Id", StringComparison.Ordinal));
        Assert.Equal(idAttribute.ColumnName.Value, idColumn.PhysicalName);
        Assert.Equal(idAttribute.LogicalName.Value, idColumn.Name);

        var cityColumn = columns.Single(c => c.LogicalName.Equals("CityId", StringComparison.Ordinal));
        Assert.Equal(SqlDataType.BigInt, cityColumn.DataType.SqlDataType);
        Assert.False(cityColumn.Nullable);
        var cityAttribute = customerContext.EmittableAttributes.Single(a => a.LogicalName.Value.Equals("CityId", StringComparison.Ordinal));
        Assert.Equal(cityAttribute.ColumnName.Value, cityColumn.PhysicalName);
        Assert.Equal(cityAttribute.LogicalName.Value, cityColumn.Name);

        var firstNameColumn = columns.Single(c => c.LogicalName.Equals("FirstName", StringComparison.Ordinal));
        Assert.Equal("('')", firstNameColumn.DefaultExpression);

        var cityEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("City", StringComparison.Ordinal));
        var cityContext = contexts.GetContext(cityEntity);
        var cityColumns = SmoColumnBuilder.BuildColumns(cityContext, decisions, profileDefaults, TypeMappingPolicy.Default, contexts);
        var isActive = cityColumns.Single(c => c.LogicalName.Equals("IsActive", StringComparison.Ordinal));
        Assert.Equal("((1))", isActive.DefaultExpression);
    }
}
