using System;
using System.IO;
using System.Text;

namespace Osm.Pipeline.UatUsers;

internal static class IdempotentFileWriter
{
    public static void WriteAllText(string path, string contents, bool idempotent)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        contents ??= string.Empty;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (idempotent && File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8);
            if (string.Equals(existing, contents, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, contents, Encoding.UTF8);
    }
}
