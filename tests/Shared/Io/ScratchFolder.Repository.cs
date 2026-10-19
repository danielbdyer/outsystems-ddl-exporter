using System;
using System.IO;
using DbChange.Budgets.Tests;

namespace DbChange.Tests;

/// <summary>The half of ScratchFolder that knows the repository, linked into the projects that reference io alone.</summary>
internal sealed partial class ScratchFolder
{
    /// <summary>
    /// A new folder under the repository's .dbchange/&lt;purpose&gt;/, named for this process and a random suffix, for work that must find
    /// dist/dbchange/ or the repository's .gitignore above it; git ignores .dbchange/, so nothing a test writes there is ever listed.
    /// </summary>
    public static ScratchFolder UnderRepository(string purpose) =>
        new(Directory.CreateDirectory(System.IO.Path.Combine(Repository.Root, ".dbchange", purpose, Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8])).FullName);
}
