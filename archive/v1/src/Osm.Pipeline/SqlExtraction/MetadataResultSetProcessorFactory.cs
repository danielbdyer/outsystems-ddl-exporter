using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Osm.Pipeline.SqlExtraction;

internal sealed class MetadataResultSetProcessorFactory : IMetadataResultSetProcessorFactory
{
    public static MetadataResultSetProcessorFactory Default { get; } = new();

    public IReadOnlyList<IResultSetProcessor> Create(MetadataContractOverrides overrides, ILoggerFactory? loggerFactory)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        var attributeLogger = loggerFactory?.CreateLogger<AttributeJsonResultSetProcessor>();

        return new IResultSetProcessor[]
        {
            new ModulesResultSetProcessor(),
            new EntitiesResultSetProcessor(),
            new AttributesResultSetProcessor(),
            new ReferencesResultSetProcessor(),
            new PhysicalTablesResultSetProcessor(),
            new ColumnRealityResultSetProcessor(),
            new ColumnChecksResultSetProcessor(),
            new ColumnCheckJsonResultSetProcessor(),
            new PhysicalColumnsPresentResultSetProcessor(),
            new IndexesResultSetProcessor(),
            new IndexColumnsResultSetProcessor(),
            new ForeignKeysResultSetProcessor(),
            new ForeignKeyColumnsResultSetProcessor(),
            new ForeignKeyAttributeMapResultSetProcessor(),
            new AttributeHasForeignKeyResultSetProcessor(),
            new ForeignKeyColumnsJsonResultSetProcessor(),
            new ForeignKeyAttributeJsonResultSetProcessor(),
            new TriggersResultSetProcessor(),
            new AttributeJsonResultSetProcessor(overrides, attributeLogger),
            new RelationshipJsonResultSetProcessor(),
            new IndexJsonResultSetProcessor(),
            new TriggerJsonResultSetProcessor(),
            new ModuleJsonResultSetProcessor()
        };
    }
}
