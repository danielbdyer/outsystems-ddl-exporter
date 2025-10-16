using System;
using System.Collections.Generic;
using System.IO;

namespace Tests.Support;

public sealed class TempDirectory : IDisposable
{
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
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
