using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class AttributeJsonResultSetProcessor : ResultSetProcessor<OutsystemsAttributeJsonRow>
{
    private static readonly ColumnDefinition<int> EntityId = Column.Int32(0, "EntityId");
    private static readonly ColumnDefinition<string> AttributesJsonRequired = Column.String(1, "AttributesJson");
    private static readonly ColumnDefinition<string?> AttributesJsonOptional = Column.StringOrNull(1, "AttributesJson");

    private readonly MetadataContractOverrides _contractOverrides;
    private readonly ILogger<AttributeJsonResultSetProcessor> _logger;

    public AttributeJsonResultSetProcessor(
        MetadataContractOverrides contractOverrides,
        ILogger<AttributeJsonResultSetProcessor>? logger = null)
        : base("AttributeJson", order: 18)
    {
        _contractOverrides = contractOverrides ?? throw new ArgumentNullException(nameof(contractOverrides));
        _logger = logger ?? NullLogger<AttributeJsonResultSetProcessor>.Instance;
    }

    protected override ResultSetReader<OutsystemsAttributeJsonRow> CreateReader(ResultSetProcessingContext context)
    {
        var allowNull = _contractOverrides.IsColumnOptional("AttributeJson", "AttributesJson");
        return ResultSetReader<OutsystemsAttributeJsonRow>.Create(row => MapRow(row, allowNull));
    }

    protected override void Assign(MetadataAccumulator accumulator, List<OutsystemsAttributeJsonRow> rows)
        => accumulator.SetAttributeJson(rows);

    private OutsystemsAttributeJsonRow MapRow(DbRow row, bool allowNull)
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
            _logger.LogDebug(
                "AttributeJson result set row {RowIndex} returned NULL for AttributesJson and was accepted due to contract overrides.",
                row.RowIndex);
        }

        return new OutsystemsAttributeJsonRow(entityId, optionalValue);
    }
}
