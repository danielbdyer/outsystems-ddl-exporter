using System;
using System.Collections.Generic;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class ColumnDecisionServiceTests
{
    [Fact]
    public void Analyze_populates_column_module_rollups()
    {
        var (model, snapshot, moduleAColumn, moduleBColumn) = BuildModel();
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated);

        var lookupContext = TighteningLookupContext.Create(model, snapshot, options);
        var analyzers = new ITighteningAnalyzer[]
        {
            new NullabilityEvaluator(
                options,
                lookupContext.ColumnProfiles,
                lookupContext.UniqueProfiles,
                lookupContext.ForeignKeyReality,
                lookupContext.ForeignKeyTargets,
                lookupContext.UniqueEvidence.SingleColumnClean,
                lookupContext.UniqueEvidence.SingleColumnDuplicates,
                lookupContext.UniqueEvidence.CompositeClean,
                lookupContext.UniqueEvidence.CompositeDuplicates),
            new ForeignKeyEvaluator(options.ForeignKeys, lookupContext.ForeignKeyReality, lookupContext.ForeignKeyTargets)
        };

        var service = new ColumnDecisionService(lookupContext, analyzers);
        var result = service.Analyze();

        Assert.Equal("ModuleA", result.ColumnModules[moduleAColumn]);
        Assert.Equal("ModuleB", result.ColumnModules[moduleBColumn]);
        Assert.True(result.AnalysisBuilders.ContainsKey(moduleAColumn));
        Assert.True(result.AnalysisBuilders.ContainsKey(moduleBColumn));
    }

    private static (OsmModel Model, ProfileSnapshot Snapshot, ColumnCoordinate ModuleAColumn, ColumnCoordinate ModuleBColumn) BuildModel()
    {
        var schema = SchemaName.Create("dbo").Value;

        var moduleAName = ModuleName.Create("ModuleA").Value;
        var moduleBName = ModuleName.Create("ModuleB").Value;

        var entityA = CreateEntity(
            moduleAName,
            "EntityA",
            "ENTITYA",
            new[]
            {
                CreateAttribute("EntityAId", "ENTITYAID", isMandatory: true, isIdentifier: true),
                CreateAttribute("EntityAValue", "ENTITYAVALUE", isMandatory: false, isIdentifier: false)
            });

        var entityB = CreateEntity(
            moduleBName,
            "EntityB",
            "ENTITYB",
            new[]
            {
                CreateAttribute("EntityBId", "ENTITYBID", isMandatory: true, isIdentifier: true),
                CreateAttribute("EntityBValue", "ENTITYBVALUE", isMandatory: false, isIdentifier: false)
            });

        var moduleA = ModuleModel.Create(moduleAName, isSystemModule: false, isActive: true, new[] { entityA }).Value;
        var moduleB = ModuleModel.Create(moduleBName, isSystemModule: false, isActive: true, new[] { entityB }).Value;

        var model = OsmModel.Create(DateTime.UtcNow, new[] { moduleA, moduleB }).Value;

        var probeStatus = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 10);

        var columnProfiles = new List<ColumnProfile>
        {
            ColumnProfile.Create(schema, entityA.PhysicalName, entityA.Attributes[0].ColumnName, false, false, true, true, null, 10, 0, probeStatus).Value,
            ColumnProfile.Create(schema, entityA.PhysicalName, entityA.Attributes[1].ColumnName, true, false, false, false, null, 10, 2, probeStatus).Value,
            ColumnProfile.Create(schema, entityB.PhysicalName, entityB.Attributes[0].ColumnName, false, false, true, true, null, 20, 0, probeStatus).Value,
            ColumnProfile.Create(schema, entityB.PhysicalName, entityB.Attributes[1].ColumnName, true, false, false, false, null, 20, 4, probeStatus).Value
        };

        var snapshot = ProfileSnapshot.Create(
            columnProfiles,
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        var moduleAColumn = new ColumnCoordinate(schema, entityA.PhysicalName, entityA.Attributes[1].ColumnName);
        var moduleBColumn = new ColumnCoordinate(schema, entityB.PhysicalName, entityB.Attributes[1].ColumnName);

        return (model, snapshot, moduleAColumn, moduleBColumn);
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isMandatory, bool isIdentifier)
        => AttributeModel.Create(
            AttributeName.Create(logicalName).Value,
            ColumnName.Create(columnName).Value,
            dataType: "INT",
            isMandatory,
            isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None).Value;

    private static EntityModel CreateEntity(ModuleName module, string logicalName, string tableName, IEnumerable<AttributeModel> attributes)
        => EntityModel.Create(
            module,
            EntityName.Create(logicalName).Value,
            TableName.Create(tableName).Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes,
            indexes: Array.Empty<IndexModel>(),
            relationships: Array.Empty<RelationshipModel>(),
            triggers: Array.Empty<TriggerModel>(),
            metadata: EntityMetadata.Empty).Value;
}
