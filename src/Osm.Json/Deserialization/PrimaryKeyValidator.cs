using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Json.Deserialization;

internal sealed class PrimaryKeyValidator : IPrimaryKeyValidator
{
    private readonly DocumentMapperContext _context;

    public PrimaryKeyValidator(DocumentMapperContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public EntityDocumentMapper.HelperResult<bool> Validate(
        EntityDocumentMapper.MapContext mapContext,
        ImmutableArray<AttributeModel> attributes)
    {
        var moduleName = mapContext.ModuleNameValue;
        var entityName = mapContext.EntityNameValue;
        var allowMissingPrimaryKey = _context.Options.ValidationOverrides.AllowsMissingPrimaryKey(moduleName, entityName);

        if (attributes.Any(static attribute => attribute.IsIdentifier))
        {
            return EntityDocumentMapper.HelperResult<bool>.Success(mapContext, allowMissingPrimaryKey);
        }

        mapContext = mapContext.EnsureSerializedPayload(_context);
        if (!allowMissingPrimaryKey)
        {
            return EntityDocumentMapper.HelperResult<bool>.Failure(
                mapContext,
                _context.CreateError(
                    "entity.attributes.missingPrimaryKey",
                    $"Entity '{moduleName}::{entityName}' does not define a primary key attribute. Raw payload: {mapContext.SerializedPayload}",
                    mapContext.AttributesPath));
        }

        _context.AddWarning(
            $"Entity '{moduleName}::{entityName}' missing primary key; override applied. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.AttributesPath})");
        return EntityDocumentMapper.HelperResult<bool>.Success(mapContext, allowMissingPrimaryKey);
    }
}
