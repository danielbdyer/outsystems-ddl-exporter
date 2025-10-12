using System;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tests.Policy;

internal static class CrossScopeForeignKeyScenario
{
    public static ForeignKeyScenario Create(string targetSchema, string? targetCatalog = null)
    {
        var orderModule = ModuleName.Create("Orders").Value;
        var customerModule = ModuleName.Create("Accounts").Value;

        var orderSchema = SchemaName.Create("dbo").Value;
        var orderTable = TableName.Create("OSUSR_ORD_ORDER").Value;

        var customerSchema = SchemaName.Create(targetSchema).Value;
        var customerTable = TableName.Create("OSUSR_ACCT_CUSTOMER").Value;

        var orderId = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var reference = AttributeReference.Create(
            isReference: true,
            targetEntityId: 1,
            targetEntity: EntityName.Create("Customer").Value,
            targetPhysicalName: customerTable,
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: false).Value;

        var customerId = AttributeModel.Create(
            AttributeName.Create("CustomerId").Value,
            ColumnName.Create("CUSTOMERID").Value,
            dataType: "Identifier",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: reference).Value;

        var orderEntity = EntityModel.Create(
            orderModule,
            EntityName.Create("Order").Value,
            orderTable,
            orderSchema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { orderId, customerId }).Value;

        var customerIdAttribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var customerEntity = EntityModel.Create(
            customerModule,
            EntityName.Create("Customer").Value,
            customerTable,
            customerSchema,
            targetCatalog,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { customerIdAttribute }).Value;

        var orderModuleModel = ModuleModel.Create(orderModule, isSystemModule: false, isActive: true, new[] { orderEntity }).Value;
        var customerModuleModel = ModuleModel.Create(customerModule, isSystemModule: false, isActive: true, new[] { customerEntity }).Value;

        var model = OsmModel.Create(DateTime.UtcNow, new[] { orderModuleModel, customerModuleModel }).Value;

        var columnProfile = ColumnProfile.Create(
            orderSchema,
            orderTable,
            customerId.ColumnName,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 1_000,
            nullCount: 0).Value;

        var fkReference = ForeignKeyReference.Create(
            orderSchema,
            orderTable,
            customerId.ColumnName,
            customerSchema,
            customerTable,
            customerIdAttribute.ColumnName,
            hasDatabaseConstraint: false).Value;

        var foreignKeyReality = ForeignKeyReality.Create(fkReference, hasOrphan: false, isNoCheck: false).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { columnProfile },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { foreignKeyReality }).Value;

        var coordinate = new ColumnCoordinate(orderSchema, orderTable, customerId.ColumnName);

        return new ForeignKeyScenario(model, snapshot, coordinate);
    }

    public static TighteningOptions CreateOptions(bool allowCrossSchema, bool allowCrossCatalog)
    {
        var defaults = TighteningOptions.Default;
        var policy = PolicyOptions.Create(TighteningMode.EvidenceGated, defaults.Policy.NullBudget).Value;
        var foreignKeys = ForeignKeyOptions.Create(enableCreation: true, allowCrossSchema, allowCrossCatalog).Value;

        return TighteningOptions.Create(
            policy,
            foreignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            defaults.Emission,
            defaults.Mocking).Value;
    }

    internal sealed record ForeignKeyScenario(
        OsmModel Model,
        ProfileSnapshot Snapshot,
        ColumnCoordinate Coordinate);
}
