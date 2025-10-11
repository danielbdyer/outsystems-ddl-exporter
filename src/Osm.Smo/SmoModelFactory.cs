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
                tablesBuilder.Add(BuildTable(context, decisions, options, contexts, profileDefaults, foreignKeyReality));
            }
        }

        var tables = tablesBuilder.ToImmutable();
        if (!tables.IsDefaultOrEmpty)
        {
            tables = tables.Sort(SmoTableDefinitionComparer.Instance);
        }

        return SmoModel.Create(tables);
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
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality)
    {
        var columns = BuildColumns(context, decisions, profileDefaults);
        var indexes = BuildIndexes(context, decisions, options.IncludePlatformAutoIndexes, options.Format);
        var foreignKeys = BuildForeignKeys(context, decisions, entityLookup, foreignKeyReality, options.Format);
        var triggers = BuildTriggers(context);
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
            var normalized = NormalizeSqlExpression(column.DefaultDefinition);
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
            var defaultExpression = NormalizeSqlExpression(ResolveDefaultExpression(onDisk.DefaultDefinition, profileDefaults, coordinate, attribute));
            var collation = NormalizeWhitespace(onDisk.Collation);
            var description = NormalizeWhitespace(attribute.Metadata.Description);
            var defaultConstraint = CreateDefaultConstraint(attribute.OnDisk.DefaultConstraint);

            var checkConstraints = ImmutableArray<SmoCheckConstraintDefinition>.Empty;
            if (!attribute.OnDisk.CheckConstraints.IsDefaultOrEmpty)
            {
                var checkBuilder = ImmutableArray.CreateBuilder<SmoCheckConstraintDefinition>(attribute.OnDisk.CheckConstraints.Length);
                foreach (var constraint in attribute.OnDisk.CheckConstraints)
                {
                    if (constraint is null)
                    {
                        continue;
                    }

                    var expression = NormalizeSqlExpression(constraint.Definition);
                    if (expression is null)
                    {
                        continue;
                    }

                    checkBuilder.Add(new SmoCheckConstraintDefinition(
                        NormalizeWhitespace(constraint.Name),
                        expression,
                        constraint.IsNotTrusted));
                }

                if (checkBuilder.Count > 0)
                {
                    checkConstraints = checkBuilder.ToImmutable().Sort(SmoCheckConstraintComparer.Instance);
                }
            }

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

    private static SmoDefaultConstraintDefinition? CreateDefaultConstraint(AttributeOnDiskDefaultConstraint? constraint)
    {
        if (constraint is not { Definition: { } definition })
        {
            return null;
        }

        var expression = NormalizeSqlExpression(definition);
        if (expression is null)
        {
            return null;
        }

        return new SmoDefaultConstraintDefinition(
            NormalizeWhitespace(constraint.Name),
            expression,
            constraint.IsNotTrusted);
    }

    private static string? NormalizeWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSqlExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
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

    private static ImmutableArray<SmoIndexColumnDefinition> BuildPrimaryKeyColumns(
        EntityEmissionContext context,
        IndexModel? domainPrimaryIndex,
        out ImmutableArray<AttributeModel> referencedAttributes)
    {
        if (domainPrimaryIndex is not null && !domainPrimaryIndex.Columns.IsDefaultOrEmpty)
        {
            var orderedColumns = domainPrimaryIndex.Columns
                .Where(static column => !column.IsIncluded)
                .OrderBy(static column => column.Ordinal)
                .ToImmutableArray();

            if (!orderedColumns.IsDefaultOrEmpty)
            {
                var columnBuilder = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(orderedColumns.Length);
                var attributeBuilder = ImmutableArray.CreateBuilder<AttributeModel>(orderedColumns.Length);
                var ordinal = 1;
                var missingAttribute = false;

                foreach (var column in orderedColumns)
                {
                    if (!context.AttributeLookup.TryGetValue(column.Column.Value, out var attribute))
                    {
                        missingAttribute = true;
                        break;
                    }

                    var isDescending = column.Direction == IndexColumnDirection.Descending;
                    columnBuilder.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, ordinal++, IsIncluded: false, isDescending));
                    attributeBuilder.Add(attribute);
                }

                if (!missingAttribute && columnBuilder.Count > 0)
                {
                    referencedAttributes = attributeBuilder.ToImmutable();
                    return columnBuilder.ToImmutable();
                }
            }
        }

        referencedAttributes = context.IdentifierAttributes.IsDefaultOrEmpty
            ? ImmutableArray<AttributeModel>.Empty
            : context.IdentifierAttributes;

        if (referencedAttributes.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoIndexColumnDefinition>.Empty;
        }

        var fallback = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(referencedAttributes.Length);
        for (var i = 0; i < referencedAttributes.Length; i++)
        {
            var attribute = referencedAttributes[i];
            fallback.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, i + 1, IsIncluded: false, IsDescending: false));
        }

        return fallback.ToImmutable();
    }

    private static ImmutableArray<SmoIndexDefinition> BuildIndexes(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        bool includePlatformAuto,
        SmoFormatOptions format)
    {
        var builder = ImmutableArray.CreateBuilder<SmoIndexDefinition>();
        var uniqueDecisions = decisions.UniqueIndexes;

        var domainPrimaryIndex = context.Entity.Indexes.FirstOrDefault(i => i.IsPrimary);
        var primaryMetadata = domainPrimaryIndex is not null
            ? MapIndexMetadata(domainPrimaryIndex)
            : SmoIndexMetadata.Empty;

        var primaryColumns = BuildPrimaryKeyColumns(context, domainPrimaryIndex, out var primaryAttributes);
        if (!primaryColumns.IsDefaultOrEmpty)
        {
            var pkName = NormalizeConstraintName(
                $"PK_{context.Entity.PhysicalName.Value}",
                context.Entity,
                primaryAttributes.IsDefaultOrEmpty ? context.IdentifierAttributes : primaryAttributes,
                ConstraintNameKind.PrimaryKey,
                format);

            builder.Add(new SmoIndexDefinition(
                pkName,
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                primaryColumns,
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
            var normalizedName = NormalizeConstraintName(
                index.Name.Value,
                context.Entity,
                referencedAttributes,
                index.IsUnique ? ConstraintNameKind.UniqueIndex : ConstraintNameKind.NonUniqueIndex,
                format);

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
        EntityEmissionIndex entityLookup,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        SmoFormatOptions format)
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

            var isNoCheck = foreignKeyReality.TryGetValue(coordinate, out var reality) && reality.IsNoCheck;
            var referencedAttributes = new[] { attribute };
            var name = NormalizeConstraintName(
                $"FK_{context.Entity.PhysicalName.Value}_{attribute.ColumnName.Value}",
                context.Entity,
                referencedAttributes,
                ConstraintNameKind.ForeignKey,
                format);
            builder.Add(new SmoForeignKeyDefinition(
                name,
                attribute.LogicalName.Value,
                targetEntity.ModuleName,
                targetEntity.Entity.PhysicalName.Value,
                targetEntity.Entity.Schema.Value,
                referencedColumn.LogicalName.Value,
                targetEntity.Entity.LogicalName.Value,
                MapDeleteRule(attribute.Reference.DeleteRuleCode),
                isNoCheck));
        }

        return builder.ToImmutable();
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
                NormalizeWhitespace(trigger.Name.Value) ?? trigger.Name.Value,
                context.Entity.Schema.Value,
                context.Entity.PhysicalName.Value,
                trigger.IsDisabled,
                NormalizeSqlExpression(trigger.Definition) ?? trigger.Definition));
        }

        var triggers = builder.ToImmutable();
        if (!triggers.IsDefaultOrEmpty)
        {
            triggers = triggers.Sort(SmoTriggerDefinitionComparer.Instance);
        }

        return triggers;
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
        IReadOnlyCollection<AttributeModel> referencedAttributes,
        ConstraintNameKind kind,
        SmoFormatOptions format)
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
        var baseName = string.IsNullOrEmpty(prefix) ? rebuiltSuffix : $"{prefix}_{rebuiltSuffix}";
        return format.IndexNaming.Apply(baseName, kind);
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

    private sealed class SmoIndexDefinitionComparer : IComparer<SmoIndexDefinition>
    {
        public static readonly SmoIndexDefinitionComparer Instance = new();

        public int Compare(SmoIndexDefinition? x, SmoIndexDefinition? y)
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

            if (x.IsPrimaryKey && !y.IsPrimaryKey)
            {
                return -1;
            }

            if (!x.IsPrimaryKey && y.IsPrimaryKey)
            {
                return 1;
            }

            if (x.IsUnique && !y.IsUnique)
            {
                return -1;
            }

            if (!x.IsUnique && y.IsUnique)
            {
                return 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
        }
    }

    private sealed class SmoForeignKeyDefinitionComparer : IComparer<SmoForeignKeyDefinition>
    {
        public static readonly SmoForeignKeyDefinitionComparer Instance = new();

        public int Compare(SmoForeignKeyDefinition? x, SmoForeignKeyDefinition? y)
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

            var comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.ReferencedSchema, y.ReferencedSchema);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.ReferencedTable, y.ReferencedTable);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Column, y.Column);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.ReferencedColumn, y.ReferencedColumn);
        }
    }

    private sealed class SmoCheckConstraintComparer : IComparer<SmoCheckConstraintDefinition>
    {
        public static readonly SmoCheckConstraintComparer Instance = new();

        public int Compare(SmoCheckConstraintDefinition? x, SmoCheckConstraintDefinition? y)
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

            var leftName = x.Name ?? string.Empty;
            var rightName = y.Name ?? string.Empty;
            var comparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.Expression, y.Expression);
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
