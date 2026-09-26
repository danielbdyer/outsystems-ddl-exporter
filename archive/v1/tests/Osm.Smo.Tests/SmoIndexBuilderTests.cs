using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Smo.Tests;

public class SmoIndexBuilderTests
{
    [Fact]
    public void BuildIndexes_generates_primary_and_unique_metadata()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);
        var profileDefaults = SmoTestHelper.BuildProfileDefaults(snapshot);
        var foreignKeyReality = SmoTestHelper.BuildForeignKeyReality(snapshot);
        var emitter = new SmoEntityEmitter(
            customerContext,
            decisions,
            contexts,
            profileDefaults,
            foreignKeyReality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);

        var indexes = SmoIndexBuilder.BuildIndexes(emitter);

        var primaryKey = indexes.Single(index => index.IsPrimaryKey);
        Assert.Equal("PK_Customer_Id", primaryKey.Name);
        Assert.True(primaryKey.IsUnique);
        Assert.All(primaryKey.Columns, column => Assert.False(column.IsIncluded));

        var uniqueIndex = indexes.Single(index => index.Name.Equals("UIX_Customer_Email", StringComparison.Ordinal));
        Assert.True(uniqueIndex.IsUnique);
        Assert.False(uniqueIndex.IsPrimaryKey);
        Assert.Equal(85, uniqueIndex.Metadata.FillFactor);
        Assert.True(uniqueIndex.Metadata.IgnoreDuplicateKey);
        Assert.Equal("[EMAIL] IS NOT NULL", uniqueIndex.Metadata.FilterDefinition);

        var nonUnique = indexes.Single(index => index.Name.Equals("IX_Customer_LastName_FirstName", StringComparison.Ordinal));
        Assert.False(nonUnique.IsUnique);
        Assert.True(nonUnique.Metadata.StatisticsNoRecompute);
        Assert.True(nonUnique.Metadata.AllowRowLocks);
    }

    [Fact]
    public void BuildIndexes_honors_platform_auto_toggle()
    {
        var moduleName = ModuleName.Create("Toggle").Value;
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
        var nameAttribute = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "Text",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;

        var platformColumn = IndexColumnModel.Create(
            nameAttribute.LogicalName,
            nameAttribute.ColumnName,
            ordinal: 1,
            isIncluded: false,
            IndexColumnDirection.Ascending).Value;
        var platformIndex = IndexModel.Create(
            IndexName.Create("IX_NAME").Value,
            isUnique: false,
            isPrimary: false,
            isPlatformAuto: true,
            new[] { platformColumn }).Value;

        var entity = EntityModel.Create(
            moduleName,
            EntityName.Create("ToggleEntity").Value,
            TableName.Create("TOGGLE_ENTITY").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { idAttribute, nameAttribute },
            indexes: new[] { platformIndex },
            relationships: Array.Empty<RelationshipModel>(),
            triggers: Array.Empty<TriggerModel>(),
            metadata: EntityMetadata.Empty).Value;

        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var context = contexts.GetContext(entity);
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var excludeEmitter = new SmoEntityEmitter(
            context,
            decisions,
            contexts,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyReality>.Empty,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);
        var excludedIndexes = SmoIndexBuilder.BuildIndexes(excludeEmitter);
        Assert.DoesNotContain(excludedIndexes, index => index.IsPlatformAuto);

        var includeEmitter = new SmoEntityEmitter(
            context,
            decisions,
            contexts,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyReality>.Empty,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: true);
        var includedIndexes = SmoIndexBuilder.BuildIndexes(includeEmitter);
        Assert.Contains(includedIndexes, index => index.IsPlatformAuto);
    }

    [Fact]
    public void BuildIndexes_orders_unique_indexes_before_non_unique()
    {
        var (model, decisions, snapshot) = SmoTestHelper.LoadEdgeCaseArtifacts();
        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var customerEntity = model.Modules
            .SelectMany(module => module.Entities)
            .First(entity => entity.LogicalName.Value.Equals("Customer", StringComparison.Ordinal));
        var customerContext = contexts.GetContext(customerEntity);
        var profileDefaults = SmoTestHelper.BuildProfileDefaults(snapshot);
        var foreignKeyReality = SmoTestHelper.BuildForeignKeyReality(snapshot);
        var emitter = new SmoEntityEmitter(
            customerContext,
            decisions,
            contexts,
            profileDefaults,
            foreignKeyReality,
            TypeMappingPolicy.Default,
            SmoFormatOptions.Default,
            includePlatformAutoIndexes: false);

        var indexes = SmoIndexBuilder.BuildIndexes(emitter);

        var uniquePositions = indexes
            .Select((index, position) => (index, position))
            .Where(tuple => tuple.index.IsUnique && !tuple.index.IsPrimaryKey)
            .ToImmutableArray();

        var nonUniquePositions = indexes
            .Select((index, position) => (index, position))
            .Where(tuple => !tuple.index.IsUnique && !tuple.index.IsPrimaryKey)
            .ToImmutableArray();

        Assert.NotEmpty(uniquePositions);
        Assert.NotEmpty(nonUniquePositions);

        var lastUniquePosition = uniquePositions.Max(tuple => tuple.position);
        var firstNonUniquePosition = nonUniquePositions.Min(tuple => tuple.position);

        Assert.True(lastUniquePosition < firstNonUniquePosition);
    }
}
