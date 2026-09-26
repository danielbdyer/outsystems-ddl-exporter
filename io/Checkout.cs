using System;
using System.IO;

namespace DbChange.Io;

/// <summary>
/// Where dbchange runs, read once per command: the repository root (EnvironmentsFile.Root of the working directory), the working
/// directory, the tool folder DBCHANGE_TOOL names, if any, and dbchange's own version, which the toolchain ledger's rows name. Every use
/// case takes it; cli makes it with <see cref="Here"/>, and a test describes one.
/// </summary>
public sealed record Checkout(string Root, string WorkingDirectory, string? Tool, string Version)
{
    /// <summary>The checkout of this process's working directory, for dbchange at <paramref name="version"/>.</summary>
    public static Checkout Here(string version)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        return new(EnvironmentsFile.Root(workingDirectory), workingDirectory, Environment.GetEnvironmentVariable("DBCHANGE_TOOL"), version);
    }
}
