using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class AttributeJsonResultSetProcessor : ResultSetProcessor<OutsystemsAttributeJsonRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> AttributesJsonRequired = Column.String(1, "AttributesJson");
    private static readonly ColumnDefinition<string?> AttributesJsonOptional = Column.StringOrNull(1, "AttributesJson");

    public AttributeJsonResultSetProcessor(
        MetadataContractOverrides contractOverrides,
        ILogger<AttributeJsonResultSetProcessor>? logger = null)
        : base(CreateDescriptor(
            contractOverrides ?? throw new ArgumentNullException(nameof(contractOverrides)),
            logger ?? NullLogger<AttributeJsonResultSetProcessor>.Instance))
    {
    }

    private static ResultSetDescriptor<OutsystemsAttributeJsonRow> CreateDescriptor(
        MetadataContractOverrides contractOverrides,
        ILogger<AttributeJsonResultSetProcessor> logger)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        var allowNull = contractOverrides.IsColumnOptional("AttributeJson", "AttributesJson");
        IResultSetColumn column = allowNull
            ? (IResultSetColumn)AttributesJsonOptional
            : AttributesJsonRequired;
        var reader = ResultSetReader<OutsystemsAttributeJsonRow>.Create(row => MapRow(row, allowNull, logger));

        return ResultSetDescriptorFactory.Create<OutsystemsAttributeJsonRow>(
            "AttributeJson",
            order: 18,
            builder => builder
                .Columns(EntityId, column)
                .Reader(_ => reader)
                .Assign(static (accumulator, rows) => accumulator.SetAttributeJson(rows)));
    }

    private static OutsystemsAttributeJsonRow MapRow(
        DbRow row,
        bool allowNull,
        ILogger<AttributeJsonResultSetProcessor> logger)
    {
        var entityId = EntityId.Read(row);
        if (!allowNull)
        {
            var requiredValue = AttributesJsonRequired.Read(row);
            return new OutsystemsAttributeJsonRow(entityId, requiredValue);
        }

        var optionalValue = AttributesJsonOptional.Read(row);
        if (optionalValue is null)
        {
            logger.LogDebug(
                "AttributeJson result set row {RowIndex} returned NULL for AttributesJson and was accepted due to contract overrides.",
                row.RowIndex);
        }

        return new OutsystemsAttributeJsonRow(entityId, optionalValue);
    }
}
