using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Configuration;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;

namespace Osm.Emission.Tests;

/// <summary>
/// Tests for StaticSeedSqlBuilder validation override functionality (allowMissingPrimaryKey)
/// </summary>
public sealed class StaticSeedSqlBuilderValidationOverridesTests
{
    private readonly StaticSeedSqlBuilder _builder;

    public StaticSeedSqlBuilderValidationOverridesTests()
    {
        _builder = new StaticSeedSqlBuilder(new SqlLiteralFormatter());
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_AndNoOverride_ThrowsException()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "TestEntity");
        var tableData = CreateTableData(definition);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply));

        Assert.Contains("TestModule::TestEntity", exception.Message);
        Assert.Contains("does not define a primary key", exception.Message);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_WithOverride_Succeeds()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "TestEntity");
        var tableData = CreateTableData(definition);
        var overrides = CreateValidationOverrides("TestModule", new[] { "TestEntity" });

        // Act - Should NOT throw when override is provided
        var result = _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides);

        // Assert - Just verify it didn't throw and contains MERGE statement
        Assert.NotNull(result);
        Assert.Contains("MERGE", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TestEntity", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_WithWildcardOverride_Succeeds()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "AnyEntity");
        var tableData = CreateTableData(definition);
        var overrides = CreateValidationOverridesWithWildcard("TestModule");

        // Act
        var result = _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("MERGE INTO", result);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_WithWrongModuleOverride_ThrowsException()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "TestEntity");
        var tableData = CreateTableData(definition);
        var overrides = CreateValidationOverrides("DifferentModule", new[] { "TestEntity" });

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides));

        Assert.Contains("TestModule::TestEntity", exception.Message);
        Assert.Contains("does not define a primary key", exception.Message);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_WithWrongEntityOverride_ThrowsException()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "TestEntity");
        var tableData = CreateTableData(definition);
        var overrides = CreateValidationOverrides("TestModule", new[] { "DifferentEntity" });

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides));

        Assert.Contains("TestModule::TestEntity", exception.Message);
        Assert.Contains("does not define a primary key", exception.Message);
    }

    [Fact]
    public void BuildBlock_WithPrimaryKey_WithOverride_UsesOnlyPrimaryKeyForMatching()
    {
        // Arrange
        var definition = CreateTableDefinitionWithPrimaryKey("TestModule", "TestEntity");
        var tableData = CreateTableData(definition);
        var overrides = CreateValidationOverrides("TestModule", new[] { "TestEntity" });

        // Act
        var result = _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("MERGE INTO", result);
        // Should only use PK column for matching when PK exists (check for Id in ON clause context)
        Assert.Contains("ON", result);
        Assert.Contains("Id", result);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_EmptyRows_WithOverride_Succeeds()
    {
        // Arrange
        var definition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "TestEntity");
        var tableData = new StaticEntityTableData(definition, ImmutableArray<StaticEntityRow>.Empty);
        var overrides = CreateValidationOverrides("TestModule", new[] { "TestEntity" });

        // Act
        var result = _builder.BuildBlock(tableData, StaticSeedSynchronizationMode.ValidateThenApply, overrides);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("No data rows were returned", result);
    }

    [Fact]
    public void BuildBlock_WithoutPrimaryKey_MultipleEntitiesInOverride_OnlyConfiguredEntitySucceeds()
    {
        // Arrange
        var allowedDefinition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "AllowedEntity");
        var notAllowedDefinition = CreateTableDefinitionWithoutPrimaryKey("TestModule", "NotAllowedEntity");
        var allowedData = CreateTableData(allowedDefinition);
        var notAllowedData = CreateTableData(notAllowedDefinition);
        var overrides = CreateValidationOverrides("TestModule", new[] { "AllowedEntity", "AnotherAllowedEntity" });

        // Act - Allowed entity should succeed
        var result = _builder.BuildBlock(allowedData, StaticSeedSynchronizationMode.ValidateThenApply, overrides);
        Assert.NotNull(result);

        // Act - Not allowed entity should throw
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _builder.BuildBlock(notAllowedData, StaticSeedSynchronizationMode.ValidateThenApply, overrides));

        Assert.Contains("NotAllowedEntity", exception.Message);
    }

    // Helper methods

    private static StaticEntitySeedTableDefinition CreateTableDefinitionWithoutPrimaryKey(string module, string entityName)
    {
        var columns = ImmutableArray.Create(
            new StaticEntitySeedColumn(
                "Column1",
                "Column1",
                "Column1",
                "nvarchar",
                Length: 50,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false,
                IsNullable: false),
            new StaticEntitySeedColumn(
                "Column2",
                "Column2",
                "Column2",
                "int",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false,
                IsNullable: false));

        return new StaticEntitySeedTableDefinition(
            module,
            entityName,
            "dbo",
            $"OSUSR_{entityName.ToUpperInvariant()}",
            $"OSUSR_{entityName.ToUpperInvariant()}",
            columns);
    }

    private static StaticEntitySeedTableDefinition CreateTableDefinitionWithPrimaryKey(string module, string entityName)
    {
        var columns = ImmutableArray.Create(
            new StaticEntitySeedColumn(
                "Id",
                "Id",
                "Id",
                "int",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: true,
                IsIdentity: true,
                IsNullable: false),
            new StaticEntitySeedColumn(
                "Column1",
                "Column1",
                "Column1",
                "nvarchar",
                Length: 50,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false,
                IsNullable: false));

        return new StaticEntitySeedTableDefinition(
            module,
            entityName,
            "dbo",
            $"OSUSR_{entityName.ToUpperInvariant()}",
            $"OSUSR_{entityName.ToUpperInvariant()}",
            columns);
    }

    private static StaticEntityTableData CreateTableData(StaticEntitySeedTableDefinition definition)
    {
        // Create row data matching the number of columns in the definition
        var values = new object[definition.Columns.Length];
        for (var i = 0; i < definition.Columns.Length; i++)
        {
            var column = definition.Columns[i];
            if (column.DataType == "int")
            {
                values[i] = i + 1; // Use sequential integers
            }
            else if (column.DataType == "nvarchar")
            {
                values[i] = $"Value{i + 1}";
            }
            else
            {
                values[i] = $"Value{i + 1}";
            }
        }

        var rows = ImmutableArray.Create(
            StaticEntityRow.Create(values));

        return new StaticEntityTableData(definition, rows);
    }

    private static ModuleValidationOverrides CreateValidationOverrides(string moduleName, string[] entityNames)
    {
        var config = new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            [moduleName] = new ModuleValidationOverrideConfiguration(
                AllowMissingPrimaryKey: entityNames,
                AllowMissingPrimaryKeyForAll: false,
                AllowMissingSchema: Array.Empty<string>(),
                AllowMissingSchemaForAll: false)
        };

        var result = ModuleValidationOverrides.Create(config);
        Assert.True(result.IsSuccess, $"Failed to create validation overrides: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        return result.Value;
    }

    private static ModuleValidationOverrides CreateValidationOverridesWithWildcard(string moduleName)
    {
        var config = new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            [moduleName] = new ModuleValidationOverrideConfiguration(
                AllowMissingPrimaryKey: Array.Empty<string>(),
                AllowMissingPrimaryKeyForAll: true,
                AllowMissingSchema: Array.Empty<string>(),
                AllowMissingSchemaForAll: false)
        };

        var result = ModuleValidationOverrides.Create(config);
        Assert.True(result.IsSuccess, $"Failed to create validation overrides: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        return result.Value;
    }
}
