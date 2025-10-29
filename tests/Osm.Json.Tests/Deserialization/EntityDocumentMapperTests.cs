using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;
using Osm.Json.Deserialization;

namespace Osm.Json.Tests.Deserialization;

using AttributeDocument = ModelJsonDeserializer.AttributeDocument;
using EntityDocument = ModelJsonDeserializer.EntityDocument;

public class EntityDocumentMapperTests
{
    private static DocumentMapperContext CreateContext(
        List<string> warnings,
        ModuleValidationOverrides? overrides = null,
        bool allowDuplicateAttributeLogicalNames = false,
        bool allowDuplicateAttributeColumnNames = false)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var options = new ModelJsonDeserializerOptions(
            overrides,
            missingSchemaFallback: null,
            allowDuplicateAttributeLogicalNames: allowDuplicateAttributeLogicalNames,
            allowDuplicateAttributeColumnNames: allowDuplicateAttributeColumnNames);
        return new DocumentMapperContext(options, warnings, serializerOptions);
    }

    private static AttributeDocument CreateIdentifierAttribute()
        => new()
        {
            Name = "Id",
            PhysicalName = "ID",
            DataType = "Identifier",
            IsMandatory = true,
            IsIdentifier = true,
            IsAutoNumber = true,
            IsActive = true,
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

    [Fact]
    public void Map_ShouldFail_WhenSchemaMissingAndNoOverride()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var mapper = CreateEntityMapper(context);

        var document = new EntityDocument
        {
            Name = "Invoice",
            PhysicalName = "OSUSR_FIN_INVOICE",
            IsActive = true,
            Attributes = new[] { CreateIdentifierAttribute() }
        };

        var result = mapper.Map(
            ModuleName.Create("Finance").Value,
            document,
            DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("entity.schema.missing", error.Code);
        Assert.Contains("Path: $['modules'][0]['entities'][0]['schema']", error.Message);
    }

    [Fact]
    public void Map_ShouldAllowMissingSchema_WhenOverrideExists()
    {
        var warnings = new List<string>();
        var overridesResult = ModuleValidationOverrides.Create(new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            ["Finance"] = new ModuleValidationOverrideConfiguration(
                Array.Empty<string>(),
                false,
                new[] { "Invoice" },
                false)
        });
        Assert.True(overridesResult.IsSuccess, string.Join(", ", overridesResult.Errors.Select(e => e.Message)));

        var context = CreateContext(warnings, overridesResult.Value);
        var mapper = CreateEntityMapper(context);

        var document = new EntityDocument
        {
            Name = "Invoice",
            PhysicalName = "OSUSR_FIN_INVOICE",
            IsActive = true,
            Attributes = new[] { CreateIdentifierAttribute() }
        };

        var result = mapper.Map(
            ModuleName.Create("Finance").Value,
            document,
            DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Contains(warnings, warning => warning.Contains("missing schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Map_ShouldWarnAndSucceed_WhenDuplicateLogicalNamesAllowed()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings, allowDuplicateAttributeLogicalNames: true);
        var mapper = CreateEntityMapper(context);

        var duplicateAttribute = new AttributeDocument
        {
            Name = "Id",
            PhysicalName = "ID2",
            DataType = "Identifier",
            IsMandatory = false,
            IsIdentifier = false,
            IsAutoNumber = false,
            IsActive = true,
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

        var document = new EntityDocument
        {
            Name = "Invoice",
            PhysicalName = "OSUSR_FIN_INVOICE",
            Schema = "dbo",
            IsActive = true,
            Attributes = new[] { CreateIdentifierAttribute(), duplicateAttribute }
        };

        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);
        var result = mapper.Map(ModuleName.Create("Finance").Value, document, path);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(warnings, w => w.Contains("duplicate attribute logical name 'Id'", StringComparison.Ordinal));
        Assert.Contains("mapped to columns", warning);
        Assert.Contains("Path: $['modules'][0]['entities'][0]['attributes']", warning);
    }

    [Fact]
    public void SchemaResolver_ShouldApplyFallback_WhenOverrideAllowsMissingSchema()
    {
        var warnings = new List<string>();
        var overridesResult = ModuleValidationOverrides.Create(new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            ["Finance"] = new ModuleValidationOverrideConfiguration(
                Array.Empty<string>(),
                false,
                new[] { "Invoice" },
                false)
        });
        Assert.True(overridesResult.IsSuccess);

        var context = CreateContext(warnings, overridesResult.Value);
        var resolver = new EntityDocumentMapper.SchemaResolver(context);
        var moduleName = ModuleName.Create("Finance").Value;
        var entityName = EntityName.Create("Invoice").Value;
        var document = new EntityDocument
        {
            Name = entityName.Value,
            PhysicalName = "OSUSR_FIN_INVOICE",
            Attributes = new[] { CreateIdentifierAttribute() }
        };

        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);
        var result = resolver.Resolve(EntityDocumentMapper.MapContext.Create(moduleName, entityName, document, path));

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(context.Options.MissingSchemaFallbackSchema?.Value, result.Result.Value.Value);
        var warning = Assert.Single(warnings);
        Assert.Contains("missing schema", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path.Property("schema").ToString(), warning);
    }

    [Fact]
    public void DuplicateWarningEmitter_ShouldReportDuplicates_WhenAllowancesEnabled()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings, allowDuplicateAttributeLogicalNames: true, allowDuplicateAttributeColumnNames: true);
        var emitter = new EntityDocumentMapper.DuplicateWarningEmitter(context);
        var moduleName = ModuleName.Create("Finance").Value;
        var entityName = EntityName.Create("Invoice").Value;
        var document = new EntityDocument
        {
            Name = entityName.Value,
            PhysicalName = "OSUSR_FIN_INVOICE",
            Schema = "dbo",
            Attributes = new[] { CreateIdentifierAttribute() }
        };
        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);

        var duplicateLogical = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID_SECONDARY").Value,
            dataType: "Identifier",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;
        var duplicateColumn = AttributeModel.Create(
            AttributeName.Create("LegacyId").Value,
            ColumnName.Create("id").Value,
            dataType: "Identifier",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;

        var attributes = ImmutableArray.Create(
            AttributeModel.Create(
                AttributeName.Create("Id").Value,
                ColumnName.Create("ID").Value,
                dataType: "Identifier",
                isMandatory: true,
                isIdentifier: true,
                isAutoNumber: true,
                isActive: true).Value,
            duplicateLogical,
            duplicateColumn);

        var result = emitter.EmitWarnings(EntityDocumentMapper.MapContext.Create(moduleName, entityName, document, path), attributes);

        Assert.True(result.Result.IsSuccess);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("duplicate attribute logical name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("duplicate attribute column name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrimaryKeyValidator_ShouldFail_WhenNoIdentifierAndOverrideMissing()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings);
        var validator = new EntityDocumentMapper.PrimaryKeyValidator(context);
        var moduleName = ModuleName.Create("Finance").Value;
        var entityName = EntityName.Create("Invoice").Value;
        var document = new EntityDocument
        {
            Name = entityName.Value,
            PhysicalName = "OSUSR_FIN_INVOICE",
            Schema = "dbo",
            Attributes = Array.Empty<AttributeDocument>()
        };
        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);

        var attributes = ImmutableArray.Create(
            AttributeModel.Create(
                AttributeName.Create("Legacy").Value,
                ColumnName.Create("LEGACY_ID").Value,
                dataType: "Identifier",
                isMandatory: false,
                isIdentifier: false,
                isAutoNumber: false,
                isActive: true).Value);

        var result = validator.Validate(EntityDocumentMapper.MapContext.Create(moduleName, entityName, document, path), attributes);

        Assert.True(result.Result.IsFailure);
        var error = Assert.Single(result.Result.Errors);
        Assert.Equal("entity.attributes.missingPrimaryKey", error.Code);
        Assert.Empty(warnings);
    }

    [Fact]
    public void PrimaryKeyValidator_ShouldWarn_WhenOverrideAllowsMissingPrimaryKey()
    {
        var warnings = new List<string>();
        var overridesResult = ModuleValidationOverrides.Create(new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            ["Finance"] = new ModuleValidationOverrideConfiguration(
                Array.Empty<string>(),
                true,
                Array.Empty<string>(),
                false)
        });
        Assert.True(overridesResult.IsSuccess);

        var context = CreateContext(warnings, overridesResult.Value);
        var validator = new EntityDocumentMapper.PrimaryKeyValidator(context);
        var moduleName = ModuleName.Create("Finance").Value;
        var entityName = EntityName.Create("Invoice").Value;
        var document = new EntityDocument
        {
            Name = entityName.Value,
            PhysicalName = "OSUSR_FIN_INVOICE",
            Schema = "dbo",
            Attributes = Array.Empty<AttributeDocument>()
        };
        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);

        var attributes = ImmutableArray.Create(
            AttributeModel.Create(
                AttributeName.Create("Legacy").Value,
                ColumnName.Create("LEGACY_ID").Value,
                dataType: "Identifier",
                isMandatory: false,
                isIdentifier: false,
                isAutoNumber: false,
                isActive: true).Value);

        var result = validator.Validate(EntityDocumentMapper.MapContext.Create(moduleName, entityName, document, path), attributes);

        Assert.True(result.Result.IsSuccess);
        Assert.True(result.Result.Value);
        var warning = Assert.Single(warnings);
        Assert.Contains("missing primary key", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_ShouldWarnAndSucceed_WhenDuplicateColumnNamesAllowed()
    {
        var warnings = new List<string>();
        var context = CreateContext(warnings, allowDuplicateAttributeColumnNames: true);
        var mapper = CreateEntityMapper(context);

        var duplicateAttribute = new AttributeDocument
        {
            Name = "Legacy",
            PhysicalName = "id",
            DataType = "Identifier",
            IsMandatory = false,
            IsIdentifier = false,
            IsAutoNumber = false,
            IsActive = true,
            ExtendedProperties = Array.Empty<ModelJsonDeserializer.ExtendedPropertyDocument>()
        };

        var document = new EntityDocument
        {
            Name = "Invoice",
            PhysicalName = "OSUSR_FIN_INVOICE",
            Schema = "dbo",
            IsActive = true,
            Attributes = new[] { CreateIdentifierAttribute(), duplicateAttribute }
        };

        var path = DocumentPathContext.Root.Property("modules").Index(0).Property("entities").Index(0);
        var result = mapper.Map(ModuleName.Create("Finance").Value, document, path);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(warnings, w => w.Contains("duplicate attribute column name 'id'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("shared by attributes", warning);
        Assert.Contains("Path: $['modules'][0]['entities'][0]['attributes']", warning);
    }

    private static EntityDocumentMapper CreateEntityMapper(DocumentMapperContext context)
    {
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var indexMapper = new IndexDocumentMapper(context, extendedPropertyMapper);
        var relationshipMapper = new RelationshipDocumentMapper(context);
        var triggerMapper = new TriggerDocumentMapper(context);
        var temporalMetadataMapper = new TemporalMetadataMapper(context, extendedPropertyMapper);
        return new EntityDocumentMapper(
            context,
            attributeMapper,
            extendedPropertyMapper,
            indexMapper,
            relationshipMapper,
            triggerMapper,
            temporalMetadataMapper);
    }
}
