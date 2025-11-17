using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission.Seeds;

public static class DynamicTableNameResolver
{
    public static DynamicTableNameResolutionResult Resolve(
        DynamicEntityDataset dataset,
        OsmModel? model,
        NamingOverrideOptions? namingOverrides)
    {
        if (dataset is null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        if (dataset.IsEmpty || model is null || model.Modules.IsDefaultOrEmpty)
        {
            return DynamicTableNameResolutionResult.Empty(dataset);
        }

        namingOverrides ??= NamingOverrideOptions.Empty;
        var lookup = EntityLookup.Create(model, namingOverrides);
        if (lookup.IsEmpty)
        {
            return DynamicTableNameResolutionResult.Empty(dataset);
        }

        var tables = dataset.Tables;
        if (tables.IsDefaultOrEmpty)
        {
            return DynamicTableNameResolutionResult.Empty(dataset);
        }

        var tableBuilder = tables.ToBuilder();
        var reconciliations = ImmutableArray.CreateBuilder<DynamicEntityTableReconciliation>();
        var replaced = false;

        for (var i = 0; i < tableBuilder.Count; i++)
        {
            var table = tableBuilder[i];
            if (table is null)
            {
                continue;
            }

            var definition = table.Definition;
            var match = lookup.TryResolve(definition, out var strategy);
            if (match is null)
            {
                continue;
            }

            var resolvedDefinition = definition with
            {
                Module = match.Module,
                LogicalName = match.LogicalName,
                Schema = match.Schema,
                PhysicalName = match.PhysicalName,
                EffectiveName = match.EffectiveName,
            };

            if (DefinitionsEqual(definition, resolvedDefinition))
            {
                continue;
            }

            tableBuilder[i] = table with { Definition = resolvedDefinition };
            replaced = true;

            reconciliations.Add(new DynamicEntityTableReconciliation(
                DatasetModule: definition.Module ?? string.Empty,
                DatasetLogicalName: definition.LogicalName ?? string.Empty,
                DatasetSchema: definition.Schema ?? string.Empty,
                DatasetPhysicalName: definition.PhysicalName ?? string.Empty,
                DatasetEffectiveName: definition.EffectiveName ?? string.Empty,
                ResolvedModule: resolvedDefinition.Module ?? string.Empty,
                ResolvedLogicalName: resolvedDefinition.LogicalName ?? string.Empty,
                ResolvedSchema: resolvedDefinition.Schema ?? string.Empty,
                ResolvedPhysicalName: resolvedDefinition.PhysicalName ?? string.Empty,
                ResolvedEffectiveName: resolvedDefinition.EffectiveName ?? string.Empty,
                Strategy: strategy));
        }

        if (!replaced || reconciliations.Count == 0)
        {
            return DynamicTableNameResolutionResult.Empty(dataset);
        }

        return new DynamicTableNameResolutionResult(
            new DynamicEntityDataset(tableBuilder.ToImmutable()),
            reconciliations.ToImmutable());
    }

    private static bool DefinitionsEqual(
        StaticEntitySeedTableDefinition original,
        StaticEntitySeedTableDefinition resolved)
    {
        return string.Equals(original.Module, resolved.Module, StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.LogicalName, resolved.LogicalName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.Schema, resolved.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.PhysicalName, resolved.PhysicalName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.EffectiveName, resolved.EffectiveName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EntityLookup
    {
        private readonly Dictionary<string, EntityDescriptor> _physicalLookup;
        private readonly Dictionary<string, EntityDescriptor> _effectiveLookup;
        private readonly Dictionary<string, EntityDescriptor> _moduleLookup;

        private EntityLookup(
            Dictionary<string, EntityDescriptor> physicalLookup,
            Dictionary<string, EntityDescriptor> effectiveLookup,
            Dictionary<string, EntityDescriptor> moduleLookup)
        {
            _physicalLookup = physicalLookup;
            _effectiveLookup = effectiveLookup;
            _moduleLookup = moduleLookup;
        }

        public static EntityLookup Create(OsmModel model, NamingOverrideOptions namingOverrides)
        {
            var physicalLookup = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);
            var effectiveLookup = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);
            var moduleLookup = new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in model.Modules)
            {
                if (module.Entities.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var entity in module.Entities)
                {
                    var descriptor = new EntityDescriptor(
                        Module: module.Name.Value,
                        LogicalName: entity.LogicalName.Value,
                        Schema: entity.Schema.Value,
                        PhysicalName: entity.PhysicalName.Value,
                        EffectiveName: namingOverrides.GetEffectiveTableName(
                            entity.Schema.Value,
                            entity.PhysicalName.Value,
                            entity.LogicalName.Value,
                            module.Name.Value));

                    var physicalKey = TableKey(descriptor.Schema, descriptor.PhysicalName);
                    physicalLookup[physicalKey] = descriptor;

                    var effectiveKey = TableKey(descriptor.Schema, descriptor.EffectiveName);
                    if (!effectiveLookup.ContainsKey(effectiveKey))
                    {
                        effectiveLookup[effectiveKey] = descriptor;
                    }

                    var moduleKey = ModuleKey(descriptor.Module, descriptor.LogicalName);
                    if (!moduleLookup.ContainsKey(moduleKey))
                    {
                        moduleLookup[moduleKey] = descriptor;
                    }
                }
            }

            return new EntityLookup(physicalLookup, effectiveLookup, moduleLookup);
        }

        public bool IsEmpty => _physicalLookup.Count == 0;

        public EntityDescriptor? TryResolve(
            StaticEntitySeedTableDefinition definition,
            out DynamicTableResolutionStrategy strategy)
        {
            if (definition is null)
            {
                strategy = DynamicTableResolutionStrategy.PhysicalName;
                return null;
            }

            if (!string.IsNullOrWhiteSpace(definition.Schema))
            {
                var schema = definition.Schema.Trim();
                if (!string.IsNullOrWhiteSpace(definition.PhysicalName))
                {
                    var physicalKey = TableKey(schema, definition.PhysicalName);
                    if (_physicalLookup.TryGetValue(physicalKey, out var descriptor))
                    {
                        strategy = DynamicTableResolutionStrategy.PhysicalName;
                        return descriptor;
                    }

                    if (_effectiveLookup.TryGetValue(physicalKey, out descriptor))
                    {
                        strategy = DynamicTableResolutionStrategy.EffectiveName;
                        return descriptor;
                    }
                }

                if (!string.IsNullOrWhiteSpace(definition.EffectiveName))
                {
                    var effectiveKey = TableKey(schema, definition.EffectiveName);
                    if (_physicalLookup.TryGetValue(effectiveKey, out var descriptor))
                    {
                        strategy = DynamicTableResolutionStrategy.EffectiveName;
                        return descriptor;
                    }

                    if (_effectiveLookup.TryGetValue(effectiveKey, out descriptor))
                    {
                        strategy = DynamicTableResolutionStrategy.EffectiveName;
                        return descriptor;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.Module) &&
                !string.IsNullOrWhiteSpace(definition.LogicalName))
            {
                var moduleKey = ModuleKey(definition.Module, definition.LogicalName);
                if (_moduleLookup.TryGetValue(moduleKey, out var descriptor))
                {
                    strategy = DynamicTableResolutionStrategy.ModuleLogical;
                    return descriptor;
                }
            }

            strategy = DynamicTableResolutionStrategy.PhysicalName;
            return null;
        }

        private static string TableKey(string schema, string table)
        {
            return $"{schema?.Trim()}.{table?.Trim()}";
        }

        private static string ModuleKey(string module, string logical)
        {
            return $"{module?.Trim()}::{logical?.Trim()}";
        }
    }

    private sealed record EntityDescriptor(
        string Module,
        string LogicalName,
        string Schema,
        string PhysicalName,
        string EffectiveName);
}

public sealed record DynamicTableNameResolutionResult(
    DynamicEntityDataset Dataset,
    ImmutableArray<DynamicEntityTableReconciliation> Reconciliations)
{
    public static DynamicTableNameResolutionResult Empty(DynamicEntityDataset dataset)
        => new(dataset, ImmutableArray<DynamicEntityTableReconciliation>.Empty);

    public bool HasReconciliations => !Reconciliations.IsDefaultOrEmpty && Reconciliations.Length > 0;
}

public sealed record DynamicEntityTableReconciliation(
    string DatasetModule,
    string DatasetLogicalName,
    string DatasetSchema,
    string DatasetPhysicalName,
    string DatasetEffectiveName,
    string ResolvedModule,
    string ResolvedLogicalName,
    string ResolvedSchema,
    string ResolvedPhysicalName,
    string ResolvedEffectiveName,
    DynamicTableResolutionStrategy Strategy);

public enum DynamicTableResolutionStrategy
{
    PhysicalName,
    EffectiveName,
    ModuleLogical,
}
