using System;
using System.IO;

namespace DbChange.Tests;

/// <summary>
/// A folder a test makes for itself and deletes when it is disposed: under the system's temporary folder, or, through the partial
/// linked into the projects that reference io, under the repository's .dbchange/ for work that must find dist/dbchange/ or the
/// repository's .gitignore above it. Disposing clears the read-only attribute git puts on its objects and, where files carry a Unix
/// mode, the modes a test withdrew, so the folder deletes however the test left it.
/// </summary>
internal sealed partial class ScratchFolder : IDisposable
{
    private ScratchFolder(string path) => Path = path;

    public string Path { get; }

    /// <summary>A new folder under the system's temporary folder, named for its purpose.</summary>
    public static ScratchFolder Temporary(string purpose) => new(Directory.CreateTempSubdirectory("dbchange-" + purpose + "-").FullName);

    /// <summary>The full path of <paramref name="relative"/> under the folder.</summary>
    public string Under(string relative) => System.IO.Path.Combine(Path, relative);

    /// <summary>A file at <paramref name="relative"/> under the folder, its folders made, holding <paramref name="text"/>; its full path.</summary>
    public string File(string relative, string text)
    {
        var path = Under(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A folder at <paramref name="relative"/> under the folder, made; its full path.</summary>
    public string Folder(string relative) => Directory.CreateDirectory(Under(relative)).FullName;

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
        {
            System.IO.File.SetAttributes(entry, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                System.IO.File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(Path, recursive: true);
    }

    public override string ToString() => Path;
}
