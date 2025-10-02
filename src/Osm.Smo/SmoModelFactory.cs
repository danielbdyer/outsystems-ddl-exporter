using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Smo;

public sealed class SmoModelFactory
{
    public SmoModel Create(OsmModel model, PolicyDecisionSet decisions, SmoBuildOptions? options = null)
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

        var contexts = BuildEntityContexts(model);
        var tablesBuilder = ImmutableArray.CreateBuilder<SmoTableDefinition>(contexts.Count);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                if (!contexts.TryGetValue(entity.LogicalName, out var context))
                {
                    continue;
                }

                tablesBuilder.Add(BuildTable(context, decisions, options, contexts));
            }
        }

        return SmoModel.Create(tablesBuilder.ToImmutable());
    }

    private static Dictionary<EntityName, EntityEmissionContext> BuildEntityContexts(OsmModel model)
    {
        var lookup = new Dictionary<EntityName, EntityEmissionContext>();

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var context = EntityEmissionContext.Create(module.Name.Value, entity);
                lookup[entity.LogicalName] = context;
            }
        }

        return lookup;
    }

    private static SmoTableDefinition BuildTable(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        SmoBuildOptions options,
        IReadOnlyDictionary<EntityName, EntityEmissionContext> entityLookup)
    {
        var columns = BuildColumns(context, decisions);
        var indexes = BuildIndexes(context, decisions, options.IncludePlatformAutoIndexes);
        var foreignKeys = BuildForeignKeys(context, decisions, entityLookup);
        var checkConstraints = BuildCheckConstraints(context);
        var catalog = string.IsNullOrWhiteSpace(context.Entity.Catalog) ? options.DefaultCatalogName : context.Entity.Catalog!;

        var moduleName = options.SanitizeModuleNames ? SanitizeModuleName(context.ModuleName) : context.ModuleName;

        return new SmoTableDefinition(
            moduleName,
            context.Entity.PhysicalName.Value,
            context.Entity.Schema.Value,
            catalog,
            context.Entity.LogicalName.Value,
            columns,
            indexes,
            foreignKeys,
            checkConstraints);
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
        PolicyDecisionSet decisions)
    {
        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in context.EmittableAttributes)
        {
            var dataType = SqlDataTypeMapper.Resolve(attribute);
            var nullable = !ShouldEnforceNotNull(context.Entity, attribute, decisions);
            var isIdentity = attribute.IsAutoNumber;
            var identitySeed = isIdentity ? 1 : 0;
            var identityIncrement = isIdentity ? 1 : 0;

            builder.Add(new SmoColumnDefinition(
                attribute.LogicalName.Value,
                attribute.LogicalName.Value,
                dataType,
                nullable,
                isIdentity,
                identitySeed,
                identityIncrement));
        }

        return builder.ToImmutable();
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

        if (!context.IdentifierAttributes.IsDefaultOrEmpty)
        {
            var pkColumns = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>(context.IdentifierAttributes.Length);
            for (var i = 0; i < context.IdentifierAttributes.Length; i++)
            {
                var attribute = context.IdentifierAttributes[i];
                pkColumns.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, i + 1));
            }

            var pkName = NormalizeConstraintName($"PK_{context.Entity.PhysicalName.Value}", context.Entity, context.IdentifierAttributes);
            var pkColumnArray = pkColumns.ToImmutable();

            builder.Add(new SmoIndexDefinition(
                pkName,
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                pkColumnArray));
        }

        foreach (var index in context.Entity.Indexes)
        {
            if (index.IsPrimary)
            {
                continue;
            }

            if (!includePlatformAuto && index.IsPlatformAuto)
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

                referencedAttributes.Add(attribute);
                columnsBuilder.Add(new SmoIndexColumnDefinition(attribute.LogicalName.Value, column.Ordinal));
            }

            if (columnsBuilder.Count == 0)
            {
                continue;
            }

            var columns = columnsBuilder.ToImmutable();
            var normalizedName = NormalizeConstraintName(index.Name.Value, context.Entity, referencedAttributes);

            var indexCoordinate = new IndexCoordinate(context.Entity.Schema, context.Entity.PhysicalName, index.Name);
            var enforceUnique = index.IsUnique;
            if (index.IsUnique && uniqueDecisions.TryGetValue(indexCoordinate, out var decision))
            {
                enforceUnique = decision.EnforceUnique;
            }

            builder.Add(new SmoIndexDefinition(
                normalizedName,
                enforceUnique,
                IsPrimaryKey: false,
                index.IsPlatformAuto,
                columns));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmoForeignKeyDefinition> BuildForeignKeys(
        EntityEmissionContext context,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<EntityName, EntityEmissionContext> entityLookup)
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

            if (attribute.Reference.TargetEntity is null)
            {
                continue;
            }

            var targetEntityName = attribute.Reference.TargetEntity.Value;
            if (!entityLookup.TryGetValue(targetEntityName, out var targetEntity))
            {
                throw new InvalidOperationException($"Target entity '{targetEntityName.Value}' not found for foreign key on '{context.Entity.PhysicalName.Value}.{attribute.ColumnName.Value}'.");
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
                targetEntity.Entity.PhysicalName.Value,
                targetEntity.Entity.Schema.Value,
                referencedColumn.LogicalName.Value,
                targetEntity.Entity.LogicalName.Value,
                MapDeleteRule(attribute.Reference.DeleteRuleCode)));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmoCheckConstraintDefinition> BuildCheckConstraints(EntityEmissionContext context)
    {
        if (context.ActiveCheckConstraints.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoCheckConstraintDefinition>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SmoCheckConstraintDefinition>(context.ActiveCheckConstraints.Length);

        foreach (var constraint in context.ActiveCheckConstraints)
        {
            var normalizedName = NormalizeConstraintName(
                constraint.Name.Value,
                context.Entity,
                context.EmittableAttributes);

            builder.Add(new SmoCheckConstraintDefinition(normalizedName, constraint.Definition));
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
    private sealed record EntityEmissionContext(
        string ModuleName,
        EntityModel Entity,
        ImmutableArray<AttributeModel> EmittableAttributes,
        ImmutableArray<AttributeModel> IdentifierAttributes,
        IReadOnlyDictionary<string, AttributeModel> AttributeLookup,
        AttributeModel? ActiveIdentifier,
        AttributeModel? FallbackIdentifier,
        ImmutableArray<CheckConstraintModel> ActiveCheckConstraints)
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
            var checkConstraintBuilder = ImmutableArray.CreateBuilder<CheckConstraintModel>();

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

            foreach (var constraint in entity.CheckConstraints)
            {
                if (constraint.IsActive)
                {
                    checkConstraintBuilder.Add(constraint);
                }
            }

            return new EntityEmissionContext(
                moduleName,
                entity,
                emittableBuilder.ToImmutable(),
                identifierBuilder.ToImmutable(),
                attributeLookup,
                activeIdentifier,
                fallbackIdentifier,
                checkConstraintBuilder.ToImmutable());
        }

        public AttributeModel? GetPreferredIdentifier() => ActiveIdentifier ?? FallbackIdentifier;
    }
}
