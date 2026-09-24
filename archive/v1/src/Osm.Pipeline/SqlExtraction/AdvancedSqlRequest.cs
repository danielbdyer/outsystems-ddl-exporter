using System.Collections.Immutable;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.SqlExtraction;

public sealed class AdvancedSqlRequest
{
    public AdvancedSqlRequest(
        ImmutableArray<ModuleName> moduleNames,
        bool includeSystemModules,
        bool includeInactiveModules,
        bool onlyActiveAttributes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? entityFilters = null)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
        OnlyActiveAttributes = onlyActiveAttributes;
        EntityFilters = entityFilters ?? ImmutableDictionary<string, IReadOnlyList<string>>.Empty;
    }

    public ImmutableArray<ModuleName> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public bool OnlyActiveAttributes { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> EntityFilters { get; }
}
