using System.Collections.Generic;
using System.Linq;
using System.Text;
using Osm.Domain.Model;
using Osm.Json;
using Tests.Support;

namespace Osm.Json.Tests;

public class ModelJsonDeserializerTests
{
    private static MemoryStream ToStream(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Deserialize_ShouldProduceDomainModel_ForRichPayload()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "extendedProperties": [
            { "name": "ci:owner", "value": "DataTeam" }
          ],
          "sequences": [
            {
              "schema": "dbo",
              "name": "SEQ_INVOICE",
              "dataType": "bigint",
              "startValue": 1,
              "increment": 1,
              "minValue": 1,
              "maxValue": null,
              "cycle": false,
              "cacheMode": "cache",
              "cacheSize": 50,
              "extendedProperties": [
                { "name": "sequence.owner", "value": "Finance" }
              ]
            }
          ],
          "modules": [
            {
              "name": "Finance",
              "isSystem": false,
              "isActive": true,
              "extendedProperties": [
                { "name": "module.note", "value": "Finance data" }
              ],
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true,
                      "extendedProperties": [
                        { "name": "attr.note", "value": "Primary" }
                      ]
                    },
                    {
                      "name": "CustomerId",
                      "physicalName": "CUSTOMERID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": false,
                      "isAutoNumber": false,
                      "isReference": 1,
                      "refEntityId": 100,
                      "refEntity_name": "Customer",
                      "refEntity_physicalName": "OSUSR_FIN_CUSTOMER",
                      "reference_deleteRuleCode": "Protect",
                      "reference_hasDbConstraint": 1,
                      "isActive": true,
                      "extendedProperties": [
                        { "name": "attr.note", "value": "FK" }
                      ]
                    },
                    {
                      "name": "LegacyCode",
                      "physicalName": "LEGACYCODE",
                      "dataType": "Text",
                      "length": 50,
                      "default": null,
                      "isMandatory": false,
                      "isIdentifier": false,
                      "isAutoNumber": false,
                      "isActive": false,
                      "physical_isPresentButInactive": 1,
                      "extendedProperties": [
                        { "name": "attr.note", "value": "Legacy" }
                      ]
                    }
                  ],
                  "meta": { "description": "Invoice table" },
                  "extendedProperties": [
                    { "name": "entity.note", "value": "Invoices" }
                  ],
                  "temporal": {
                    "type": "systemVersioned",
                    "historyTable": { "schema": "dbo", "name": "OSUSR_FIN_INVOICE_HISTORY" },
                    "periodStartColumn": "ValidFrom",
                    "periodEndColumn": "ValidTo",
                    "retention": { "kind": "limited", "unit": "days", "value": 30 },
                    "extendedProperties": [
                      { "name": "temporal.note", "value": "History enabled" }
                    ]
                  },
                  "relationships": [
                    {
                      "viaAttributeName": "CustomerId",
                      "toEntity_name": "Customer",
                      "toEntity_physicalName": "OSUSR_FIN_CUSTOMER",
                      "deleteRuleCode": "Protect",
                      "hasDbConstraint": 1
                    }
                  ],
                  "indexes": [
                    {
                      "name": "IDX_INVOICE_NUMBER",
                      "isUnique": true,
                      "isPrimary": false,
                      "isPlatformAuto": 0,
                      "kind": "IX",
                      "isDisabled": false,
                      "isPadded": true,
                      "fillFactor": 90,
                      "ignoreDupKey": true,
                      "allowRowLocks": true,
                      "allowPageLocks": false,
                      "noRecompute": false,
                      "filterDefinition": "[ID] IS NOT NULL",
                      "dataSpace": { "name": "PS_Invoices", "type": "PARTITION_SCHEME" },
                      "partitionColumns": [
                        { "ordinal": 1, "name": "BILLINGDATE" }
                      ],
                      "dataCompression": [
                        { "partition": 1, "compression": "PAGE" },
                        { "partition": 2, "compression": "ROW" }
                      ],
                      "columns": [
                        { "attribute": "Id", "physicalColumn": "ID", "ordinal": 1 }
                      ],
                      "extendedProperties": [
                        { "name": "index.note", "value": "Partitioned" }
                      ]
                    }
                  ],
                  "triggers": [
                    {
                      "name": "TR_OSUSR_FIN_INVOICE_AUDIT",
                      "isDisabled": false,
                      "definition": "CREATE TRIGGER [dbo].[TR_OSUSR_FIN_INVOICE_AUDIT] ON [dbo].[OSUSR_FIN_INVOICE] AFTER INSERT AS BEGIN SET NOCOUNT ON; END"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        var modelProperty = Assert.Single(result.Value.ExtendedProperties);
        Assert.Equal("ci:owner", modelProperty.Name);
        Assert.Equal("DataTeam", modelProperty.Value);

        var sequence = Assert.Single(result.Value.Sequences);
        Assert.Equal("dbo", sequence.Schema.Value);
        Assert.Equal("SEQ_INVOICE", sequence.Name.Value);
        Assert.Equal("bigint", sequence.DataType);
        Assert.Equal(SequenceCacheMode.Cache, sequence.CacheMode);
        Assert.Equal(50, sequence.CacheSize);

        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("Finance", module.Name.Value);
        Assert.True(module.IsActive);
        Assert.False(module.IsSystemModule);
        Assert.Equal("Finance data", Assert.Single(module.ExtendedProperties).Value);

        var entity = Assert.Single(module.Entities);
        Assert.Equal("Invoice", entity.LogicalName.Value);
        Assert.Equal("OSUSR_FIN_INVOICE", entity.PhysicalName.Value);
        Assert.False(entity.IsStatic);
        Assert.False(entity.IsExternal);
        Assert.True(entity.IsActive);
        Assert.Equal("dbo", entity.Schema.Value);
        Assert.Equal("Invoice table", entity.Metadata.Description);
        Assert.Equal("Invoices", Assert.Single(entity.Metadata.ExtendedProperties).Value);
        Assert.Equal(TemporalTableType.SystemVersioned, entity.Metadata.Temporal.Type);
        Assert.Equal("dbo", entity.Metadata.Temporal.HistorySchema?.Value);
        Assert.Equal("OSUSR_FIN_INVOICE_HISTORY", entity.Metadata.Temporal.HistoryTable?.Value);
        Assert.Equal("ValidFrom", entity.Metadata.Temporal.PeriodStartColumn?.Value);
        Assert.Equal("ValidTo", entity.Metadata.Temporal.PeriodEndColumn?.Value);
        Assert.Equal(TemporalRetentionKind.Limited, entity.Metadata.Temporal.RetentionPolicy.Kind);
        Assert.Equal(TemporalRetentionUnit.Days, entity.Metadata.Temporal.RetentionPolicy.Unit);
        Assert.Equal(30, entity.Metadata.Temporal.RetentionPolicy.Value);

        var attributes = entity.Attributes;
        Assert.Equal(3, attributes.Length);
        var legacy = attributes.Single(a => a.LogicalName.Value == "LegacyCode");
        Assert.False(legacy.IsActive);
        Assert.True(legacy.Reality.IsPresentButInactive);
        Assert.Equal("Legacy", Assert.Single(legacy.Metadata.ExtendedProperties).Value);

        var reference = attributes.Single(a => a.LogicalName.Value == "CustomerId").Reference;
        Assert.True(reference.IsReference);
        Assert.Equal("Customer", reference.TargetEntity?.Value);
        Assert.Equal("Protect", reference.DeleteRuleCode);
        Assert.True(reference.HasDatabaseConstraint);

        var index = Assert.Single(entity.Indexes);
        Assert.Equal("IDX_INVOICE_NUMBER", index.Name.Value);
        Assert.True(index.IsUnique);
        Assert.False(index.IsPlatformAuto);
        Assert.Equal("Partitioned", Assert.Single(index.ExtendedProperties).Value);
        var indexColumn = Assert.Single(index.Columns);
        Assert.Equal(1, indexColumn.Ordinal);
        Assert.True(index.OnDisk.IsPadded);
        Assert.Equal(90, index.OnDisk.FillFactor);
        Assert.True(index.OnDisk.IgnoreDuplicateKey);
        Assert.True(index.OnDisk.AllowRowLocks);
        Assert.False(index.OnDisk.AllowPageLocks);
        Assert.Equal("[ID] IS NOT NULL", index.OnDisk.FilterDefinition);
        var dataSpace = index.OnDisk.DataSpace;
        Assert.NotNull(dataSpace);
        Assert.Equal("PS_Invoices", dataSpace!.Name);
        Assert.Equal("PARTITION_SCHEME", dataSpace.Type);
        var partitionColumn = Assert.Single(index.OnDisk.PartitionColumns);
        Assert.Equal("BILLINGDATE", partitionColumn.Column.Value);
        Assert.Contains(index.OnDisk.DataCompression, c => c.PartitionNumber == 1 && c.Compression == "PAGE");
        Assert.Contains(index.OnDisk.DataCompression, c => c.PartitionNumber == 2 && c.Compression == "ROW");

        var trigger = Assert.Single(entity.Triggers);
        Assert.Equal("TR_OSUSR_FIN_INVOICE_AUDIT", trigger.Name.Value);
        Assert.False(trigger.IsDisabled);
        Assert.Contains("CREATE TRIGGER", trigger.Definition);

        var relationship = Assert.Single(entity.Relationships);
        Assert.Equal("Customer", relationship.TargetEntity.Value);
        Assert.Equal("CustomerId", relationship.ViaAttribute.Value);
        Assert.True(relationship.HasDatabaseConstraint);
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenModuleNameMissing()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "entities": []
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.schema.validation" && e.Message.Contains("\"name\""));
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenAttributePhysicalNameMissing()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "Id",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.schema.validation" && e.Message.Contains("\"physicalName\""));
    }

    [Fact]
    public void Deserialize_ShouldWarnAndSkipModulesWithoutEntities()
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
              "name": "Orders",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Order",
                  "physicalName": "OSUSR_ORD_ORDER",
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

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);
        var warnings = new List<string>();

        var result = deserializer.Deserialize(stream, warnings);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("Orders", module.Name.Value);
        Assert.Collection(warnings, warning => Assert.Contains("EmptyModule", warning));
    }

    [Fact]
    public void Deserialize_ShouldLoadEdgeCaseFixture()
    {
        using var stream = FixtureFile.OpenStream("model.edge-case.json");
        var deserializer = new ModelJsonDeserializer();

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        var appCore = Assert.Single(result.Value.Modules.Where(m => m.Name.Value == "AppCore"));
        var customer = Assert.Single(appCore.Entities.Where(e => e.LogicalName.Value == "Customer"));
        Assert.Equal(6, customer.Attributes.Length);
        var legacy = customer.Attributes.Single(a => a.LogicalName.Value == "LegacyCode");
        Assert.False(legacy.IsActive);
        Assert.True(legacy.Reality.IsPresentButInactive);
        var cityId = customer.Attributes.Single(a => a.LogicalName.Value == "CityId");
        Assert.Equal("Protect", cityId.Reference.DeleteRuleCode);
        Assert.True(cityId.Reference.HasDatabaseConstraint);
        var jobRun = result.Value.Modules
            .Single(m => m.Name.Value == "Ops")
            .Entities.Single(e => e.LogicalName.Value == "JobRun");
        var relationship = jobRun.Relationships.Single();
        Assert.Equal("Ignore", relationship.DeleteRuleCode);
        Assert.False(relationship.HasDatabaseConstraint);
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenAttributesCollectionMissing()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo"
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.schema.validation" && e.Message.Contains("\"attributes\""));
    }

    [Fact]
    public void Deserialize_ShouldSkipInactiveEntitiesMissingAttributes()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "db_schema": "dbo",
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
                  ]
                },
                {
                  "name": "Legacy",
                  "physicalName": "OSUSR_FIN_LEGACY",
                  "db_schema": "dbo",
                  "isActive": false,
                  "attributes": []
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Message)));
        var module = Assert.Single(result.Value.Modules);
        var entity = Assert.Single(module.Entities);
        Assert.Equal("Invoice", entity.LogicalName.Value);
    }

    [Fact]
    public void Deserialize_ShouldSkipInactiveModuleWhenAllEntitiesAreInactiveWithoutAttributes()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Common",
              "isActive": false,
              "entities": [
                {
                  "name": "AuditLog",
                  "physicalName": "OSUSR_COM_AUDIT",
                  "db_schema": "dbo",
                  "isActive": false,
                  "attributes": []
                }
              ]
            },
            {
              "name": "Finance",
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "db_schema": "dbo",
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
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("Finance", module.Name.Value);
        var entity = Assert.Single(module.Entities);
        Assert.Equal("Invoice", entity.LogicalName.Value);
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenReferenceAttributeMissingTargetLogicalName()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "CustomerId",
                      "physicalName": "CUSTOMERID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": false,
                      "isAutoNumber": false,
                      "isReference": 1,
                      "isActive": true,
                      "refEntity_physicalName": "OSUSR_FIN_CUSTOMER"
                    },
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.reference.target.missing");
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenReferenceAttributeMissingTargetPhysicalName()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "CustomerId",
                      "physicalName": "CUSTOMERID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": false,
                      "isAutoNumber": false,
                      "isReference": 1,
                      "isActive": true,
                      "refEntity_name": "Customer"
                    },
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "attribute.reference.physical.missing");
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenDuplicateAttributeColumnsDetected()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo",
                  "attributes": [
                    {
                      "name": "Id",
                      "physicalName": "ID",
                      "dataType": "Identifier",
                      "isMandatory": true,
                      "isIdentifier": true,
                      "isAutoNumber": true,
                      "isActive": true
                    },
                    {
                      "name": "Legacy",
                      "physicalName": "id",
                      "dataType": "Identifier",
                      "isMandatory": false,
                      "isIdentifier": false,
                      "isAutoNumber": false,
                      "isActive": true
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "entity.attributes.duplicateColumn");
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenDuplicateEntityPhysicalNames()
    {
        const string json = """
        {
          "exportedAtUtc": "2025-01-01T00:00:00Z",
          "modules": [
            {
              "name": "Finance",
              "isSystem": false,
              "isActive": true,
              "entities": [
                {
                  "name": "Invoice",
                  "physicalName": "OSUSR_FIN_INVOICE",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo",
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
                  ]
                },
                {
                  "name": "InvoiceShadow",
                  "physicalName": "osusr_fin_invoice",
                  "isStatic": false,
                  "isExternal": false,
                  "isActive": true,
                  "db_schema": "dbo",
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
                  ]
                }
              ]
            }
          ]
        }
        """;

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "module.entities.duplicatePhysical");
    }

    [Fact]
    public void Deserialize_ShouldFail_WhenJsonMalformed()
    {
        const string json = "{";

        var deserializer = new ModelJsonDeserializer();
        using var stream = ToStream(json);

        var result = deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "json.parse.failed");
    }
}
