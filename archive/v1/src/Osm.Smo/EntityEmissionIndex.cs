using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Smo;

internal sealed class EntityEmissionIndex
{
    private readonly IReadOnlyDictionary<string, EntityEmissionContext> _primaryBySchemaAndTable;
    private readonly IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> _primaryByPhysicalName;
    private readonly IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> _primaryByLogicalName;
    private readonly IReadOnlyDictionary<string, EntityEmissionContext> _supplementalBySchemaAndTable;
    private readonly IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> _supplementalByPhysicalName;
    private readonly IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> _supplementalByLogicalName;

    public EntityEmissionIndex(
        IReadOnlyDictionary<string, EntityEmissionContext> primaryBySchemaAndTable,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> primaryByPhysicalName,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> primaryByLogicalName,
        IReadOnlyDictionary<string, EntityEmissionContext> supplementalBySchemaAndTable,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> supplementalByPhysicalName,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> supplementalByLogicalName)
    {
        _primaryBySchemaAndTable = primaryBySchemaAndTable ?? throw new ArgumentNullException(nameof(primaryBySchemaAndTable));
        _primaryByPhysicalName = primaryByPhysicalName ?? throw new ArgumentNullException(nameof(primaryByPhysicalName));
        _primaryByLogicalName = primaryByLogicalName ?? throw new ArgumentNullException(nameof(primaryByLogicalName));
        _supplementalBySchemaAndTable = supplementalBySchemaAndTable ?? throw new ArgumentNullException(nameof(supplementalBySchemaAndTable));
        _supplementalByPhysicalName = supplementalByPhysicalName ?? throw new ArgumentNullException(nameof(supplementalByPhysicalName));
        _supplementalByLogicalName = supplementalByLogicalName ?? throw new ArgumentNullException(nameof(supplementalByLogicalName));
    }

    public int Count => _primaryBySchemaAndTable.Count;

    public ImmutableArray<EntityEmissionContext> GetSupplementalContexts()
    {
        if (_supplementalBySchemaAndTable.Count == 0)
        {
            return ImmutableArray<EntityEmissionContext>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<EntityEmissionContext>(_supplementalBySchemaAndTable.Count);
        foreach (var context in _supplementalBySchemaAndTable.Values)
        {
            if (context is not null)
            {
                builder.Add(context);
            }
        }

        return builder.ToImmutable();
    }

    public EntityEmissionContext GetContext(EntityModel entity)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var key = SchemaTableKey(entity.Schema.Value, entity.PhysicalName.Value);
        if (_primaryBySchemaAndTable.TryGetValue(key, out var context))
        {
            return context;
        }

        throw new InvalidOperationException($"Emission context not found for entity '{entity.Module.Value}.{entity.LogicalName.Value}'.");
    }

    public bool TryResolveReference(AttributeReference reference, EntityEmissionContext source, out EntityEmissionContext context)
    {
        if (reference is null)
        {
            context = default!;
            return false;
        }

        if (reference.TargetPhysicalName is not null &&
            TryGetByPhysical(reference.TargetPhysicalName.Value.Value, source.Entity.Schema.Value, out context))
        {
            return true;
        }

        if (reference.TargetEntity is not null &&
            TryGetByLogical(reference.TargetEntity.Value.Value, source.ModuleName, source.Entity.Schema.Value, out context))
        {
            return true;
        }

        context = default!;
        return false;
    }

    private bool TryGetByPhysical(string tableName, string preferredSchema, out EntityEmissionContext context)
    {
        if (TryGetByPhysical(tableName, preferredSchema, _primaryByPhysicalName, _primaryBySchemaAndTable, out context))
        {
            return true;
        }

        if (TryGetByPhysical(tableName, preferredSchema, _supplementalByPhysicalName, _supplementalBySchemaAndTable, out context))
        {
            return true;
        }

        context = default!;
        return false;
    }

    private bool TryGetByLogical(string logicalName, string moduleName, string preferredSchema, out EntityEmissionContext context)
    {
        if (TryGetByLogical(logicalName, moduleName, preferredSchema, _primaryByLogicalName, out context))
        {
            return true;
        }

        if (TryGetByLogical(logicalName, moduleName, preferredSchema, _supplementalByLogicalName, out context))
        {
            return true;
        }

        context = default!;
        return false;
    }

    private static bool TryGetByPhysical(
        string tableName,
        string preferredSchema,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> lookup,
        IReadOnlyDictionary<string, EntityEmissionContext> schemaLookup,
        out EntityEmissionContext context)
    {
        if (lookup.TryGetValue(tableName, out var contexts) && !contexts.IsDefaultOrEmpty)
        {
            if (contexts.Length == 1)
            {
                context = contexts[0];
                return true;
            }

            var schemaMatch = contexts.FirstOrDefault(c =>
                string.Equals(c.Entity.Schema.Value, preferredSchema, StringComparison.OrdinalIgnoreCase));
            if (schemaMatch is not null)
            {
                context = schemaMatch;
                return true;
            }

            context = contexts[0];
            return true;
        }

        var schemaKey = SchemaTableKey(preferredSchema, tableName);
        if (schemaLookup.TryGetValue(schemaKey, out var candidate) && candidate is not null)
        {
            context = candidate;
            return true;
        }

        context = default!;
        return false;
    }

    private static bool TryGetByLogical(
        string logicalName,
        string moduleName,
        string preferredSchema,
        IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> lookup,
        out EntityEmissionContext context)
    {
        if (lookup.TryGetValue(logicalName, out var contexts) && !contexts.IsDefaultOrEmpty)
        {
            if (contexts.Length == 1)
            {
                context = contexts[0];
                return true;
            }

            var moduleMatch = contexts.FirstOrDefault(c =>
                string.Equals(c.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
            if (moduleMatch is not null)
            {
                context = moduleMatch;
                return true;
            }

            var schemaMatch = contexts.FirstOrDefault(c =>
                string.Equals(c.Entity.Schema.Value, preferredSchema, StringComparison.OrdinalIgnoreCase));
            if (schemaMatch is not null)
            {
                context = schemaMatch;
                return true;
            }

            context = contexts[0];
            return true;
        }

        context = default!;
        return false;
    }

    private static string SchemaTableKey(string schema, string table)
        => $"{schema}.{table}";
}
