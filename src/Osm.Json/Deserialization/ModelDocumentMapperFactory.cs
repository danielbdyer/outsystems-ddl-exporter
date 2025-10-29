using System;

namespace Osm.Json.Deserialization;

internal interface IModelDocumentMapperFactory
{
    ModelDocumentMapperSet Create(DocumentMapperContext context);
}

internal sealed class ModelDocumentMapperFactory : IModelDocumentMapperFactory
{
    public ModelDocumentMapperSet Create(DocumentMapperContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var extendedPropertyMapper = new ExtendedPropertyDocumentMapper(context);
        var attributeMapper = new AttributeDocumentMapper(context, extendedPropertyMapper);
        var indexMapper = new IndexDocumentMapper(context, extendedPropertyMapper);
        var relationshipMapper = new RelationshipDocumentMapper(context);
        var triggerMapper = new TriggerDocumentMapper(context);
        var temporalMetadataMapper = new TemporalMetadataMapper(context, extendedPropertyMapper);
        var schemaResolver = new EntitySchemaResolver(context);
        var metadataFactory = new EntityMetadataFactory();
        var duplicateWarningEmitter = new DuplicateWarningEmitter(context);
        var primaryKeyValidator = new PrimaryKeyValidator(context);
        var entityMapper = new EntityDocumentMapper(
            context,
            attributeMapper,
            extendedPropertyMapper,
            indexMapper,
            relationshipMapper,
            triggerMapper,
            temporalMetadataMapper,
            schemaResolver,
            metadataFactory,
            duplicateWarningEmitter,
            primaryKeyValidator);
        var moduleMapper = new ModuleDocumentMapper(context, entityMapper, extendedPropertyMapper);
        var sequenceMapper = new SequenceDocumentMapper(context, extendedPropertyMapper);

        return new ModelDocumentMapperSet(moduleMapper, sequenceMapper, extendedPropertyMapper);
    }
}

internal sealed class ModelDocumentMapperSet
{
    public ModelDocumentMapperSet(
        ModuleDocumentMapper moduleMapper,
        SequenceDocumentMapper sequenceMapper,
        ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        ModuleMapper = moduleMapper ?? throw new ArgumentNullException(nameof(moduleMapper));
        SequenceMapper = sequenceMapper ?? throw new ArgumentNullException(nameof(sequenceMapper));
        ExtendedPropertyMapper = extendedPropertyMapper ?? throw new ArgumentNullException(nameof(extendedPropertyMapper));
    }

    public ModuleDocumentMapper ModuleMapper { get; }

    public SequenceDocumentMapper SequenceMapper { get; }

    public ExtendedPropertyDocumentMapper ExtendedPropertyMapper { get; }
}
