using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Smo;

public sealed class SmoModelFactory
{
    public SmoModel Create(
        OsmModel model,
        PolicyDecisionSet decisions,
        ProfileSnapshot? profile = null,
        SmoBuildOptions? options = null,
        IEnumerable<EntityModel>? supplementalEntities = null,
        TypeMappingPolicy? typeMappingPolicy = null)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        options ??= SmoBuildOptions.Default;
        var resolvedTypeMapping = typeMappingPolicy ?? TypeMappingPolicy.Default;

        var profileDefaults = BuildProfileDefaults(profile);
        var foreignKeyReality = BuildForeignKeyReality(profile);

        var contexts = BuildEntityContexts(model, supplementalEntities);
        var supplementalContexts = contexts.GetSupplementalContexts();
        var totalTableCapacity = model.Modules.Sum(static m => m.Entities.Length)
            + (supplementalContexts.IsDefaultOrEmpty ? 0 : supplementalContexts.Length);

        var tableBuilder = new SmoTableBuilder(decisions, options, contexts, profileDefaults, foreignKeyReality, resolvedTypeMapping);
        var tablesBuilder = ImmutableArray.CreateBuilder<SmoTableDefinition>(totalTableCapacity);

        var orderedModules = model.Modules
            .OrderBy(static module => module.Name.Value, StringComparer.Ordinal);

        foreach (var module in orderedModules)
        {
            var orderedEntities = module.Entities
                .OrderBy(static entity => entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entity => entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entity => entity.LogicalName.Value, StringComparer.Ordinal);

            foreach (var entity in orderedEntities)
            {
                var context = contexts.GetContext(entity);
                tablesBuilder.Add(tableBuilder.Build(context));
            }
        }

        if (!supplementalContexts.IsDefaultOrEmpty)
        {
            var orderedSupplemental = supplementalContexts
                .OrderBy(static context => context.ModuleName, StringComparer.Ordinal)
                .ThenBy(static context => context.Entity.Schema.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static context => context.Entity.PhysicalName.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static context => context.Entity.LogicalName.Value, StringComparer.Ordinal);

            foreach (var context in orderedSupplemental)
            {
                tablesBuilder.Add(tableBuilder.Build(context));
            }
        }

        var tables = tablesBuilder.ToImmutable();
        if (!tables.IsDefaultOrEmpty)
        {
            tables = tables.Sort(SmoTableBuilder.DefinitionComparer);
        }

        return SmoModel.Create(tables);
    }

    public ImmutableArray<Table> CreateSmoTables(
        OsmModel model,
        PolicyDecisionSet decisions,
        ProfileSnapshot? profile = null,
        SmoBuildOptions? options = null,
        IEnumerable<EntityModel>? supplementalEntities = null,
        TypeMappingPolicy? typeMappingPolicy = null)
    {
        var smoModel = Create(model, decisions, profile, options, supplementalEntities, typeMappingPolicy);
        options ??= SmoBuildOptions.Default;

        using var factory = new SmoObjectGraphFactory();
        return factory.CreateTables(smoModel, options);
    }

    internal static EntityEmissionIndex BuildEntityContexts(
        OsmModel model,
        IEnumerable<EntityModel>? supplementalEntities)
    {
        var primaryBySchemaAndTable = new Dictionary<string, EntityEmissionContext>(StringComparer.OrdinalIgnoreCase);
        var primaryByPhysicalName = new Dictionary<string, List<EntityEmissionContext>>(StringComparer.OrdinalIgnoreCase);
        var primaryByLogicalName = new Dictionary<string, List<EntityEmissionContext>>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var context = EntityEmissionContext.Create(module.Name.Value, entity);
                var schemaKey = SchemaTableKey(entity.Schema.Value, entity.PhysicalName.Value);
                primaryBySchemaAndTable[schemaKey] = context;

                AddContext(primaryByPhysicalName, entity.PhysicalName.Value, context);
                AddContext(primaryByLogicalName, entity.LogicalName.Value, context);
            }
        }

        var supplementalBySchemaAndTable = new Dictionary<string, EntityEmissionContext>(StringComparer.OrdinalIgnoreCase);
        var supplementalByPhysicalName = new Dictionary<string, List<EntityEmissionContext>>(StringComparer.OrdinalIgnoreCase);
        var supplementalByLogicalName = new Dictionary<string, List<EntityEmissionContext>>(StringComparer.OrdinalIgnoreCase);

        if (supplementalEntities is not null)
        {
            foreach (var entity in supplementalEntities)
            {
                if (entity is null)
                {
                    continue;
                }

                var schemaKey = SchemaTableKey(entity.Schema.Value, entity.PhysicalName.Value);
                if (primaryBySchemaAndTable.ContainsKey(schemaKey) || supplementalBySchemaAndTable.ContainsKey(schemaKey))
                {
                    continue;
                }

                var context = EntityEmissionContext.Create(entity.Module.Value, entity);
                supplementalBySchemaAndTable[schemaKey] = context;
                AddContext(supplementalByPhysicalName, entity.PhysicalName.Value, context);
                AddContext(supplementalByLogicalName, entity.LogicalName.Value, context);
            }
        }

        return new EntityEmissionIndex(
            primaryBySchemaAndTable,
            ToImmutable(primaryByPhysicalName),
            ToImmutable(primaryByLogicalName),
            supplementalBySchemaAndTable,
            ToImmutable(supplementalByPhysicalName),
            ToImmutable(supplementalByLogicalName));
    }

    private static IReadOnlyDictionary<ColumnCoordinate, string> BuildProfileDefaults(ProfileSnapshot? profile)
    {
        if (profile is null || profile.Columns.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<ColumnCoordinate, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, string>();
        foreach (var column in profile.Columns)
        {
            var normalized = SmoNormalization.NormalizeSqlExpression(column.DefaultDefinition);
            if (normalized is null)
            {
                continue;
            }

            builder[ColumnCoordinate.From(column)] = normalized;
        }

        return builder.ToImmutable();
    }

    private static IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> BuildForeignKeyReality(ProfileSnapshot? profile)
    {
        if (profile is null || profile.ForeignKeys.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<ColumnCoordinate, ForeignKeyReality>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<ColumnCoordinate, ForeignKeyReality>();
        foreach (var foreignKey in profile.ForeignKeys)
        {
            builder[ColumnCoordinate.From(foreignKey.Reference)] = foreignKey;
        }

        return builder.ToImmutable();
    }

    private static void AddContext(
        IDictionary<string, List<EntityEmissionContext>> lookup,
        string key,
        EntityEmissionContext context)
    {
        if (!lookup.TryGetValue(key, out var list))
        {
            list = new List<EntityEmissionContext>();
            lookup[key] = list;
        }

        list.Add(context);
    }

    private static IReadOnlyDictionary<string, ImmutableArray<EntityEmissionContext>> ToImmutable(
        IDictionary<string, List<EntityEmissionContext>> lookup)
    {
        var builder = new Dictionary<string, ImmutableArray<EntityEmissionContext>>(lookup.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in lookup)
        {
            builder[pair.Key] = pair.Value.ToImmutableArray();
        }

        return builder;
    }

    private static string SchemaTableKey(string schema, string table)
        => $"{schema}.{table}";
}
