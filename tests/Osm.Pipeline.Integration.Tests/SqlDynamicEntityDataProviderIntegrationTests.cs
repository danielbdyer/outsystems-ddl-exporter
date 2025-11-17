using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
        var dataset = result.Value.Dataset;
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
}
