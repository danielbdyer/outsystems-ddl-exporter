using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.TestSupport;
using Tests.Support;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.Integration.Tests;

[Collection("SqlServerCollection")]
public sealed class SqlDynamicEntityDataProviderIntegrationTests
{
    private readonly SqlServerFixture _fixture;

    public SqlDynamicEntityDataProviderIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [DockerFact]
    public async Task ExtractAsync_ShouldPopulateDatasetAndEmitDynamicScripts()
    {
        await using var stream = FixtureFile.OpenRead("model.edge-case.json");
        var deserializer = new ModelJsonDeserializer();
        var modelResult = deserializer.Deserialize(stream);
        modelResult.IsSuccess.Should().BeTrue();
        var model = modelResult.Value;
        var provider = new SqlDynamicEntityDataProvider(
            TimeProvider.System,
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));
        var logBuilder = new PipelineExecutionLogBuilder(TimeProvider.System);

        var request = new SqlDynamicEntityExtractionRequest(
            _fixture.DatabaseConnectionString,
            SqlConnectionOptions.Default,
            model,
            ModuleFilterOptions.IncludeAll,
            NamingOverrideOptions.Empty,
            CommandTimeoutSeconds: 60,
            logBuilder);

        var result = await provider.ExtractAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var extraction = result.Value;
        var dataset = extraction.Dataset;
        dataset.IsEmpty.Should().BeFalse();
        dataset.Tables.Should().NotBeEmpty();
        dataset.Tables[0].Rows.Should().NotBeEmpty();

        var log = logBuilder.Build();
        log.Entries.Should().Contain(entry => entry.Step == "dynamicData.extract.completed");

        using var tempDirectory = new TempDirectory();
        var generator = new DynamicEntityInsertGenerator(new SqlLiteralFormatter());
        var scripts = generator.GenerateArtifacts(dataset, ImmutableArray<StaticEntityTableData>.Empty, model: model);

        scripts.Should().NotBeEmpty();

