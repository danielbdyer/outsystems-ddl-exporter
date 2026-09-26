using System;
using System.IO;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// Where dbchange runs, read once per command: the repository root (EnvironmentsFile.Root of the working directory), the working
/// directory, the tool folder DBCHANGE_TOOL names, if any, and dbchange's own version, which the toolchain ledger's rows name. Every use
/// case takes it; cli makes it with <see cref="Here(string)"/>, and a test describes one.
/// </summary>
public sealed record Checkout(string Root, string WorkingDirectory, string? Tool, string Version)
{
    /// <summary>
    /// The checkout of this process's working directory, for dbchange at <paramref name="version"/>; file.no-working-directory where the
    /// working directory no longer exists, as on Linux and macOS after another program deleted it, or where this identity may not read it.
    /// </summary>
    public static Result<Checkout> Here(string version) => Here(version, Directory.GetCurrentDirectory);

    internal static Result<Checkout> Here(string version, Func<string> currentDirectory)
    {
        string workingDirectory;
        try
        {
            workingDirectory = currentDirectory();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("file.no-working-directory", "The working directory no longer exists, or this identity may not read it: " + e.Message.TrimEnd('.') + ".",
                "Change to the SSDT repository's folder, then run dbchange again.");
        }

        return new Checkout(EnvironmentsFile.Root(workingDirectory), workingDirectory, Environment.GetEnvironmentVariable("DBCHANGE_TOOL"), version);
    }
}
