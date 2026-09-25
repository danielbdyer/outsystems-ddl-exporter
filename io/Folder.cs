using System;
using System.IO;

namespace Estate.Io;

/// <summary>The upward search three modules once wrote by hand: the nearest folder at or above a start folder that holds what the caller looks for.</summary>
internal static class Folder
{
    /// <summary>
    /// The nearest folder at or above <paramref name="start"/> for which <paramref name="holds"/> is true, or null above the root. A
    /// folder the search cannot read answers false to a File.Exists and is passed by, as dotnet's own search for global.json passes it.
    /// </summary>
    public static string? Nearest(string start, Func<string, bool> holds)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
        {
            if (holds(directory.FullName))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
