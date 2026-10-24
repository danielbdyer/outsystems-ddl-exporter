using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace DbChangeLite;

public static partial class Ssdt
{
    public const string Targets = "Microsoft.Data.Tools.Schema.SqlTasks.targets";

    private static readonly Regex BuildError = new(@"\berror\s+[A-Z]+\d+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Build(string project, Run run)
    {
        if (!File.Exists(Path.Combine(run.Tool, Targets)))
        {
            throw Stop.Of("configuration-refused", "tool.targets-missing", run.Tool, "The tool folder " + run.Tool + " holds no " + Targets + ", so no SSDT project can build.",
                "Run ci/publish.sh (ci/publish.ps1 on Windows) and run dbchange-lite from the published folder, or set DBCHANGE_LITE_TOOL to it.");
        }

        StopFiles(run.Folder);
        var output = Path.Combine(run.Folder, "build-" + Path.GetFileNameWithoutExtension(project) + "-" + Guid.NewGuid().ToString("N")[..8]) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(output);
        var build = Exec("dotnet",
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false", "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true",
            "-p:NETCoreTargetsPath=" + run.Tool, "-p:SQLDBExtensionsRefPath=" + run.Tool, "-p:TargetFrameworkRootPath=" + Path.Combine(run.Tool, "refasm"),
            "-p:OutputPath=" + output, "-p:BaseIntermediateOutputPath=" + output + "obj" + Path.DirectorySeparatorChar, "-p:IntermediateOutputPath=" + output + "obj" + Path.DirectorySeparatorChar,
        ], Path.GetDirectoryName(project)!, TimeSpan.FromMinutes(10), run.Cancel);
        var package = Directory.EnumerateFiles(output, "*.dacpac", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => !Path.GetFileName(p).Equals("master.dacpac", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Equals("msdb.dacpac", StringComparison.OrdinalIgnoreCase));
        if (build.Code != 0 || package is null)
        {
            var lines = (build.Output + "\n" + build.Error).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var errors = lines.Where(l => BuildError.IsMatch(l)).Distinct().ToList();
            var said = errors.Count > 0 ? errors.Take(10) : (build.Error.Trim().Length > 0 ? build.Error : build.Output).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(3);
            throw new Stop(Contract.Of("build-failed"), Finding.Error("build.failed", Path.GetFileName(project),
                "The build of " + Path.GetFileName(project) + " failed with exit " + build.Code + ": " + string.Join(" | ", said),
                errors.Count > 0 ? "Fix each error at the file and line named, then run the verb again." : "Correct what dotnet reports, then run the verb again."));
        }

        return package;
    }

    private static void StopFiles(string folder)
    {
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
        {
            File.WriteAllText(Path.Combine(folder, name), "<Project />\n");
        }

        File.WriteAllText(Path.Combine(folder, "Directory.Build.rsp"), "");
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
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        bounded.CancelAfter(bound);
        try
        {
            process.WaitForExitAsync(bounded.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            cancel.ThrowIfCancellationRequested();
            throw Stop.Of("configuration-refused", "process.timeout", file, file + " " + string.Join(' ', args.Take(2)) + " ran past " + bound.TotalMinutes + " minutes.", "Run the verb again when the machine is less busy.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
