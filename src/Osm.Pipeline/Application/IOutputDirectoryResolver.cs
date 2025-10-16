using System;
using System.IO;

namespace Osm.Pipeline.Application;

public interface IOutputDirectoryResolver
{
    OutputDirectoryResolution Resolve(BuildSsdtOverrides overrides);
}

public sealed class OutputDirectoryResolver : IOutputDirectoryResolver
{
    public OutputDirectoryResolution Resolve(BuildSsdtOverrides overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        var candidate = string.IsNullOrWhiteSpace(overrides.OutputDirectory)
            ? "out"
            : overrides.OutputDirectory!;

        var resolved = Path.GetFullPath(candidate);
        Directory.CreateDirectory(resolved);

        var extractedModelPath = Path.Combine(resolved, "model.extracted.json");
        return new OutputDirectoryResolution(resolved, extractedModelPath);
    }
}

public sealed record OutputDirectoryResolution(string OutputDirectory, string ExtractedModelPath);
