using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

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

    public static MockFileSystem CreateMockFileSystem(IDictionary<string, MockFileData>? files = null)
    {
        var root = OperatingSystem.IsWindows() ? @"c:\" : "/";
        var fileSystem = new MockFileSystem(files ?? new Dictionary<string, MockFileData>(), root);
        fileSystem.Directory.SetCurrentDirectory(root);
        return fileSystem;
    }

    public static string Combine(IFileSystem fileSystem, params string[] segments)
    {
        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        var path = fileSystem.Directory.GetCurrentDirectory();
        foreach (var segment in segments)
        {
            path = fileSystem.Path.Combine(path, segment);
        }

        return path;
    }
}
