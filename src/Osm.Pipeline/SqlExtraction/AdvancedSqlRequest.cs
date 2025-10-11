using System.Collections.Immutable;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.SqlExtraction;

public sealed class AdvancedSqlRequest
{
    public AdvancedSqlRequest(ImmutableArray<ModuleName> moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public ImmutableArray<ModuleName> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool OnlyActiveAttributes { get; }
}
