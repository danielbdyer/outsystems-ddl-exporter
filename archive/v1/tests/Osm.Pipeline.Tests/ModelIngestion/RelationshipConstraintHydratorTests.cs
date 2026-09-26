using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests.ModelIngestion;

public sealed class RelationshipConstraintHydratorTests
{
    [Fact]
    public async Task HydrateAsync_ShouldPopulateConstraintColumns()
    {
        var model = CreateModel(withConstraintName: "FK_CHILD_PARENT");
        var metadata = new List<ForeignKeyColumnMetadata>
        {
            new(new RelationshipConstraintKey("dbo", "OSUSR_SAMPLE_CHILD", "FK_CHILD_PARENT"), 1, "PARENTID", "ID", "dbo", "OSUSR_SAMPLE_PARENT")
        };
        var provider = new RecordingMetadataProvider(metadata);
        var hydrator = new RelationshipConstraintHydrator(provider);
        var sqlOptions = new ModelIngestionSqlMetadataOptions(
            "Server=(local);Database=OSM",
            SqlConnectionOptions.Default);

        var result = await hydrator.HydrateAsync(model, sqlOptions, warnings: null, cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(error => error.Message)));
        var relationship = result.Value.Modules[0].Entities[1].Relationships[0];
        var constraint = Assert.Single(relationship.ActualConstraints);
        var column = Assert.Single(constraint.Columns);
        Assert.Equal("PARENTID", column.OwnerColumn);
        Assert.Equal("ParentId", column.OwnerAttribute);
        Assert.Equal("ID", column.ReferencedColumn);
        Assert.Equal("Id", column.ReferencedAttribute);
        Assert.True(provider.RequestedKeys.Contains(new RelationshipConstraintKey("dbo", "OSUSR_SAMPLE_CHILD", "FK_CHILD_PARENT")));
    }

    [Fact]
    public async Task HydrateAsync_ShouldAddWarning_WhenMetadataMissing()
    {
        var model = CreateModel(withConstraintName: "FK_CHILD_PARENT");
        var provider = new RecordingMetadataProvider(Array.Empty<ForeignKeyColumnMetadata>());
        var hydrator = new RelationshipConstraintHydrator(provider);
        var warnings = new List<string>();
        var sqlOptions = new ModelIngestionSqlMetadataOptions(
            "Server=(local);Database=OSM",
            SqlConnectionOptions.Default);

        var result = await hydrator.HydrateAsync(model, sqlOptions, warnings, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(warnings, warning => warning.Contains("FK_CHILD_PARENT"));
    }

    [Fact]
    public async Task HydrateAsync_ShouldWarn_WhenConstraintNameMissing()
    {
        var model = CreateModel(withConstraintName: " ");
        var provider = new RecordingMetadataProvider(Array.Empty<ForeignKeyColumnMetadata>());
        var hydrator = new RelationshipConstraintHydrator(provider);
        var warnings = new List<string>();
        var sqlOptions = new ModelIngestionSqlMetadataOptions(
            "Server=(local);Database=OSM",
            SqlConnectionOptions.Default);

        var result = await hydrator.HydrateAsync(model, sqlOptions, warnings, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(warnings, warning => warning.Contains("missing constraint name", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(provider.RequestedKeys);
    }

    private static OsmModel CreateModel(string withConstraintName)
    {
        var idAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var parentEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { idAttribute }).Value;

        var parentIdAttribute = AttributeModel.Create(
            AttributeName.Create("ParentId").Value,
            ColumnName.Create("PARENTID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference
                .Create(
                    isReference: true,
                    targetEntityId: null,
                    targetEntity: new EntityName("Parent"),
                    targetPhysicalName: new TableName("OSUSR_SAMPLE_PARENT"),
                    deleteRuleCode: null,
                    hasDatabaseConstraint: true)
                .Value)
            .Value;

        var childRelationship = RelationshipModel.Create(
            AttributeName.Create("ParentId").Value,
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            deleteRuleCode: "NO_ACTION",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    withConstraintName,
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_PARENT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    Array.Empty<RelationshipActualConstraintColumn>())
            }).Value;

        var childEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Child"),
            new TableName("OSUSR_SAMPLE_CHILD"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { idAttribute, parentIdAttribute },
            relationships: new[] { childRelationship }).Value;

        var module = ModuleModel.Create(
            new ModuleName("Sample"),
            isSystemModule: false,
            isActive: true,
            entities: new[] { parentEntity, childEntity }).Value;

        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

    private sealed class RecordingMetadataProvider : IRelationshipConstraintMetadataProvider
    {
        private readonly IReadOnlyList<ForeignKeyColumnMetadata> _results;

        public RecordingMetadataProvider(IReadOnlyList<ForeignKeyColumnMetadata> results)
        {
            _results = results;
        }

        public HashSet<RelationshipConstraintKey> RequestedKeys { get; } = new(RelationshipConstraintKeyComparer.Instance);

        public Task<IReadOnlyList<ForeignKeyColumnMetadata>> LoadAsync(
            IReadOnlyCollection<RelationshipConstraintKey> requests,
            ModelIngestionSqlMetadataOptions sqlOptions,
            CancellationToken cancellationToken)
        {
            foreach (var key in requests)
            {
                RequestedKeys.Add(key);
            }

            return Task.FromResult(_results);
        }
    }
}
