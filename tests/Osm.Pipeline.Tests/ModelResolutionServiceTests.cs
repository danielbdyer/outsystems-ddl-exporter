using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
using Osm.Pipeline.SqlExtraction;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ModelResolutionServiceTests
{
    private static readonly ResolvedSqlOptions DefaultSqlOptions = new(
        ConnectionString: null,
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, null, null));

    [Fact]
    public async Task ResolveModelAsync_ReturnsExistingModelPath_WhenOverrideProvided()
    {
        var dispatcher = new RecordingDispatcher();
        var service = new ModelResolutionService(dispatcher);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(
            ModelPath: "existing.json",
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            DefaultSqlOptions,
            new OutputDirectoryResolution("out", Path.Combine("out", "model.extracted.json")),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("existing.json", result.Value.ModelPath);
        Assert.False(result.Value.WasExtracted);
        Assert.Empty(result.Value.Warnings);
        Assert.Null(dispatcher.ExtractRequest);
    }

    [Fact]
    public async Task ResolveModelAsync_FailsWhenConnectionStringMissing()
    {
        var dispatcher = new RecordingDispatcher();
        var service = new ModelResolutionService(dispatcher);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(null, null, null, null, null, null, null);

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            DefaultSqlOptions,
            new OutputDirectoryResolution("out", Path.Combine("out", "model.extracted.json")),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, static error => error.Code == "pipeline.buildSsdt.model.extraction.connectionStringMissing");
    }

    [Fact]
    public async Task ResolveModelAsync_ExtractsModelAndWritesFile()
    {
        using var output = new TempDirectory();
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetExtractionResult(Result<ModelExtractionResult>.Success(CreateExtractionResult()));
        var service = new ModelResolutionService(dispatcher);
        var configuration = CreateConfiguration();
        var overrides = new BuildSsdtOverrides(null, null, null, null, null, null, null);
        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };

        var result = await service.ResolveModelAsync(
            configuration,
            overrides,
            ModuleFilterOptions.IncludeAll,
            sqlOptions,
            new OutputDirectoryResolution(output.Path, Path.Combine(output.Path, "model.extracted.json")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasExtracted);
        Assert.Equal(new[] { "warning" }, result.Value.Warnings);
        Assert.NotNull(dispatcher.ExtractRequest);
        Assert.True(File.Exists(result.Value.ModelPath));
        Assert.Equal(Path.Combine(output.Path, "model.extracted.json"), result.Value.ModelPath);
        var content = await File.ReadAllTextAsync(result.Value.ModelPath);
        Assert.Equal("{\"model\":true}", content);
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
        return new ModelExtractionResult(model, "{\"model\":true}", DateTimeOffset.UtcNow, new[] { "warning" }, metadata);
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
