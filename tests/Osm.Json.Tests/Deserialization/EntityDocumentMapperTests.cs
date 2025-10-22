using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Domain.Configuration;
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
        bool allowDuplicateAttributeLogicalNames = false)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var options = new ModelJsonDeserializerOptions(
            overrides,
            missingSchemaFallback: null,
            allowDuplicateAttributeLogicalNames: allowDuplicateAttributeLogicalNames);
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
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var mapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);

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
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var mapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);

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
        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var mapper = new EntityDocumentMapper(context, attributeMapper, extendedPropertyMapper);

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
        Assert.Contains("Path: $['modules'][0]['entities'][0]['attributes']", warning);
    }
}
