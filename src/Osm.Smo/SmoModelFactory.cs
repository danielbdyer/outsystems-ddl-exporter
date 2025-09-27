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
        var columns = BuildColumns(entity, decisions);
        var indexes = BuildIndexes(entity, decisions, options.IncludePlatformAutoIndexes);
        var foreignKeys = BuildForeignKeys(entity, decisions, entityLookup);
        var catalog = string.IsNullOrWhiteSpace(entity.Catalog) ? options.DefaultCatalogName : entity.Catalog!;

        var module = options.SanitizeModuleNames ? SanitizeModuleName(moduleName) : moduleName;

        return new SmoTableDefinition(
            module,
            entity.PhysicalName.Value,
            entity.Schema.Value,
            catalog,
            columns,
            indexes,
            foreignKeys);
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

    private static ImmutableArray<SmoColumnDefinition> BuildColumns(EntityModel entity, PolicyDecisionSet decisions)
    {
        var builder = ImmutableArray.CreateBuilder<SmoColumnDefinition>();

        foreach (var attribute in entity.Attributes)
        {
            var dataType = SqlDataTypeMapper.Resolve(attribute);
            var nullable = !ShouldEnforceNotNull(entity, attribute, decisions);
            var isIdentity = attribute.IsAutoNumber;
            var identitySeed = isIdentity ? 1 : 0;
            var identityIncrement = isIdentity ? 1 : 0;

            builder.Add(new SmoColumnDefinition(
                attribute.ColumnName.Value,
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

    private static ImmutableArray<SmoIndexDefinition> BuildIndexes(EntityModel entity, PolicyDecisionSet decisions, bool includePlatformAuto)
    {
        var builder = ImmutableArray.CreateBuilder<SmoIndexDefinition>();
        var uniqueDecisions = decisions.UniqueIndexes;

        var keyColumns = entity.Attributes.Where(a => a.IsIdentifier).ToArray();
        if (keyColumns.Length > 0)
        {
            var pkColumns = keyColumns
                .Select((attribute, ordinal) => new SmoIndexColumnDefinition(attribute.ColumnName.Value, ordinal + 1))
                .ToImmutableArray();

            builder.Add(new SmoIndexDefinition(
                $"PK_{entity.PhysicalName.Value}",
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

            var columns = index.Columns
                .OrderBy(c => c.Ordinal)
                .Select(c => new SmoIndexColumnDefinition(c.Column.Value, c.Ordinal))
                .ToImmutableArray();

            var indexCoordinate = new IndexCoordinate(entity.Schema, entity.PhysicalName, index.Name);
            var enforceUnique = index.IsUnique;
            if (index.IsUnique && uniqueDecisions.TryGetValue(indexCoordinate, out var decision))
            {
                enforceUnique = decision.EnforceUnique;
            }

            builder.Add(new SmoIndexDefinition(
                index.Name.Value,
                enforceUnique,
                IsPrimaryKey: false,
                index.IsPlatformAuto,
                columns));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SmoForeignKeyDefinition> BuildForeignKeys(
        EntityModel entity,
        PolicyDecisionSet decisions,
        IReadOnlyDictionary<EntityName, EntityModel> entityLookup)
    {
        var builder = ImmutableArray.CreateBuilder<SmoForeignKeyDefinition>();

        foreach (var attribute in entity.Attributes.Where(a => a.Reference.IsReference))
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

            var referencedColumn = targetEntity.Attributes.FirstOrDefault(a => a.IsIdentifier);
            if (referencedColumn is null)
            {
                throw new InvalidOperationException($"Target entity '{targetEntity.PhysicalName.Value}' is missing an identifier column for foreign key creation.");
            }

            var name = $"FK_{entity.PhysicalName.Value}_{attribute.ColumnName.Value}";
            builder.Add(new SmoForeignKeyDefinition(
                name,
                attribute.ColumnName.Value,
                targetEntity.PhysicalName.Value,
                targetEntity.Schema.Value,
                referencedColumn.ColumnName.Value,
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
}
