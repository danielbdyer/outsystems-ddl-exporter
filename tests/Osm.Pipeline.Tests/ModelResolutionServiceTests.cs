using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ModelResolutionServiceTests
{
    private static readonly ResolvedSqlOptions DefaultSqlOptions = new(
        ConnectionString: null,
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, null, null),
        MetadataContract: MetadataContractOverrides.Strict);

    [Fact]
    public async Task ResolveModelAsync_ReturnsExistingModelPath_WhenOverrideProvided()
    {
        var dispatcher = new RecordingDispatcher();
        var fileSystem = new MockFileSystem();
        var service = new ModelResolutionService(dispatcher, fileSystem);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(
            ModelPath: "existing.json",
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            DefaultSqlOptions,
            outputDirectory: "out",
            sqlMetadataLog: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("existing.json", result.Value.ModelPath);
        Assert.False(result.Value.WasExtracted);
        Assert.Empty(result.Value.Warnings);
        Assert.Null(dispatcher.ExtractRequest);
        Assert.Null(result.Value.Extraction);
    }

    [Fact]
    public async Task ResolveModelAsync_FailsWhenConnectionStringMissing()
    {
        var dispatcher = new RecordingDispatcher();
        var fileSystem = new MockFileSystem();
        var service = new ModelResolutionService(dispatcher, fileSystem);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(null, null, null, null, null, null, null, null);

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            DefaultSqlOptions,
            outputDirectory: "out",
            sqlMetadataLog: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, static error => error.Code == "pipeline.buildSsdt.model.extraction.connectionStringMissing");
    }

    [Fact]
    public async Task ResolveModelAsync_ExtractsModelAndWritesFile()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), "/");
        fileSystem.AddDirectory("/working");
        fileSystem.Directory.SetCurrentDirectory("/working");
        var outputDirectory = "/working/out";
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetExtractionResult(Result<ModelExtractionResult>.Success(CreateExtractionResult()));
        var service = new ModelResolutionService(dispatcher, fileSystem);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(null, null, null, null, null, null, null, null);
        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            sqlOptions,
            outputDirectory,
            sqlMetadataLog: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasExtracted);
        Assert.Equal(new[] { "warning" }, result.Value.Warnings);
        Assert.NotNull(dispatcher.ExtractRequest);
        Assert.True(fileSystem.FileExists(result.Value.ModelPath));
        Assert.Equal(fileSystem.Path.Combine(outputDirectory, "model.extracted.json"), result.Value.ModelPath);
        var content = fileSystem.File.ReadAllText(result.Value.ModelPath);
        Assert.Equal("{\"model\":true}", content);
        Assert.NotNull(result.Value.Extraction);
        Assert.Equal("{\"model\":true}", await result.Value.Extraction!.JsonPayload.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveModelAsync_RespectsExtractModelInlineOverride()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), "/");
        fileSystem.AddDirectory("/working");
        fileSystem.Directory.SetCurrentDirectory("/working");
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetExtractionResult(Result<ModelExtractionResult>.Success(CreateExtractionResult()));
        var service = new ModelResolutionService(dispatcher, fileSystem);
        var configuration = CreateConfiguration() with { ModelPath = "configured.json" };
        var overrides = new BuildSsdtOverrides(
            ModelPath: "override.json",
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null,
            ExtractModelInline: true);
        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            sqlOptions,
            outputDirectory: "/working/out",
            sqlMetadataLog: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasExtracted);
        Assert.NotNull(dispatcher.ExtractRequest);
        Assert.NotEqual("override.json", result.Value.ModelPath);
    }

    private static CliConfiguration CreateConfiguration()
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            CacheConfiguration.Empty,
            ProfilerConfiguration.Empty,
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
    }

    private static ModelExtractionResult CreateExtractionResult()
    {
        var moduleName = ModuleName.Create("SampleModule").Value;
        var entityName = EntityName.Create("SampleEntity").Value;
        var tableName = TableName.Create("OSUSR_SAMPLE").Value;
        var schemaName = SchemaName.Create("dbo").Value;
        var attribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("Id").Value,
            dataType: "int",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;
        var entity = EntityModel.Create(
            moduleName,
            entityName,
            tableName,
            schemaName,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { attribute },
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: false).Value;
        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
        var metadata = new OutsystemsMetadataSnapshot(
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
            "database");
        var buffer = new MemoryStream();
        using var writer = new StreamWriter(buffer, Encoding.UTF8, leaveOpen: true);
        writer.Write("{\"model\":true}");
        writer.Flush();
        buffer.Position = 0;
        var payload = ModelJsonPayload.FromStream(buffer);
        return new ModelExtractionResult(model, payload, DateTimeOffset.UtcNow, new[] { "warning" }, metadata);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public ExtractModelPipelineRequest? ExtractRequest { get; private set; }

        private Result<ModelExtractionResult>? _extractionResult;

        public void SetExtractionResult(Result<ModelExtractionResult> result)
        {
            _extractionResult = result ?? throw new ArgumentNullException(nameof(result));
        }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            switch (command)
            {
                case ExtractModelPipelineRequest extract:
                    ExtractRequest = extract;
                    if (_extractionResult is null)
                    {
                        throw new InvalidOperationException("Extraction result was not configured for the dispatcher.");
                    }

                    if (_extractionResult.IsFailure)
                    {
                        return Task.FromResult(Result<TResponse>.Failure(_extractionResult.Errors));
                    }

                    if (_extractionResult.Value is not ModelExtractionResult value)
                    {
                        throw new InvalidOperationException("Extraction result did not contain a payload.");
                    }

                    if (typeof(TResponse) != typeof(ModelExtractionResult))
                    {
                        throw new InvalidOperationException($"Unexpected response type requested: {typeof(TResponse).Name}");
                    }

                    return Task.FromResult(Result<TResponse>.Success((TResponse)(object)value));
                default:
                    throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
            }
        }
    }
}
