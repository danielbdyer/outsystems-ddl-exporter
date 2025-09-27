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

        var entityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(e => e.LogicalName, e => e);

        var tables = model.Modules
            .SelectMany(module => module.Entities.Select(entity => BuildTable(module.Name.Value, entity, decisions, options, entityLookup)))
            .ToImmutableArray();

        return SmoModel.Create(tables);
    }

    private static SmoTableDefinition BuildTable(
        string moduleName,
        EntityModel entity,
        PolicyDecisionSet decisions,
        SmoBuildOptions options,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        var emittableAttributes = entity.Attributes
            .Where(IsEmittableAttribute)
            .ToArray();

        var attributeLookup = emittableAttributes
            .ToDictionary(a => a.ColumnName.Value, StringComparer.OrdinalIgnoreCase);

        var columns = BuildColumns(entity, emittableAttributes, decisions);
        var indexes = BuildIndexes(entity, emittableAttributes, decisions, options.IncludePlatformAutoIndexes, attributeLookup);
        var foreignKeys = BuildForeignKeys(entity, emittableAttributes, decisions, entityLookup);
        var catalog = string.IsNullOrWhiteSpace(entity.Catalog) ? options.DefaultCatalogName : entity.Catalog!;

        var module = options.SanitizeModuleNames ? SanitizeModuleName(moduleName) : moduleName;

        return new SmoTableDefinition(
            module,
            entity.PhysicalName.Value,
            entity.Schema.Value,
            catalog,
            entity.LogicalName.Value,
            columns,
            indexes,
            foreignKeys);
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
        EntityModel entity,
        IReadOnlyList<AttributeModel> attributes,
        PolicyDecisionSet decisions)
    {
        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in attributes)
        {
            var dataType = SqlDataTypeMapper.Resolve(attribute);
            var nullable = !ShouldEnforceNotNull(entity, attribute, decisions);
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
        EntityModel entity,
        IReadOnlyList<AttributeModel> attributes,
        PolicyDecisionSet decisions,
        bool includePlatformAuto,
        IReadOnlyDictionary<string, AttributeModel> attributeLookup)
    {
        var builder = ImmutableArray.CreateBuilder<SmoIndexDefinition>();
        var uniqueDecisions = decisions.UniqueIndexes;

        var keyColumns = attributes.Where(a => a.IsIdentifier).ToArray();
        if (keyColumns.Length > 0)
        {
            var pkColumns = keyColumns
                .Select((attribute, ordinal) => new SmoIndexColumnDefinition(attribute.LogicalName.Value, ordinal + 1))
                .ToImmutableArray();

            var pkName = NormalizeConstraintName($"PK_{entity.PhysicalName.Value}", entity, keyColumns);

            builder.Add(new SmoIndexDefinition(
                pkName,
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                pkColumns));
        }

        foreach (var index in entity.Indexes)
        {
            if (index.IsPrimary)
            {
                continue;
            }

            if (!includePlatformAuto && index.IsPlatformAuto)
            {
                continue;
            }

            var referencedAttributes = new List<AttributeModel>();
            var columnsBuilder = ImmutableArray.CreateBuilder<SmoIndexColumnDefinition>();
            foreach (var column in index.Columns.OrderBy(c => c.Ordinal))
            {
                if (!attributeLookup.TryGetValue(column.Column.Value, out var attribute))
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
            var normalizedName = NormalizeConstraintName(index.Name.Value, entity, referencedAttributes);

            var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
            var enforceUnique = index.IsUnique;
            if (index.IsUnique && uniqueDecisions.TryGetValue(indexCoordinate, out var decision))
            {
                enforceUnique = decision.EnforceUnique;
            }

            var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
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
        EntityModel entity,
        IReadOnlyList<AttributeModel> attributes,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        var builder = ImmutableArray.CreateBuilder<SmoForeignKeyDefinition>();

        foreach (var attribute in attributes.Where(a => a.Reference.IsReference))
        {
            var coordinate = new ColumnCoordinate(entity.Schema, entity.PhysicalName, attribute.ColumnName);
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
                throw new InvalidOperationException($"Target entity '{targetEntityName.Value}' not found for foreign key on '{entity.PhysicalName.Value}.{attribute.ColumnName.Value}'.");
            }

            var referencedColumn = targetEntity.Attributes.FirstOrDefault(a => a.IsIdentifier && IsEmittableAttribute(a))
                ?? targetEntity.Attributes.FirstOrDefault(a => a.IsIdentifier);
            if (referencedColumn is null)
            {
                throw new InvalidOperationException($"Target entity '{targetEntity.PhysicalName.Value}' is missing an identifier column for foreign key creation.");
            }

            var referencedAttributes = new[] { attribute };
            var name = NormalizeConstraintName($"FK_{entity.PhysicalName.Value}_{attribute.ColumnName.Value}", entity, referencedAttributes);
            builder.Add(new SmoForeignKeyDefinition(
                name,
                attribute.LogicalName.Value,
                targetEntity.PhysicalName.Value,
                targetEntity.Schema.Value,
                referencedColumn.LogicalName.Value,
                targetEntity.LogicalName.Value,
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
}
