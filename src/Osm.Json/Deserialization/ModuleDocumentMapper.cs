using System.Collections.Generic;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Json.Deserialization;

using ModuleDocument = ModelJsonDeserializer.ModuleDocument;
using EntityDocument = ModelJsonDeserializer.EntityDocument;

internal sealed class ModuleDocumentMapper
{
    private readonly DocumentMapperContext _context;
    private readonly EntityDocumentMapper _entityMapper;
    private readonly ExtendedPropertyDocumentMapper _extendedPropertyMapper;

    public ModuleDocumentMapper(
        DocumentMapperContext context,
        EntityDocumentMapper entityMapper,
        ExtendedPropertyDocumentMapper extendedPropertyMapper)
    {
        _context = context;
        _entityMapper = entityMapper;
        _extendedPropertyMapper = extendedPropertyMapper;
    }

    public bool ShouldSkipInactiveModule(ModuleDocument doc)
    {
        if (doc is null || doc.IsActive)
        {
            return false;
        }

        var entities = doc.Entities;
        if (entities is null || entities.Length == 0)
        {
            return true;
        }

        foreach (var entity in entities)
        {
            if (!ShouldSkipInactiveEntity(entity))
            {
                return false;
            }
        }

        return true;
    }

    public Result<ModuleModel?> Map(ModuleDocument doc, ModuleName moduleName)
    {
        var entities = doc.Entities ?? Array.Empty<EntityDocument>();
        var entityResults = new List<EntityModel>(entities.Length);
        foreach (var entity in entities)
        {
            if (ShouldSkipInactiveEntity(entity))
            {
                continue;
            }

            var entityResult = _entityMapper.Map(moduleName, entity);
            if (entityResult.IsFailure)
            {
                return Result<ModuleModel?>.Failure(entityResult.Errors);
            }

            entityResults.Add(entityResult.Value);
        }

        if (entityResults.Count == 0)
        {
            _context.AddWarning($"Module '{moduleName.Value}' contains no entities and will be skipped.");
            return Result<ModuleModel?>.Success(null);
        }

        var propertiesResult = _extendedPropertyMapper.Map(doc.ExtendedProperties);
        if (propertiesResult.IsFailure)
        {
            return Result<ModuleModel?>.Failure(propertiesResult.Errors);
        }

        var moduleResult = ModuleModel.Create(moduleName, doc.IsSystem, doc.IsActive, entityResults, propertiesResult.Value);
        if (moduleResult.IsFailure)
        {
            return Result<ModuleModel?>.Failure(moduleResult.Errors);
        }

        return Result<ModuleModel?>.Success(moduleResult.Value);
    }

    private static bool ShouldSkipInactiveEntity(EntityDocument doc)
    {
        if (doc is null)
        {
            return false;
        }

        if (doc.IsActive)
        {
            return false;
        }

        var attributes = doc.Attributes;
        return attributes is null || attributes.Length == 0;
    }
}
