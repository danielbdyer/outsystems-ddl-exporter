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
        IEnumerable<EntityModel>? supplementalEntities = null)
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

        var profileDefaults = BuildProfileDefaults(profile);

        var contexts = BuildEntityContexts(model, supplementalEntities);
        var tablesBuilder = ImmutableArray.CreateBuilder<SmoTableDefinition>(model.Modules.Sum(static m => m.Entities.Length));

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var context = contexts.GetContext(entity);
                tablesBuilder.Add(BuildTable(context, decisions, options, contexts, profileDefaults));
            }
        }

        return SmoModel.Create(tablesBuilder.ToImmutable());
    }

    private static EntityEmissionIndex BuildEntityContexts(
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
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults)
    {
        var columns = BuildColumns(context, decisions, profileDefaults);
        var indexes = BuildIndexes(context, decisions, options.IncludePlatformAutoIndexes);
        var foreignKeys = BuildForeignKeys(context, decisions, entityLookup);
        var catalog = string.IsNullOrWhiteSpace(context.Entity.Catalog) ? options.DefaultCatalogName : context.Entity.Catalog!;

        var moduleName = options.SanitizeModuleNames ? SanitizeModuleName(context.ModuleName) : context.ModuleName;

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
            foreignKeys);
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
            if (string.IsNullOrWhiteSpace(column.DefaultDefinition))
            {
                continue;
            }

            builder[ColumnCoordinate.From(column)] = column.DefaultDefinition!;
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

    private static string SanitizeModuleName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Module";
        }

        var trimmed = value.Trim();
        var sanitized = new string(trimmed
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "Module" : sanitized;
    }

    private static ImmutableArray<SmoColumnDefinition> BuildColumns(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults)
    {
        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in context.EmittableAttributes)
        {
            var dataType = SqlDataTypeMapper.Resolve(attribute);
            if (attribute.IsIdentifier)
            {
                dataType = DataType.BigInt;
            }
            else if (attribute.Reference.IsReference &&
                     string.Equals(attribute.DataType, "Identifier", StringComparison.OrdinalIgnoreCase))
            {
                dataType = DataType.BigInt;
            }
            var nullable = !ShouldEnforceNotNull(context.Entity, attribute, decisions);
            var onDisk = attribute.OnDisk;
            var isIdentity = onDisk.IsIdentity ?? attribute.IsAutoNumber;
            var identitySeed = isIdentity ? 1 : 0;
            var identityIncrement = isIdentity ? 1 : 0;
            var isComputed = onDisk.IsComputed ?? false;
            var computed = isComputed ? onDisk.ComputedDefinition : null;
            var coordinate = new ColumnCoordinate(context.Entity.Schema, context.Entity.PhysicalName, attribute.ColumnName);
            var defaultExpression = ResolveDefaultExpression(onDisk.DefaultDefinition, profileDefaults, coordinate, attribute);
            var collation = onDisk.Collation;
            var description = attribute.Metadata.Description;
            var defaultConstraint = attribute.OnDisk.DefaultConstraint is { Definition: not null } onDiskDefault
                ? new SmoDefaultConstraintDefinition(onDiskDefault.Name, onDiskDefault.Definition, onDiskDefault.IsNotTrusted)
                : null;
            var checkConstraints = attribute.OnDisk.CheckConstraints.IsDefaultOrEmpty
                ? ImmutableArray<SmoCheckConstraintDefinition>.Empty
                : attribute.OnDisk.CheckConstraints
                    .Where(static constraint => !string.IsNullOrWhiteSpace(constraint.Definition))
                    .Select(static constraint => new SmoCheckConstraintDefinition(constraint.Name, constraint.Definition, constraint.IsNotTrusted))
                    .ToImmutableArray();

            builder.Add(new SmoColumnDefinition(
                attribute.LogicalName.Value,
                attribute.LogicalName.Value,
                dataType,
                nullable,
                isIdentity,
                identitySeed,
                identityIncrement,
                isComputed,
                computed,
                defaultExpression,
                collation,
                description,
                defaultConstraint,
                checkConstraints.IsDefaultOrEmpty ? ImmutableArray<SmoCheckConstraintDefinition>.Empty : checkConstraints));
        }

        return builder.ToImmutable();
    }

    private static string? ResolveDefaultExpression(
        string? onDiskDefault,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        ColumnCoordinate coordinate,
        AttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(onDiskDefault))
        {
            return onDiskDefault;
        }

        if (profileDefaults.TryGetValue(coordinate, out var profileDefault) &&
            !string.IsNullOrWhiteSpace(profileDefault))
        {
            return profileDefault;
        }

        return string.IsNullOrWhiteSpace(attribute.DefaultValue) ? null : attribute.DefaultValue;
    }

    private static bool ShouldEnforceNotNull(EntityModel entity, AttributeModel attribute, PolicyDecisionSet decisions)
    {
        var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
        if (decisions.Nullability.TryGetValue(coordinate, out var decision))
        {
            return decision.MakeNotNull;
        }

        return attribute.IsMandatory;
    }

    private static ImmutableArray<SmoIndexDefinition> BuildIndexes(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        bool includePlatformAuto)
    {
        var builder = ImmutableArray.CreateBuilder<SmoIndexDefinition>();
        var uniqueDecisions = decisions.UniqueIndexes;

        var domainPrimaryIndex = context.Entity.Indexes.FirstOrDefault(i => i.IsPrimary);
        var primaryMetadata = domainPrimaryIndex is not null
            ? MapIndexMetadata(domainPrimaryIndex)
            : SmoIndexMetadata.Empty;

        if (!context.IdentifierAttributes.IsDefaultOrEmpty)
        {
            var pkColumns = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(context.IdentifierAttributes.Length);
            for (var i = 0; i < context.IdentifierAttributes.Length; i++)
            {
                var attribute = context.IdentifierAttributes[i];
                pkColumns.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, i + 1, IsIncluded: false, IsDescending: false));
            }

            var pkName = NormalizeConstraintName($"PK_{context.Entity.PhysicalName.Value}", context.Entity, context.IdentifierAttributes);
            var pkColumnArray = pkColumns.ToImmutable();

            builder.Add(new SmoIndexDefinition(
                pkName,
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                pkColumnArray,
                primaryMetadata));
        }

        foreach (var index in context.Entity.Indexes)
        {
            if (index.IsPrimary)
            {
                continue;
            }

            if (!includePlatformAuto && index.IsPlatformAuto && !index.IsUnique)
            {
                continue;
            }

            var referencedAttributes = new List<AttributeModel>(index.Columns.Length);
            var orderedColumns = index.Columns.ToBuilder();
            orderedColumns.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));

            var columnsBuilder = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(orderedColumns.Count);
            foreach (var column in orderedColumns)
            {
                if (!context.AttributeLookup.TryGetValue(column.Column.Value, out var attribute))
                {
                    referencedAttributes.Clear();
                    columnsBuilder.Clear();
                    break;
                }

                if (!column.IsIncluded)
                {
                    referencedAttributes.Add(attribute);
                }
                var isDescending = column.Direction == IndexColumnDirection.Descending;
                columnsBuilder.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, column.Ordinal, column.IsIncluded, isDescending));
            }

            if (columnsBuilder.Count == 0)
            {
                continue;
            }

            var columns = columnsBuilder.ToImmutable();
            if (!columns.Any(c => !c.IsIncluded))
            {
                continue;
            }
            var normalizedName = NormalizeConstraintName(index.Name.Value, context.Entity, referencedAttributes);

            var indexCoordinate = new IndexCoordinate(context.Entity.Schema, context.Entity.PhysicalName, index.Name);
            var enforceUnique = index.IsUnique;
            if (index.IsUnique && uniqueDecisions.TryGetValue(indexCoordinate, out var decision))
            {
                enforceUnique = decision.EnforceUnique;
            }

            var metadata = MapIndexMetadata(index);
            builder.Add(new SmoIndexDefinition(
                normalizedName,
                enforceUnique,
                IsPrimaryKey: false,
                index.IsPlatformAuto,
                columns,
                metadata));
        }

        return builder.ToImmutable();
    }

    private static SmoIndexMetadata MapIndexMetadata(IndexModel index)
    {
        var onDisk = index.OnDisk;
        var partitionColumns = onDisk.PartitionColumns.IsDefaultOrEmpty
            ? ImmutableArray<SmoIndexPartitionColumn>.Empty
            : onDisk.PartitionColumns
                .OrderBy(static c => c.Ordinal)
                .Select(c => new SmoIndexPartitionColumn(c.Column.Value, c.Ordinal))
                .ToImmutableArray();

        var compression = onDisk.DataCompression.IsDefaultOrEmpty
            ? ImmutableArray<SmoIndexCompressionSetting>.Empty
            : onDisk.DataCompression
                .OrderBy(static c => c.PartitionNumber)
                .Select(c => new SmoIndexCompressionSetting(c.PartitionNumber, c.Compression))
                .ToImmutableArray();

        SmoIndexDataSpace? dataSpace = null;
        if (onDisk.DataSpace is not null)
        {
            dataSpace = new SmoIndexDataSpace(onDisk.DataSpace.Name, onDisk.DataSpace.Type);
        }

        return new SmoIndexMetadata(
            onDisk.IsDisabled,
            onDisk.IsPadded,
            onDisk.FillFactor,
            onDisk.IgnoreDuplicateKey,
            onDisk.AllowRowLocks,
            onDisk.AllowPageLocks,
            onDisk.NoRecomputeStatistics,
            onDisk.FilterDefinition,
            dataSpace,
            partitionColumns,
            compression);
    }

    private static ImmutableArray<SmoForeignKeyDefinition> BuildForeignKeys(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        EntityEmissionIndex entityLookup)
    {
        var builder = ImmutableArray.CreateBuilder<SmoForeignKeyDefinition>();

        foreach (var attribute in context.EmittableAttributes)
        {
            if (!attribute.Reference.IsReference)
            {
                continue;
            }

            var coordinate = new ColumnCoordinate(context.Entity.Schema, context.Entity.PhysicalName, attribute.ColumnName);
            if (!decisions.ForeignKeys.TryGetValue(coordinate, out var decision) || !decision.CreateConstraint)
            {
                continue;
            }

            if (!entityLookup.TryResolveReference(attribute.Reference, context, out var targetEntity))
            {
                var targetName = attribute.Reference.TargetEntity?.Value ?? attribute.Reference.TargetPhysicalName?.Value ?? "<unknown>";
                throw new InvalidOperationException($"Target entity '{targetName}' not found for foreign key on '{context.Entity.PhysicalName.Value}.{attribute.ColumnName.Value}'.");
            }

            var referencedColumn = targetEntity.GetPreferredIdentifier();
            if (referencedColumn is null)
            {
                throw new InvalidOperationException($"Target entity '{targetEntity.Entity.PhysicalName.Value}' is missing an identifier column for foreign key creation.");
            }

            var referencedAttributes = new[] { attribute };
            var name = NormalizeConstraintName($"FK_{context.Entity.PhysicalName.Value}_{attribute.ColumnName.Value}", context.Entity, referencedAttributes);
            builder.Add(new SmoForeignKeyDefinition(
                name,
                attribute.LogicalName.Value,
                targetEntity.ModuleName,
                targetEntity.Entity.PhysicalName.Value,
                targetEntity.Entity.Schema.Value,
                referencedColumn.LogicalName.Value,
                targetEntity.Entity.LogicalName.Value,
                MapDeleteRule(attribute.Reference.DeleteRuleCode)));
        }

        return builder.ToImmutable();
    }

    private static ForeignKeyAction MapDeleteRule(string? deleteRule)
    {
        if (string.IsNullOrWhiteSpace(deleteRule))
        {
            return ForeignKeyAction.NoAction;
        }

        return deleteRule.Trim() switch
        {
            "Cascade" => ForeignKeyAction.Cascade,
            "Delete" => ForeignKeyAction.Cascade,
            "Protect" => ForeignKeyAction.NoAction,
            "Ignore" => ForeignKeyAction.NoAction,
            "SetNull" => ForeignKeyAction.SetNull,
            _ => ForeignKeyAction.NoAction,
        };
    }

    private static string NormalizeConstraintName(
        string originalName,
        EntityModel entity,
        IReadOnlyCollection<AttributeModel> referencedAttributes)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return originalName;
        }

        var normalized = ReplaceIgnoreCase(originalName, entity.PhysicalName.Value, entity.LogicalName.Value);
        normalized = ReplaceIgnoreCase(normalized, entity.LogicalName.Value, entity.LogicalName.Value);

        foreach (var attribute in referencedAttributes)
        {
            normalized = ReplaceIgnoreCase(normalized, attribute.ColumnName.Value, attribute.LogicalName.Value);
            normalized = ReplaceIgnoreCase(normalized, attribute.LogicalName.Value, attribute.LogicalName.Value);
        }

        var prefixSeparator = normalized.IndexOf('_');
        var prefix = prefixSeparator > 0 ? normalized[..prefixSeparator] : null;
        var suffix = prefixSeparator > 0 ? normalized[(prefixSeparator + 1)..] : normalized;

        var parts = suffix
            .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => NormalizeToken(part))
            .ToArray();

        var rebuiltSuffix = string.Join('_', parts);
        return string.IsNullOrEmpty(prefix) ? rebuiltSuffix : $"{prefix}_{rebuiltSuffix}";
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var isAllUpper = value.All(char.IsUpper);
        var isAllLower = value.All(char.IsLower);

        if (!isAllUpper && !isAllLower)
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var currentIndex = 0;
        var comparison = StringComparison.OrdinalIgnoreCase;
        var builder = new System.Text.StringBuilder();

        while (currentIndex < source.Length)
        {
            var matchIndex = source.IndexOf(search, currentIndex, comparison);
            if (matchIndex < 0)
            {
                builder.Append(source, currentIndex, source.Length - currentIndex);
                break;
            }

            builder.Append(source, currentIndex, matchIndex - currentIndex);
            builder.Append(replacement);
            currentIndex = matchIndex + search.Length;
        }

        return builder.ToString();
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
    private sealed record EntityEmissionContext(
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

            var identifierBuilder = ImmutableArray.CreateBuilder<AttributeModel>();
            foreach (var attribute in emittableBuilder)
            {
                if (attribute.IsIdentifier)
                {
                    identifierBuilder.Add(attribute);
                }
            }

            return new EntityEmissionContext(
                moduleName,
                entity,
                emittableBuilder.ToImmutable(),
                identifierBuilder.ToImmutable(),
                attributeLookup,
                activeIdentifier,
                fallbackIdentifier);
        }

        public AttributeModel? GetPreferredIdentifier() => ActiveIdentifier ?? FallbackIdentifier;
    }

    private sealed class EntityEmissionIndex
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
