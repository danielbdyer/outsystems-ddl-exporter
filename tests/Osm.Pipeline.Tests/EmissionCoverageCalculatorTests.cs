using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Orchestration;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class EmissionCoverageCalculatorTests
{
    [Fact]
    public void Compute_ReportsUnsupportedIndexes()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var moduleIndex = Array.FindIndex(
            model.Modules.ToArray(),
            module => module.Entities.Any(entity => string.Equals(
                entity.LogicalName.Value,
                "Customer",
                StringComparison.OrdinalIgnoreCase)));
        Assert.True(moduleIndex >= 0, "Expected customer module in fixture model.");

        var module = model.Modules[moduleIndex];
        var entityIndex = Array.FindIndex(
            module.Entities.ToArray(),
            entity => string.Equals(entity.LogicalName.Value, "Customer", StringComparison.OrdinalIgnoreCase));
        Assert.True(entityIndex >= 0, "Expected customer entity in fixture model.");

        var entity = module.Entities[entityIndex];
        var attributeIndex = Array.FindIndex(
            entity.Attributes.ToArray(),
            attribute => string.Equals(attribute.LogicalName.Value, "LastName", StringComparison.OrdinalIgnoreCase));
        Assert.True(attributeIndex >= 0, "Expected Name attribute in customer entity.");

        var updatedAttribute = entity.Attributes[attributeIndex] with { IsActive = false };
        var updatedAttributes = entity.Attributes.SetItem(attributeIndex, updatedAttribute);
        var updatedEntity = entity with { Attributes = updatedAttributes };
        var updatedEntities = module.Entities.SetItem(entityIndex, updatedEntity);
        var updatedModule = module with { Entities = updatedEntities };
        var updatedModules = model.Modules.SetItem(moduleIndex, updatedModule);
        var updatedModel = model with { Modules = updatedModules };

        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(updatedModel, snapshot, TighteningOptions.Default);
        var smoOptions = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission);
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(updatedModel, decisions, snapshot, smoOptions);

        var result = EmissionCoverageCalculator.Compute(
            updatedModel,
            ImmutableArray<EntityModel>.Empty,
            decisions,
            smoModel,
            smoOptions);

        Assert.True(result.Summary.Constraints.Total > 0);
        Assert.True(result.Summary.Constraints.Emitted < result.Summary.Constraints.Total);
        Assert.Contains(
            result.Unsupported,
            message => message.Contains("IDX_CUSTOMER_NAME", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compute_UsesSnapshotColumnCounts()
    {
        var identifier = CreateAttribute("Id", "ID", isIdentifier: true);
        var inactive = CreateAttribute("IsDeleted", "ISDELETED", isIdentifier: false, isActive: false);
        var presentButInactive = CreateAttribute("LegacyId", "LEGACYID", isIdentifier: true, presentButInactive: true);
        var name = CreateAttribute("Name", "NAME");

        var entity = CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "OSUSR_SALES_CUSTOMER",
            schema: "sales",
            identifier,
            inactive,
            presentButInactive,
            name);

        var module = CreateModule("Sales", entity);
        var model = CreateModel(module);
        var snapshot = EntityEmissionSnapshot.Create("Sales", entity);

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var result = EmissionCoverageCalculator.Compute(
            model,
            ImmutableArray<EntityModel>.Empty,
            decisions,
            SmoModel.Create(ImmutableArray<SmoTableDefinition>.Empty),
            SmoBuildOptions.Default);

        Assert.Equal(snapshot.EmittableAttributes.Length, result.Summary.Columns.Total);
        Assert.Equal(snapshot.EmittableAttributes.Length, result.Summary.Columns.Emitted);
    }

    private static AttributeModel CreateAttribute(
        string logicalName,
        string columnName,
        bool isIdentifier = false,
        bool isActive = true,
        bool presentButInactive = false)
    {
        var reality = new AttributeReality(null, null, null, null, presentButInactive);
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: isActive,
            reality: reality,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }

    private static EntityModel CreateEntity(
        string moduleName,
        string logicalName,
        string physicalName,
        string schema,
        params AttributeModel[] attributes)
    {
        return EntityModel.Create(
            new ModuleName(moduleName),
            new EntityName(logicalName),
            new TableName(physicalName),
            new SchemaName(schema),
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: attributes).Value;
    }

    private static ModuleModel CreateModule(string moduleName, params EntityModel[] entities)
        => ModuleModel.Create(new ModuleName(moduleName), isSystemModule: false, isActive: true, entities: entities).Value;

    private static OsmModel CreateModel(params ModuleModel[] modules)
        => OsmModel.Create(DateTime.UtcNow, modules).Value;
}
