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
        typeMappingPolicy ??= TypeMappingPolicy.Default;

        var profileDefaults = BuildProfileDefaults(profile);
        var foreignKeyReality = BuildForeignKeyReality(profile);

        var contexts = BuildEntityContexts(model, supplementalEntities);
        var tablesBuilder = ImmutableArray.CreateBuilder<SmoTableDefinition>(model.Modules.Sum(static m => m.Entities.Length));

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
                tablesBuilder.Add(BuildTable(context, decisions, options, contexts, profileDefaults, foreignKeyReality, typeMappingPolicy));
            }
        }

        var tables = tablesBuilder.ToImmutable();
        if (!tables.IsDefaultOrEmpty)
        {
            tables = tables.Sort(SmoTableDefinitionComparer.Instance);
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

    private static SmoTableDefinition BuildTable(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        SmoBuildOptions options,
        EntityEmissionIndex entityLookup,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        TypeMappingPolicy typeMapping)
    {
        var columns = SmoColumnBuilder.BuildColumns(context, decisions, profileDefaults, typeMapping, entityLookup);
        var indexes = SmoIndexBuilder.BuildIndexes(context, decisions, options.IncludePlatformAutoIndexes, options.Format);
        var foreignKeys = SmoForeignKeyBuilder.BuildForeignKeys(context, decisions, entityLookup, foreignKeyReality, options.Format);
        var triggers = BuildTriggers(context);
        var catalog = string.IsNullOrWhiteSpace(context.Entity.Catalog) ? options.DefaultCatalogName : context.Entity.Catalog!;

        var moduleName = options.SanitizeModuleNames ? ModuleNameSanitizer.Sanitize(context.ModuleName) : context.ModuleName;

        return new SmoTableDefinition(
            moduleName,
            context.ModuleName,
            context.Entity.PhysicalName.Value,
            context.Entity.Schema.Value,
            catalog,
            context.Entity.LogicalName.Value,
            context.Entity.Metadata.Description,
            columns,
            indexes,
            foreignKeys,
            triggers);
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

    private static bool IsEmittableAttribute(AttributeModel attribute)
    {
        if (attribute is null)
        {
            return false;
        }

        if (!attribute.IsActive)
        {
            return false;
        }

        return !attribute.Reality.IsPresentButInactive;
    }

    private static ImmutableArray<SmoTriggerDefinition> BuildTriggers(EntityEmissionContext context)
    {
        if (context.Entity.Triggers.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoTriggerDefinition>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SmoTriggerDefinition>(context.Entity.Triggers.Length);
        foreach (var trigger in context.Entity.Triggers)
        {
            builder.Add(new SmoTriggerDefinition(
                SmoNormalization.NormalizeWhitespace(trigger.Name.Value) ?? trigger.Name.Value,
                context.Entity.Schema.Value,
                context.Entity.PhysicalName.Value,
                trigger.IsDisabled,
                SmoNormalization.NormalizeSqlExpression(trigger.Definition) ?? trigger.Definition));
        }

        var triggers = builder.ToImmutable();
        if (!triggers.IsDefaultOrEmpty)
        {
            triggers = triggers.Sort(SmoTriggerDefinitionComparer.Instance);
        }

        return triggers;
    }

    private sealed class SmoTableDefinitionComparer : IComparer<SmoTableDefinition>
    {
        public static readonly SmoTableDefinitionComparer Instance = new();

        public int Compare(SmoTableDefinition? x, SmoTableDefinition? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(x.Module, y.Module);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Schema, y.Schema);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(x.LogicalName, y.LogicalName);
            if (comparison != 0)
            {
                return comparison;
            }

            var leftCatalog = x.Catalog ?? string.Empty;
            var rightCatalog = y.Catalog ?? string.Empty;
            return StringComparer.OrdinalIgnoreCase.Compare(leftCatalog, rightCatalog);
        }
    }

    private sealed class SmoTriggerDefinitionComparer : IComparer<SmoTriggerDefinition>
    {
        public static readonly SmoTriggerDefinitionComparer Instance = new();

        public int Compare(SmoTriggerDefinition? x, SmoTriggerDefinition? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
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
    internal sealed record EntityEmissionContext(
        string ModuleName,
        EntityModel Entity,
        ImmutableArray<AttributeModel> EmittableAttributes,
        ImmutableArray<AttributeModel> IdentifierAttributes,
        IReadOnlyDictionary<string, AttributeModel> AttributeLookup,
        AttributeModel? ActiveIdentifier,
        AttributeModel? FallbackIdentifier)
    {
        public static EntityEmissionContext Create(string moduleName, EntityModel entity)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var emittableBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
            var attributeLookup = new Dictionary<string, AttributeModel>(StringComparer.OrdinalIgnoreCase);
            AttributeModel? activeIdentifier = null;
            AttributeModel? fallbackIdentifier = null;

            foreach (var attribute in entity.Attributes)
            {
                if (attribute.IsIdentifier && fallbackIdentifier is null)
                {
                    fallbackIdentifier = attribute;
                }

                if (!IsEmittableAttribute(attribute))
                {
                    continue;
                }

                emittableBuilder.Add(attribute);
                attributeLookup[attribute.ColumnName.Value] = attribute;

                if (attribute.IsIdentifier && activeIdentifier is null)
                {
                    activeIdentifier = attribute;
                }
            }

            var orderedAttributes = emittableBuilder.ToImmutable();

            var identifierBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
            foreach (var attribute in orderedAttributes)
            {
                if (attribute.IsIdentifier)
                {
                    identifierBuilder.Add(attribute);
                }
            }

            return new EntityEmissionContext(
                moduleName,
                entity,
                orderedAttributes,
                identifierBuilder.ToImmutable(),
                attributeLookup,
                activeIdentifier,
                fallbackIdentifier);
        }

        public AttributeModel? GetPreferredIdentifier() => ActiveIdentifier ?? FallbackIdentifier;
    }

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
    }
}
