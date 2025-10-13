using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Pipeline.SqlExtraction;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public class SqlModelExtractionServiceTests
{
    [Fact]
    public void CreateCommand_ShouldRejectNullModule()
    {
        var result = ModelExtractionCommand.Create(new[] { "AppCore", null! }, includeSystemModules: false, onlyActiveAttributes: false);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.modules.null", error.Code);
    }

    [Fact]
    public void CreateCommand_ShouldRejectEmptyModule()
    {
        var result = ModelExtractionCommand.Create(new[] { "  " }, includeSystemModules: false, onlyActiveAttributes: false);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.modules.empty", error.Code);
    }

    [Fact]
    public void CreateCommand_ShouldSortAndDeduplicateModules()
    {
        var result = ModelExtractionCommand.Create(new[] { "Ops", "AppCore", "ops" }, includeSystemModules: true, onlyActiveAttributes: false);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.ModuleNames.Select(static module => module.Value));
        Assert.True(result.Value.IncludeSystemModules);
        Assert.False(result.Value.OnlyActiveAttributes);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnModelAndJson()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.micro-unique.json"));
        var executor = new StubExecutor(Result<string>.Success(json));
        var service = new SqlModelExtractionService(executor, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Model);
        Assert.Equal(json, result.Value.Json);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPropagateExecutorFailure()
    {
        var failure = Result<string>.Failure(ValidationError.Create("boom", "fail"));
        var executor = new StubExecutor(failure);
        var service = new SqlModelExtractionService(executor, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("boom", error.Code);
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectEmptyJson()
    {
        var executor = new StubExecutor(Result<string>.Success("   "));
        var service = new SqlModelExtractionService(executor, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.sql.emptyJson", error.Code);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSurfaceWarningsForModulesWithoutEntities()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "EmptyModule",
              "isSystem": false,
              "isActive": true,
              "entities": []
            },
            {
              "name": "Inventory",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Product",
                  "physicalName": "OSUSR_INV_PRODUCT",
                  "db_schema": "dbo",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "attributes": [
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    }
                  ],
                  "indexes": [],
                  "relationships": []
                }
              ]
            }
          ]
        }
        """;

        var executor = new StubExecutor(Result<string>.Success(json));
        var service = new SqlModelExtractionService(executor, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Contains("EmptyModule", warning);
        var module = Assert.Single(result.Value.Model.Modules);
        Assert.Equal("Inventory", module.Name.Value);
    }

    [Fact]
    public async Task ExtractAsync_ShouldExposeUserReferencesWhenSystemModulesExcluded()
    {
        var json = await File.ReadAllTextAsync(FixtureFile.GetPath("model.edge-case.json"));
        var executor = new StubExecutor(Result<string>.Success(json));
        var service = new SqlModelExtractionService(executor, new ModelJsonDeserializer());
        var command = ModelExtractionCommand.Create(
            new[] { "AppCore", "ExtBilling", "Ops" },
            includeSystemModules: false,
            onlyActiveAttributes: false).Value;

        var result = await service.ExtractAsync(command);

        Assert.True(result.IsSuccess);
        var model = result.Value.Model;
        var opsModule = Assert.Single(model.Modules.Where(module
            => module.Name.Value.Equals("Ops", StringComparison.OrdinalIgnoreCase)));
        var jobRun = Assert.Single(opsModule.Entities.Where(entity
            => entity.LogicalName.Value.Equals("JobRun", StringComparison.OrdinalIgnoreCase)));
        var triggeredBy = Assert.Single(jobRun.Attributes.Where(attribute
            => attribute.LogicalName.Value.Equals("TriggeredByUserId", StringComparison.OrdinalIgnoreCase)));

        Assert.True(triggeredBy.Reference.IsReference);
        Assert.Equal("User", triggeredBy.Reference.TargetEntity?.Value);
        Assert.Equal("OSUSR_U_USER", triggeredBy.Reference.TargetPhysicalName?.Value);
    }

    private sealed class StubExecutor : IAdvancedSqlExecutor
    {
        private readonly Result<string> _result;

        public StubExecutor(Result<string> result)
        {
            _result = result;
        }

        public Task<Result<string>> ExecuteAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
