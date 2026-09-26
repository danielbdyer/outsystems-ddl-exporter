using System;
using System.Collections.Generic;

namespace Osm.Pipeline.Orchestration;

public sealed record SupplementalModelOptions(bool IncludeUsers, IReadOnlyList<string> Paths)
{
    public static SupplementalModelOptions Default { get; } = new(true, Array.Empty<string>());
}
