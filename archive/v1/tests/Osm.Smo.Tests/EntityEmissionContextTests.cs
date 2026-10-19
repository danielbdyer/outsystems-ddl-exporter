using System;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Model.Emission;
using Osm.Domain.ValueObjects;
using Osm.Smo;
using Xunit;

namespace Osm.Smo.Tests;

public class EntityEmissionContextTests
{
    [Fact]
    public void GetPreferredIdentifier_prefers_active_identifier()
    {
        var inactiveIdentifier = EntityEmissionTestData.CreateAttribute(
            logicalName: "LegacyId",
            columnName: "LEGACYID",
            isIdentifier: true,
            presentButInactive: true);
        var activeIdentifier = EntityEmissionTestData.CreateAttribute(
            logicalName: "CustomerId",
            columnName: "CUSTOMERID",
            isIdentifier: true);
        var createdOn = EntityEmissionTestData.CreateAttribute("CreatedOn", "CREATEDON");

        var entity = EntityEmissionTestData.CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "OSUSR_SALES_CUSTOMER",
            schema: "sales",
            inactiveIdentifier,
            activeIdentifier,
            createdOn);

        var snapshot = EntityEmissionSnapshot.Create("Sales", entity);
        var context = EntityEmissionContext.Create("Sales", entity);
        var preferred = context.GetPreferredIdentifier();

        Assert.NotNull(preferred);
        Assert.Equal("CustomerId", preferred!.LogicalName.Value);
        Assert.Contains(
            context.IdentifierAttributes,
            attribute => attribute.LogicalName.Value.Equals("CustomerId", StringComparison.Ordinal));
        Assert.DoesNotContain(
            context.EmittableAttributes,
            attribute => attribute.LogicalName.Value.Equals("LegacyId", StringComparison.Ordinal));
        Assert.True(snapshot.EmittableAttributes.SequenceEqual(context.EmittableAttributes));
        Assert.True(snapshot.IdentifierAttributes.SequenceEqual(context.IdentifierAttributes));
        Assert.Equal(
            snapshot.AttributeLookup.Keys.OrderBy(key => key),
            context.AttributeLookup.Keys.OrderBy(key => key));
    }

    [Fact]
    public void GetPreferredIdentifier_uses_fallback_when_active_missing()
    {
        var inactiveIdentifier = EntityEmissionTestData.CreateAttribute(
            logicalName: "LegacyId",
            columnName: "LEGACYID",
            isIdentifier: true,
            presentButInactive: true);
        var nameAttribute = EntityEmissionTestData.CreateAttribute("Name", "NAME");

        var entity = EntityEmissionTestData.CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "OSUSR_SALES_CUSTOMER",
            schema: "sales",
            inactiveIdentifier,
            nameAttribute);

        var snapshot = EntityEmissionSnapshot.Create("Sales", entity);
        var context = EntityEmissionContext.Create("Sales", entity);
        var preferred = context.GetPreferredIdentifier();

        Assert.NotNull(preferred);
        Assert.Equal("LegacyId", preferred!.LogicalName.Value);
        Assert.True(context.IdentifierAttributes.IsDefaultOrEmpty);
        Assert.Same(snapshot.FallbackIdentifier, preferred);
    }
}

public class EntityEmissionIndexTests
{
    [Fact]
    public void TryResolveReference_prefers_schema_match_for_physical_name()
    {
        var customerId = EntityEmissionTestData.CreateAttribute("CustomerId", "CUSTOMERID", isIdentifier: true);
        var salesCustomer = EntityEmissionTestData.CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "CUSTOMER",
            schema: "sales",
            customerId);

        var sharedCustomer = EntityEmissionTestData.CreateEntity(
            moduleName: "Shared",
            logicalName: "Customer",
            physicalName: "CUSTOMER",
            schema: "dbo",
            EntityEmissionTestData.CreateAttribute("CustomerId", "CUSTOMERID", isIdentifier: true));

        var reference = EntityEmissionTestData.CreateReference("Customer", "CUSTOMER");
        var orderCustomer = EntityEmissionTestData.CreateAttribute(
            logicalName: "CustomerId",
            columnName: "CUSTOMERID",
            reference: reference);
        var order = EntityEmissionTestData.CreateEntity(
            moduleName: "Sales",
            logicalName: "Order",
            physicalName: "ORDER",
            schema: "sales",
            EntityEmissionTestData.CreateAttribute("OrderId", "ORDERID", isIdentifier: true),
            orderCustomer);

