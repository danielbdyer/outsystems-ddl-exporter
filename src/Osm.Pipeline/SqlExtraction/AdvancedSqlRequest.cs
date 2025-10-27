using System.Collections.Immutable;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.SqlExtraction;

public sealed class AdvancedSqlRequest
{
    public AdvancedSqlRequest(
        ImmutableArray<ModuleName> moduleNames,
        bool includeSystemModules,
        bool includeInactiveModules,
        bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public ImmutableArray<ModuleName> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public bool OnlyActiveAttributes { get; }
}
