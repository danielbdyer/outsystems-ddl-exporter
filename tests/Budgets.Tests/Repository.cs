using System;
using System.IO;

namespace Estate.Budgets.Tests;

/// <summary>The repository root: the nearest directory above the test assembly holding Estate.sln.</summary>
internal static class Repository
{
    public static string Root { get; } = Locate();

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Estate.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Estate.sln not found above " + AppContext.BaseDirectory);
    }
}