        var model = EntityEmissionTestData.CreateModel(
            EntityEmissionTestData.CreateModule("Sales", salesCustomer, order),
            EntityEmissionTestData.CreateModule("Shared", sharedCustomer));

        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var orderContext = contexts.GetContext(order);

        var resolved = contexts.TryResolveReference(orderCustomer.Reference, orderContext, out var targetContext);

        Assert.True(resolved);
        Assert.Equal("sales", targetContext.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Customer", targetContext.Entity.LogicalName.Value);
    }

    [Fact]
    public void TryResolveReference_uses_module_logical_match_when_physical_missing()
    {
        var supportCustomer = EntityEmissionTestData.CreateEntity(
            moduleName: "Support",
            logicalName: "Customer",
            physicalName: "SUPPORT_CUSTOMER",
            schema: "support",
            EntityEmissionTestData.CreateAttribute("CustomerId", "CUSTOMERID", isIdentifier: true));

        var salesCustomer = EntityEmissionTestData.CreateEntity(
            moduleName: "Sales",
            logicalName: "Customer",
            physicalName: "SALES_CUSTOMER",
            schema: "sales",
            EntityEmissionTestData.CreateAttribute("CustomerId", "CUSTOMERID", isIdentifier: true));

        var legacyReference = EntityEmissionTestData.CreateReference("Customer", "LEGACY_CUSTOMER");
        var ticketCustomer = EntityEmissionTestData.CreateAttribute(
            logicalName: "CustomerId",
            columnName: "CUSTOMERID",
            reference: legacyReference);
        var ticket = EntityEmissionTestData.CreateEntity(
            moduleName: "Support",
            logicalName: "Ticket",
            physicalName: "TICKET",
            schema: "support",
            EntityEmissionTestData.CreateAttribute("TicketId", "TICKETID", isIdentifier: true),
            ticketCustomer);

        var model = EntityEmissionTestData.CreateModel(
            EntityEmissionTestData.CreateModule("Support", supportCustomer, ticket),
            EntityEmissionTestData.CreateModule("Sales", salesCustomer));

        var contexts = SmoModelFactory.BuildEntityContexts(model, supplementalEntities: null);
        var ticketContext = contexts.GetContext(ticket);

        var resolved = contexts.TryResolveReference(ticketCustomer.Reference, ticketContext, out var targetContext);

        Assert.True(resolved);
        Assert.Equal("Support", targetContext.ModuleName);
        Assert.Equal("support", targetContext.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase);
    }
}

internal static class EntityEmissionTestData
{
    public static AttributeModel CreateAttribute(
        string logicalName,
        string columnName,
        bool isIdentifier = false,
        bool isActive = true,
        bool presentButInactive = false,
        AttributeReference? reference = null)
    {
        var reality = new AttributeReality(null, null, null, null, presentButInactive);
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: isActive,
            reference: reference,
            reality: reality,
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }

    public static AttributeReference CreateReference(string logicalName, string physicalName)
        => AttributeReference.Create(
            isReference: true,
            targetEntityId: null,
            targetEntity: new EntityName(logicalName),
            targetPhysicalName: new TableName(physicalName),
            deleteRuleCode: null,
            hasDatabaseConstraint: false).Value;

    public static EntityModel CreateEntity(
        string moduleName,
        string logicalName,
        string physicalName,
        string schema,
        params AttributeModel[] attributes)
        => EntityModel.Create(
            new ModuleName(moduleName),
            new EntityName(logicalName),
            new TableName(physicalName),
            new SchemaName(schema),
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes,
            indexes: Array.Empty<IndexModel>(),
            relationships: Array.Empty<RelationshipModel>(),
            triggers: Array.Empty<TriggerModel>(),
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: true).Value;

    public static ModuleModel CreateModule(string name, params EntityModel[] entities)
        => ModuleModel.Create(new ModuleName(name), isSystemModule: false, isActive: true, entities).Value;

    public static OsmModel CreateModel(params ModuleModel[] modules)
        => OsmModel.Create(DateTime.UtcNow, modules).Value;
}
