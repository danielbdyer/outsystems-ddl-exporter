using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class ExtractModelCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptions()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeExtractApplicationService();

        using var temp = new TempDirectory();
        var outputPath = Path.Combine(temp.Path, "model.json");

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<ExtractModelCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ExtractModelCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var moduleBinder = provider.GetRequiredService<ModuleFilterOptionBinder>();
        Assert.Contains(moduleBinder.ModulesOption, command.Options);
        var sqlBinder = provider.GetRequiredService<SqlOptionBinder>();
        Assert.Contains(sqlBinder.ConnectionStringOption, command.Options);
        Assert.Contains(sqlBinder.ProfilingConnectionStringsOption, command.Options);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var metadataPath = Path.Combine(temp.Path, "metadata.json");
        var args = $"extract-model --config config.json --modules ModuleA,ModuleB --include-system-modules --include-inactive-attributes --out {outputPath} --mock-advanced-sql manifest.json --sql-metadata-out {metadataPath} --connection-string DataSource";
        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal("config.json", configurationService.LastPath);
        var input = application.LastInput!;
        Assert.Equal(new[] { "ModuleA", "ModuleB" }, input.Overrides.Modules);
        Assert.True(input.Overrides.IncludeSystemModules);
        Assert.False(input.Overrides.OnlyActiveAttributes);
        Assert.Equal(outputPath, input.Overrides.OutputPath);
        Assert.Equal("manifest.json", input.Overrides.MockAdvancedSqlManifest);
        Assert.Equal(metadataPath, input.Overrides.SqlMetadataOutputPath);
        Assert.Equal("DataSource", input.Sql.ConnectionString);
        Assert.Null(input.Sql.ProfilingConnectionStrings);
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public string? LastPath { get; private set; }

        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            LastPath = overrideConfigPath;
            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class FakeExtractApplicationService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        public ExtractModelApplicationInput? LastInput { get; private set; }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            var model = ModelFixtures.LoadModel("model.micro-physical.json");
            var snapshot = new OutsystemsMetadataSnapshot(
                Array.Empty<OutsystemsModuleRow>(),
                Array.Empty<OutsystemsEntityRow>(),
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
                "TestDb");
            var buffer = new MemoryStream();
            using (var writer = new StreamWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("{}");
            }

            buffer.Position = 0;
            var payload = ModelJsonPayload.FromStream(buffer);
            var extraction = new ModelExtractionResult(model, payload, DateTimeOffset.UtcNow, Array.Empty<string>(), snapshot);
            var result = new ExtractModelApplicationResult(extraction, input.Overrides.OutputPath ?? "model.json");
            return Task.FromResult(Result<ExtractModelApplicationResult>.Success(result));
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(
            ICliConfigurationService configurationService,
            IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> applicationService)
        {
            _verb = new ExtractModelVerb(configurationService, applicationService);
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }
}
