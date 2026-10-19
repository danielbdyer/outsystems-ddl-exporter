using System;

namespace Osm.Pipeline.Application;

public interface IOutputDirectoryResolver
{
    string Resolve(BuildSsdtOverrides overrides);
}

public sealed class OutputDirectoryResolver : IOutputDirectoryResolver
{
    public string Resolve(BuildSsdtOverrides overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        return string.IsNullOrWhiteSpace(overrides.OutputDirectory) ? "out" : overrides.OutputDirectory!;
    }
}
