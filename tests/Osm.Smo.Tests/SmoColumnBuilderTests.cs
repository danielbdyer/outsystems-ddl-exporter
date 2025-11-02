using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
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
        var foreignKeyReality = SmoTestHelper.BuildForeignKeyReality(snapshot);
        var customerEmitter = new SmoEntityEmitter(
            customerContext,
            decisions,
            contexts,
            profileDefaults,
            foreignKeyReality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);
        var columns = SmoColumnBuilder.BuildColumns(customerEmitter);

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
        var cityEmitter = new SmoEntityEmitter(
            cityContext,
            decisions,
            contexts,
            profileDefaults,
            foreignKeyReality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);
        var cityColumns = SmoColumnBuilder.BuildColumns(cityEmitter);
        var isActive = cityColumns.Single(c => c.LogicalName.Equals("IsActive", StringComparison.Ordinal));
        Assert.Equal("(1)", isActive.DefaultExpression);
    }

    [Fact]
    public void BuildColumns_normalizes_boolean_word_defaults_to_bit_literals()
    {
        var moduleName = ModuleName.Create("TestModule").Value;
        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
        var trueAttribute = AttributeModel.Create(
            AttributeName.Create("IsEnabled").Value,
            ColumnName.Create("ISENABLED").Value,
            dataType: "Boolean",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None,
            defaultValue: "True",
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
        var falseAttribute = AttributeModel.Create(
            AttributeName.Create("IsDisabled").Value,
            ColumnName.Create("ISDISABLED").Value,
            dataType: "Boolean",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None,
            defaultValue: "False",
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;

        var entity = EntityModel.Create(
            moduleName,
            EntityName.Create("FlagHolder").Value,
            TableName.Create("FLAG_HOLDER").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { idAttribute, trueAttribute, falseAttribute },
            metadata: EntityMetadata.Empty).Value;
        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var context = contexts.GetContext(entity);
        var emitter = new SmoEntityEmitter(
            context,
            decisions,
            contexts,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyReality>.Empty,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);
        var columns = SmoColumnBuilder.BuildColumns(emitter);

        var enabled = columns.Single(c => c.LogicalName.Equals("IsEnabled", StringComparison.Ordinal));
        Assert.Equal("(1)", enabled.DefaultExpression);

        var disabled = columns.Single(c => c.LogicalName.Equals("IsDisabled", StringComparison.Ordinal));
        Assert.Equal("(0)", disabled.DefaultExpression);
    }
}
