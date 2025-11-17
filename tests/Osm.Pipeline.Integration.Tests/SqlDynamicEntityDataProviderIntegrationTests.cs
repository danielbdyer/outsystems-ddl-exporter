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
        var scripts = generator.GenerateScripts(dataset, ImmutableArray<StaticEntityTableData>.Empty, model: model);

        scripts.Should().NotBeEmpty();

        foreach (var script in scripts)
        {
            var moduleDirectory = Path.Combine(tempDirectory.Path, script.Definition.Module ?? "unknown");
            Directory.CreateDirectory(moduleDirectory);
            var filePath = Path.Combine(moduleDirectory, $"{script.Definition.PhysicalName}.dynamic.sql");
            await File.WriteAllTextAsync(filePath, script.Script);
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
