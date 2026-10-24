using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DbChangeLite;

public static partial class Ssdt
{
    private static void StopFiles(string folder)
    {
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
        {
            File.WriteAllText(Path.Combine(folder, name), "<Project />\n");
        }

        File.WriteAllText(Path.Combine(folder, "Directory.Build.rsp"), "");
    }

    public static string FolderFingerprint(string folder)
    {
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .OrderBy(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            text.Append(Path.GetRelativePath(folder, file).Replace('\\', '/')).Append('\n').Append(DbChangeLite.Fingerprint.OfBytes(File.ReadAllBytes(file))).Append('\n');
        }

        return DbChangeLite.Fingerprint.OfString(text.ToString());
    }

    private static string Git(string repository, IReadOnlyList<string> args, CancellationToken cancel, string code, string subject)
    {
        var result = Exec("git", ["-C", repository, .. args], repository, TimeSpan.FromMinutes(5), cancel);
        return result.Code == 0 ? result.Output
            : throw Stop.Of("bad-arguments", code, subject, "git " + string.Join(' ', args) + " failed: " + (result.Error + result.Output).Trim().ReplaceLineEndings(" "),
                "Give a ref the SSDT repository's clone holds, fetched from its remote.");
    }

    public static (int Code, string Output, string Error) Exec(string file, IReadOnlyList<string> args, string directory, TimeSpan bound, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DACFX_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        var error = process.StandardError.ReadToEndAsync(cancel);
        if (!process.WaitForExit((int)bound.TotalMilliseconds) || cancel.IsCancellationRequested)
        {
            process.Kill(true);
            cancel.ThrowIfCancellationRequested();
            throw Stop.Of("configuration-refused", "process.timeout", file, file + " " + string.Join(' ', args.Take(2)) + " ran past " + bound.TotalMinutes + " minutes.", "Run the verb again when the machine is less busy.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
