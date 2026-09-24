using System;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Json.Deserialization;

internal sealed class EntitySchemaResolver : IEntitySchemaResolver
{
    private readonly DocumentMapperContext _context;

    public EntitySchemaResolver(DocumentMapperContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public EntityDocumentMapper.HelperResult<SchemaName> Resolve(EntityDocumentMapper.MapContext mapContext)
    {
        var options = _context.Options;
        var document = mapContext.Document;
        var moduleName = mapContext.ModuleNameValue;
        var entityName = mapContext.EntityNameValue;
        var allowMissingSchema = options.ValidationOverrides.AllowsMissingSchema(moduleName, entityName);

        if (string.IsNullOrWhiteSpace(document.Schema))
        {
            if (!allowMissingSchema)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                return EntityDocumentMapper.HelperResult<SchemaName>.Failure(
                    mapContext,
                    _context.CreateError(
                        "entity.schema.missing",
                        $"Entity '{moduleName}::{entityName}' is missing a schema name. Raw payload: {mapContext.SerializedPayload}",
                        mapContext.SchemaPath));
            }

            var fallbackSchemaResult = options.MissingSchemaFallbackSchemaResult;
            if (fallbackSchemaResult.IsFailure)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                var errorsWithPath = _context.WithPath(mapContext.SchemaPath, fallbackSchemaResult.Errors);
                return EntityDocumentMapper.HelperResult<SchemaName>.Failure(
                    mapContext,
                    AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
            }

            mapContext = mapContext.EnsureSerializedPayload(_context);
            _context.AddWarning(
                $"Entity '{moduleName}::{entityName}' missing schema; using '{options.MissingSchemaFallback}'. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.SchemaPath})");
            return EntityDocumentMapper.HelperResult<SchemaName>.Success(mapContext, fallbackSchemaResult.Value);
        }

        var schemaResult = SchemaName.Create(document.Schema);
        if (schemaResult.IsFailure)
        {
            if (!allowMissingSchema)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                var errorsWithPath = _context.WithPath(mapContext.SchemaPath, schemaResult.Errors);
                return EntityDocumentMapper.HelperResult<SchemaName>.Failure(
                    mapContext,
                    AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
            }

            var fallbackSchemaResult = options.MissingSchemaFallbackSchemaResult;
            if (fallbackSchemaResult.IsFailure)
            {
                mapContext = mapContext.EnsureSerializedPayload(_context);
                var errorsWithPath = _context.WithPath(mapContext.SchemaPath, fallbackSchemaResult.Errors);
                return EntityDocumentMapper.HelperResult<SchemaName>.Failure(
                    mapContext,
                    AppendPayloadContext(errorsWithPath, moduleName, entityName, mapContext.SerializedPayload!));
            }

            mapContext = mapContext.EnsureSerializedPayload(_context);
            _context.AddWarning(
                $"Entity '{moduleName}::{entityName}' schema '{document.Schema}' invalid; using '{options.MissingSchemaFallback}'. Raw payload: {mapContext.SerializedPayload} (Path: {mapContext.SchemaPath})");
            return EntityDocumentMapper.HelperResult<SchemaName>.Success(mapContext, fallbackSchemaResult.Value);
        }

        return new EntityDocumentMapper.HelperResult<SchemaName>(schemaResult, mapContext);
    }

    private static ImmutableArray<ValidationError> AppendPayloadContext(
        ImmutableArray<ValidationError> errors,
        string moduleName,
        string entityName,
        string payload)
    {
        if (errors.IsDefaultOrEmpty)
        {
            return ImmutableArray.Create(
                ValidationError.Create(
                    "entity.schema.invalid",
                    $"Entity '{moduleName}::{entityName}' has an invalid schema definition. Raw payload: {payload}"));
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(errors.Length);
        foreach (var error in errors)
        {
            builder.Add(error.WithMessage($"{error.Message} (Entity '{moduleName}::{entityName}' payload: {payload})"));
        }

        return builder.ToImmutable();
    }
}
