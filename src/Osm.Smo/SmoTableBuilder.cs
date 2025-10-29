using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;

namespace Osm.Smo;

internal sealed class SmoTableBuilder
{
    private readonly PolicyDecisionSet _decisions;
    private readonly SmoBuildOptions _options;
    private readonly EntityEmissionIndex _entityLookup;
    private readonly IReadOnlyDictionary<ColumnCoordinate, string> _profileDefaults;
    private readonly IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> _foreignKeyReality;
    private readonly TypeMappingPolicy _typeMappingPolicy;

    public SmoTableBuilder(
        PolicyDecisionSet decisions,
        SmoBuildOptions options,
        EntityEmissionIndex entityLookup,
        IReadOnlyDictionary<ColumnCoordinate, string> profileDefaults,
        IReadOnlyDictionary<ColumnCoordinate, ForeignKeyReality> foreignKeyReality,
        TypeMappingPolicy typeMappingPolicy)
    {
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _entityLookup = entityLookup ?? throw new ArgumentNullException(nameof(entityLookup));
        _profileDefaults = profileDefaults ?? throw new ArgumentNullException(nameof(profileDefaults));
        _foreignKeyReality = foreignKeyReality ?? throw new ArgumentNullException(nameof(foreignKeyReality));
        _typeMappingPolicy = typeMappingPolicy ?? throw new ArgumentNullException(nameof(typeMappingPolicy));
    }

    public SmoTableDefinition Build(EntityEmissionContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var columns = SmoColumnBuilder.BuildColumns(context, _decisions, _profileDefaults, _typeMappingPolicy, _entityLookup);
        var indexes = SmoIndexBuilder.BuildIndexes(context, _decisions, _options.IncludePlatformAutoIndexes, _options.Format);
        var foreignKeys = SmoForeignKeyBuilder.BuildForeignKeys(context, _decisions, _entityLookup, _foreignKeyReality, _options.Format);
        var triggers = SmoTriggerBuilder.Build(context);
        var catalog = string.IsNullOrWhiteSpace(context.Entity.Catalog) ? _options.DefaultCatalogName : context.Entity.Catalog!;
        var moduleName = _options.SanitizeModuleNames ? ModuleNameSanitizer.Sanitize(context.ModuleName) : context.ModuleName;

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

    public static IComparer<SmoTableDefinition> DefinitionComparer { get; } = new SmoTableDefinitionComparer();

    private sealed class SmoTableDefinitionComparer : IComparer<SmoTableDefinition>
    {
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
}
