using System;
using System.Collections.Generic;
using System.IO;

namespace Tests.Support;

public sealed class TempDirectory : IDisposable
{
    private static readonly bool PreserveDirectories = string.Equals(
        Environment.GetEnvironmentVariable("OSM_KEEP_TEMP_DIRS"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public IEnumerable<string> GetFiles(string searchPattern, SearchOption searchOption = SearchOption.AllDirectories)
        => Directory.Exists(Path)
            ? Directory.GetFiles(Path, searchPattern, searchOption)
            : Array.Empty<string>();

    public void Dispose()
    {
        if (PreserveDirectories)
        {
            Console.WriteLine($"[TempDirectory] Preserving workspace at '{Path}'.");
            return;
        }

        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
