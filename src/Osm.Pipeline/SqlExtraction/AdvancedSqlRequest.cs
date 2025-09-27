using System.Collections.Generic;

namespace Osm.Pipeline.SqlExtraction;

public sealed class AdvancedSqlRequest
{
    public AdvancedSqlRequest(IReadOnlyList<string> moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames ?? throw new ArgumentNullException(nameof(moduleNames));
        IncludeSystemModules = includeSystemModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public IReadOnlyList<string> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool OnlyActiveAttributes { get; }
}
