using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class AdvancedSqlMetadataOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnSnapshotAndModulesWithoutEntities()
    {
        var snapshot = CreateSnapshot();
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
        var orchestrator = new AdvancedSqlMetadataOrchestrator(reader, NullLogger<AdvancedSqlMetadataOrchestrator>.Instance);
        var metadataLog = new SqlMetadataLog();
        var options = ModelExtractionOptions.InMemory(metadataLog: metadataLog);
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: false, onlyActiveAttributes: false).Value;

        var result = await orchestrator.ExecuteAsync(command, options, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "EmptyModule" }, result.Value.ModulesWithoutEntities);
        Assert.Equal(snapshot, result.Value.Snapshot);
        Assert.InRange((DateTimeOffset.UtcNow - result.Value.ExportedAtUtc).TotalSeconds, 0, 5);

        var state = metadataLog.BuildState();
        Assert.True(state.HasSnapshot);
        Assert.True(state.HasRequests);
        Assert.False(state.HasErrors);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateMetadataFailure()
    {
        var error = ValidationError.Create("boom", "failure");
        var reader = new StubMetadataReader(Result<OutsystemsMetadataSnapshot>.Failure(error));
        var metadataLog = new SqlMetadataLog();
        var orchestrator = new AdvancedSqlMetadataOrchestrator(reader, NullLogger<AdvancedSqlMetadataOrchestrator>.Instance);
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: false, onlyActiveAttributes: false).Value;

        var result = await orchestrator.ExecuteAsync(command, ModelExtractionOptions.InMemory(metadataLog: metadataLog), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("boom", Assert.Single(result.Errors).Code);

        var state = metadataLog.BuildState();
        Assert.True(state.HasErrors);
        Assert.False(state.HasSnapshot);
    }

    private static OutsystemsMetadataSnapshot CreateSnapshot()
    {
        var modules = new List<OutsystemsModuleRow>
        {
            new(1, "Inventory", IsSystemModule: false, ModuleIsActive: true, EspaceKind: null, EspaceSsKey: null),
            new(2, "EmptyModule", IsSystemModule: false, ModuleIsActive: true, EspaceKind: null, EspaceSsKey: null),
        };

        var entities = new List<OutsystemsEntityRow>
        {
            new(
                EntityId: 1,
                EntityName: "Product",
                PhysicalTableName: "OSUSR_INV_PRODUCT",
                EspaceId: 1,
                EntityIsActive: true,
                IsSystemEntity: false,
                IsExternalEntity: false,
                DataKind: null,
                PrimaryKeySsKey: null,
                EntitySsKey: null,
                EntityDescription: null),
        };

        return new OutsystemsMetadataSnapshot(
            modules,
            entities,
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            Array.Empty<OutsystemsModuleJsonRow>(),
            "FixtureDb");
    }

    private sealed class StubMetadataReader : IOutsystemsMetadataReader, IMetadataSnapshotDiagnostics
    {
        private readonly Result<OutsystemsMetadataSnapshot> _result;

        public StubMetadataReader(Result<OutsystemsMetadataSnapshot> result)
        {
            _result = result;
        }

        public MetadataRowSnapshot? LastFailureRowSnapshot => null;

        public Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
