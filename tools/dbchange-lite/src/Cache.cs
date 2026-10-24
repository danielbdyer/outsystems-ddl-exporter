using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DbChangeLite;

public static class Cache
{
    public static string Package(Run run, string key, Func<string> build)
    {
        var cached = Path.Combine(run.Root, ".dbchange", "lite", "packages", Safe(key) + ".dacpac");
        if (!File.Exists(cached))
        {
            Place(cached, temporary => File.Copy(build(), temporary, overwrite: true));
        }

        return cached;
    }

    public static string Extract(Run run, string environment, string schemaFingerprint, Func<byte[]> extract)
    {
        var cached = Path.Combine(run.Root, ".dbchange", "lite", "extracts", Safe(environment) + "-" + Safe(schemaFingerprint) + ".dacpac");
        if (!File.Exists(cached))
        {
            Place(cached, temporary => File.WriteAllBytes(temporary, extract()));
        }

        return cached;
    }

    public static string FolderFingerprint(string folder)
    {
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .OrderBy(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            text.Append(Path.GetRelativePath(folder, file).Replace('\\', '/')).Append('\n').Append(Fingerprint.OfBytes(File.ReadAllBytes(file))).Append('\n');
        }

        return Fingerprint.OfString(text.ToString());
    }

    private static void Place(string cached, Action<string> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        var temporary = cached + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".partial";
        write(temporary);
        File.Move(temporary, cached, overwrite: true);
    }

    private static string Safe(string key)
    {
        var bare = key.StartsWith("sha256:", StringComparison.Ordinal) ? key[7..] : key;
        return new string(bare.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
    }
}
