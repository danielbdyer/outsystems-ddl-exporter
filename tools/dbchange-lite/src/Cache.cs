using System;
using System.IO;

namespace DbChangeLite;

public static class Cache
{
    public static string Package(Run run, string key, Func<string> build)
    {
        var cached = Path.Combine(run.Root, ".dbchange", "lite", "packages", Safe(key) + ".dacpac");
        if (File.Exists(cached))
        {
            return cached;
        }

        var built = build();
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        File.Copy(built, cached, overwrite: true);
        return cached;
    }

    public static string Extract(Run run, string environment, string schemaFingerprint, Func<byte[]> extract)
    {
        var cached = Path.Combine(run.Root, ".dbchange", "lite", "extracts", Safe(environment) + "-" + Safe(schemaFingerprint) + ".dacpac");
        if (!File.Exists(cached))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.WriteAllBytes(cached, extract());
        }

        return cached;
    }

    private static string Safe(string key)
    {
        var bare = key.StartsWith("sha256:", StringComparison.Ordinal) ? key[7..] : key;
        var chars = bare.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = char.IsAsciiLetterOrDigit(chars[i]) || chars[i] == '-' ? chars[i] : '_';
        }

        return new string(chars);
    }
}
