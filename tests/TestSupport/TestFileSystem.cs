using System;
using System.IO;

namespace Tests.Support;

public static class TestFileSystem
{
    public static void CopyDirectory(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source directory must be provided.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Destination directory must be provided.", nameof(destination));
        }

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Source directory '{source}' was not found.");
        }

        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, target, overwrite: true);
        }
    }
}