        foreach (var script in scripts)
        {
            var moduleDirectory = Path.Combine(tempDirectory.Path, script.Definition.Module ?? "unknown");
            Directory.CreateDirectory(moduleDirectory);
            var filePath = Path.Combine(moduleDirectory, $"{script.Definition.PhysicalName}.dynamic.sql");

            await using var fileStream = File.Create(filePath);
            using var writer = new StreamWriter(fileStream);
            await script.WriteAsync(writer, CancellationToken.None);

            File.Exists(filePath).Should().BeTrue();
        }
    }

    [DockerFact]
    public async Task ExtractAsync_WithAutoLoadParentMode_EnqueuesStaticSeedParents()
    {
        var model = LoadModel();
        var provider = CreateProvider();
        var moduleFilter = CreateCustomerFilter();

        var request = new SqlDynamicEntityExtractionRequest(
            _fixture.DatabaseConnectionString,
            SqlConnectionOptions.Default,
            model,
            moduleFilter,
            NamingOverrideOptions.Empty,
            CommandTimeoutSeconds: 60,
            ParentHandlingMode: StaticSeedParentHandlingMode.AutoLoad);

        var result = await provider.ExtractAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var extraction = result.Value;

        extraction.Dataset.Tables
            .Should()
            .Contain(table => table.Definition.PhysicalName == "OSUSR_DEF_CITY");

        var parent = extraction.StaticSeedParents
            .Should()
            .ContainSingle(status => status.Definition.PhysicalName == "OSUSR_DEF_CITY")
            .Subject;

        parent.Satisfaction.Should().Be(StaticSeedParentSatisfaction.AutoLoaded);
        parent.ReferencedBy.Should().Contain(reference => reference.Module == "AppCore" && reference.Entity == "Customer");
    }

    [DockerFact]
    public async Task ExtractAsync_WithValidationParentMode_FailsWhenStaticSeedParentsMissing()
    {
        var model = LoadModel();
        var provider = CreateProvider();
        var moduleFilter = CreateCustomerFilter();

        var request = new SqlDynamicEntityExtractionRequest(
            _fixture.DatabaseConnectionString,
            SqlConnectionOptions.Default,
            model,
            moduleFilter,
            NamingOverrideOptions.Empty,
            CommandTimeoutSeconds: 60,
            ParentHandlingMode: StaticSeedParentHandlingMode.ValidateStaticSeedApplication);

        var result = await provider.ExtractAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var extraction = result.Value;

        extraction.Dataset.Tables
            .Should()
            .NotContain(table => table.Definition.PhysicalName == "OSUSR_DEF_CITY");

        var parent = extraction.StaticSeedParents
            .Should()
            .ContainSingle(status => status.Definition.PhysicalName == "OSUSR_DEF_CITY")
            .Subject;

        parent.Satisfaction.Should().Be(StaticSeedParentSatisfaction.RequiresVerification);

        var validator = new StaticSeedParentValidator();
        var failingProvider = new FailingStaticEntityDataProvider();
        var verificationResult = await validator
            .ValidateAsync(extraction.StaticSeedParents, failingProvider, CancellationToken.None);

        verificationResult.IsFailure.Should().BeTrue();
        verificationResult.Errors.Should()
            .Contain(error => error.Code == "tests.staticSeed.parents.missing");
    }

    [Fact]
    public void DynamicInsertGenerator_DefersJunctionTablesWhenRequested()
    {
        var (dataset, model) = CreateJunctionDataset();
        var generator = new DynamicEntityInsertGenerator(new SqlLiteralFormatter());

        var scripts = generator.GenerateArtifacts(
            dataset,
            ImmutableArray<StaticEntityTableData>.Empty,
            model: model,
            sortOptions: new EntityDependencySortOptions(true));

        scripts.Should().HaveCount(3);
        scripts[0].Definition.LogicalName.Should().Be("Left");
        scripts[1].Definition.LogicalName.Should().Be("Right");
        scripts[2].Definition.LogicalName.Should().Be("Bridge");
    }

    private static ModuleFilterOptions CreateCustomerFilter()
    {
        var entityFilters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppCore"] = new[] { "Customer" }
        };

        var result = ModuleFilterOptions.Create(
            modules: new[] { "AppCore" },
            includeSystemModules: false,
            includeInactiveModules: false,
            entityFilters: entityFilters);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Osm.Domain.Model.OsmModel LoadModel()
    {
        using var stream = FixtureFile.OpenRead("model.edge-case.json");
        var deserializer = new ModelJsonDeserializer();
        var modelResult = deserializer.Deserialize(stream);
        modelResult.IsSuccess.Should().BeTrue();
        return modelResult.Value;
    }

    private static SqlDynamicEntityDataProvider CreateProvider()
        => new(
            TimeProvider.System,
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));

    private static (DynamicEntityDataset Dataset, Osm.Domain.Model.OsmModel Model) CreateJunctionDataset()
    {
        var leftDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Left",
            "dbo",
            "OSUSR_SAMPLE_LEFT",
            "OSUSR_SAMPLE_LEFT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));
        var rightDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Right",
            "dbo",
            "OSUSR_SAMPLE_RIGHT",
            "OSUSR_SAMPLE_RIGHT",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false)));
        var bridgeDefinition = new StaticEntitySeedTableDefinition(
            "Sample",
            "Bridge",
            "dbo",
            "OSUSR_SAMPLE_BRIDGE",
            "OSUSR_SAMPLE_BRIDGE",
            ImmutableArray.Create(
                new StaticEntitySeedColumn("Id", "ID", "Id", "int", null, null, null, IsPrimaryKey: true, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("LeftId", "LEFTID", "LeftId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false),
                new StaticEntitySeedColumn("RightId", "RIGHTID", "RightId", "int", null, null, null, IsPrimaryKey: false, IsIdentity: false, IsNullable: false)));

        var dataset = DynamicEntityDataset.Create(new[]
        {
            new StaticEntityTableData(leftDefinition, ImmutableArray.Create(
                new StaticEntityRow(ImmutableArray.Create<object?>(1)))),
            new StaticEntityTableData(rightDefinition, ImmutableArray.Create(
                new StaticEntityRow(ImmutableArray.Create<object?>(10)))),
            new StaticEntityTableData(bridgeDefinition, ImmutableArray.Create(
                new StaticEntityRow(ImmutableArray.Create<object?>(100, 1, 10))))
        });

        var leftEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Left"),
            new TableName("OSUSR_SAMPLE_LEFT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[] { CreateIdentifierAttribute("ID") }).Value;

        var rightEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Right"),
            new TableName("OSUSR_SAMPLE_RIGHT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[] { CreateIdentifierAttribute("ID") }).Value;

        var leftRelationship = RelationshipModel.Create(
            new AttributeName("LeftId"),
            new EntityName("Left"),
            new TableName("OSUSR_SAMPLE_LEFT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_BRIDGE_LEFT",
                    referencedSchema: "core",
                    referencedTable: "OSUSR_SAMPLE_LEFT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("LEFTID", "LeftId", "ID", "Id", 0) })
            }).Value;

        var rightRelationship = RelationshipModel.Create(
            new AttributeName("RightId"),
            new EntityName("Right"),
            new TableName("OSUSR_SAMPLE_RIGHT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_BRIDGE_RIGHT",
                    referencedSchema: "core",
                    referencedTable: "OSUSR_SAMPLE_RIGHT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("RIGHTID", "RightId", "ID", "Id", 0) })
            }).Value;

        var bridgeEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Bridge"),
            new TableName("OSUSR_SAMPLE_BRIDGE"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateIdentifierAttribute("ID"),
                CreateMandatoryAttribute("LEFTID"),
                CreateMandatoryAttribute("RIGHTID")
            },
            relationships: new[] { leftRelationship, rightRelationship }).Value;

        var module = ModuleModel.Create(
            new ModuleName("Sample"),
            isSystemModule: false,
            isActive: true,
            entities: new[] { leftEntity, rightEntity, bridgeEntity }).Value;
        var model = Osm.Domain.Model.OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        return (dataset, model);
    }

    private static AttributeModel CreateIdentifierAttribute(string columnName)
        => CreateAttribute(columnName, columnName, isIdentifier: true, isMandatory: true);

    private static AttributeModel CreateMandatoryAttribute(string columnName)
        => CreateAttribute(columnName, columnName, isIdentifier: false, isMandatory: true);

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier, bool isMandatory)
    {
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isMandatory,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reality: new AttributeReality(null, null, null, null, IsPresentButInactive: false),
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }

    private sealed class FailingStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(
                ValidationError.Create(
                    "tests.staticSeed.parents.missing",
                    "Static seed parent tables were not available.")));
        }
    }
}
