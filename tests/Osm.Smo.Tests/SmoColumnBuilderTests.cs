using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Smo;
using Osm.Validation.Tightening;
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

    [Fact]
    public void BuildColumns_normalizes_boolean_word_default_to_bit_literal()
    {
        var (model, decisions, _) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var moduleIndex = Enumerable.Range(0, model.Modules.Length)
            .First(i => model.Modules[i].Entities.Any(entity =>
                entity.LogicalName.Value.Equals("City", StringComparison.Ordinal)));
        var module = model.Modules[moduleIndex];
        var entityIndex = Enumerable.Range(0, module.Entities.Length)
            .First(i => module.Entities[i].LogicalName.Value.Equals("City", StringComparison.Ordinal));
        var cityEntity = module.Entities[entityIndex];
        var attributeIndex = Enumerable.Range(0, cityEntity.Attributes.Length)
            .First(i => cityEntity.Attributes[i].LogicalName.Value.Equals("IsActive", StringComparison.Ordinal));
        var isActiveAttribute = cityEntity.Attributes[attributeIndex];

        var modifiedAttribute = isActiveAttribute with
        {
            DefaultValue = "True",
            OnDisk = isActiveAttribute.OnDisk with { DefaultDefinition = null, DefaultConstraint = null },
        };

        var modifiedAttributes = cityEntity.Attributes.SetItem(attributeIndex, modifiedAttribute);
        var modifiedCityEntity = cityEntity with { Attributes = modifiedAttributes };
        var modifiedEntities = module.Entities.SetItem(entityIndex, modifiedCityEntity);
        var modifiedModule = module with { Entities = modifiedEntities };
        var modifiedModules = model.Modules.SetItem(moduleIndex, modifiedModule);
        var modifiedModel = model with { Modules = modifiedModules };

        var contexts = SmoModelFactory.BuildEntityContexts(modifiedModel, supplementalEntities: null);
        var cityContext = contexts.GetContext(modifiedCityEntity);
        var profileDefaults = ImmutableDictionary<ColumnCoordinate, string>.Empty;
        var columns = SmoColumnBuilder.BuildColumns(
            cityContext,
            decisions,
            profileDefaults,
            TypeMappingPolicy.Default,
            contexts);

        var isActive = columns.Single(c => c.LogicalName.Equals("IsActive", StringComparison.Ordinal));
        Assert.Equal("(1)", isActive.DefaultExpression);
    }
}
