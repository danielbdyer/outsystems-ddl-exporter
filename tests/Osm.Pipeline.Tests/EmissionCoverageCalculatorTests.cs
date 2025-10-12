using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
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
                entity.PhysicalName.Value,
                "Customer",
                StringComparison.OrdinalIgnoreCase)));
        Assert.True(moduleIndex >= 0, "Expected customer module in fixture model.");

        var module = model.Modules[moduleIndex];
        var entityIndex = Array.FindIndex(
            module.Entities.ToArray(),
            entity => string.Equals(entity.PhysicalName.Value, "Customer", StringComparison.OrdinalIgnoreCase));
        Assert.True(entityIndex >= 0, "Expected customer entity in fixture model.");

        var entity = module.Entities[entityIndex];
        var attributeIndex = Array.FindIndex(
            entity.Attributes.ToArray(),
            attribute => string.Equals(attribute.LogicalName.Value, "Name", StringComparison.OrdinalIgnoreCase));
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
            message => message.Contains("IDX_Customer_Name", StringComparison.OrdinalIgnoreCase));
    }
}
