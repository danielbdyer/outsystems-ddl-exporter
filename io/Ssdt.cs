using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// The SSDT project and its package, read whole (V3_MILESTONES.md §2.2): Build runs the project's own build against the
/// published tool folder's DacFx targets (§1 fact 1) and reuses a package built before from the same inputs, Open reads a package from
/// its bytes, RefactorLog reads a refactorlog, and Elements reads a package's model into kernel Elements. An error's code names what
/// went wrong; cli/Contract.cs maps its category to the exit.
/// </summary>
/// <remarks>
/// No Visual Studio fallback: S1's windows-latest half answered that the committed route builds a classic project there.
/// Visual Studio's MSBuild with its own SSDT targets, found through vswhere and stamped with that build's DacFx release, lands only if S1's
/// laptop half finds the SSDT repository's project cannot build this way (WP 1.1).
/// </remarks>
public static partial class Ssdt
{
    /// <summary>
    /// What a build wrote: the package; the fingerprint of the inputs it read, the project's folder and the tool folder's build files; and
    /// those build files. The marker beside the package records the commit, the inputs, the targets and the package's own fingerprint.
    /// </summary>
    public sealed record Built(string Path, Fingerprint Inputs, BuildTargets Targets);

    /// <summary>
    /// The tool folder's build files a package depends on besides the project (§1 fact 1): the fingerprint of the SqlTasks targets and of
    /// the build task's assembly, and the DacFx release that task is. A package built by another release's task is not the one dbchange
    /// reads with its own DacFx, so <see cref="Of(string)"/> refuses a folder whose task is not the running release.
    /// </summary>
    public sealed record BuildTargets(Fingerprint Fingerprint, DacFxVersion TaskVersion)
    {
        /// <summary>The build task's assembly, whose file version names the DacFx release that builds a package.</summary>
        public const string Task = "Microsoft.Data.Tools.Schema.Tasks.Sql.dll";

        /// <summary>The targets a classic project imports from the tool folder.</summary>
        public const string Targets = "Microsoft.Data.Tools.Schema.SqlTasks.targets";

        /// <summary>The tool folder's build files, checked against the DacFx dbchange runs.</summary>
        public static Result<BuildTargets> Of(string toolFolder) => Of(toolFolder, DacFx.Version);

        /// <summary>
        /// The tool folder's build files against <paramref name="running"/>: toolchain.targets-mismatch when the folder holds no build
        /// task, when the task's assembly carries no file version, or when its release differs from the running one.
        /// </summary>
        internal static Result<BuildTargets> Of(string toolFolder, Result<DacFxVersion> running) => running.Bind(ours =>
        {
            var task = System.IO.Path.Combine(toolFolder, Task);
            var released = File.Exists(task) ? DacFx.ReleaseOf(task) : null;
            var mismatch = !File.Exists(task) ? toolFolder + " holds no build task, " + Task + ", and dbchange runs DacFx " + ours + "."
                : released is null ? "The tool folder's build task, " + task
                    + ", carries no file version, so the DacFx release that builds a package is unknown; dbchange runs DacFx " + ours + "."
                : released is Result<DacFxVersion>.Ok { Value: var built } && built.CompareTo(ours) != 0
                    ? "The tool folder's build task is DacFx " + built + " and dbchange runs DacFx " + ours + "."
                : null;
            return mismatch is not null
                ? new Error("toolchain.targets-mismatch", mismatch,
                    "Run ci/publish.sh, or ci/publish.ps1 on Windows, so the tool folder and dbchange carry one DacFx.")
                : released!.Map(version => new BuildTargets(Fingerprint.Of(string.Join('\n',
                    ((string[])[Targets, Task]).Where(file => File.Exists(System.IO.Path.Combine(toolFolder, file)))
                    .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(System.IO.Path.Combine(toolFolder, file)))))), version));
        });
    }

    /// <summary>The line in which the SqlTasks targets name the package they wrote; minimal verbosity prints it.</summary>
    private static readonly Regex PackageLine = new(@" -> (?<path>.+\.dacpac)\r?$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>
    /// An MSBuild error: its origin (a file and its position, or a tool), an optional subcategory, the code and the text, then the project
    /// in brackets.
    /// </summary>
    private static readonly Regex BuildError = new(
        @"^\s*(?<origin>.+?)(?<position>\(\d+(?:,\d+)*\))?\s*:\s*(?:[\w ]+ )?error (?<code>[A-Za-z]+\d+)\s*:"
            + @"\s*(?<text>.*?)(?:\s+\[[^\]]*\])?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The marker a build writes beside its package, last: the commit, the inputs, the targets, the package's file and its fingerprint.
    /// </summary>
    private const string Marker = "built.json";

    /// <summary>
    /// The tool folder: the one dbchange runs from, when it carries the targets; else the one DBCHANGE_TOOL names; else dist/dbchange/
    /// in the nearest directory at or above the working directory, as in dbchange's own repository once ci/publish has run in it.
    /// </summary>
    public static Result<string> Tool(string running, string? variable, string workingDirectory) =>
        Doctor.Tool(running).Remedy is null ? running
        : !string.IsNullOrEmpty(variable) && Doctor.Tool(variable) is { Remedy: not null } named
            ? new Error("tool.missing", "DBCHANGE_TOOL names " + variable + ", which is " + named.Found + ".",
                "Run ci/publish.sh, or ci/publish.ps1 on Windows, in dbchange's own repository "
                    + "and set DBCHANGE_TOOL to the dist/dbchange/ it writes, or unset DBCHANGE_TOOL; then run dbchange doctor.")
        : !string.IsNullOrEmpty(variable) ? variable
        : Io.Folder.Nearest(workingDirectory, folder => Doctor.Tool(Path.Combine(folder, "dist", "dbchange")).Remedy is null) is { } nearest
            ? Path.Combine(nearest, "dist", "dbchange")
        : new Error(
            "tool.missing",
            "dbchange does not run from a published tool folder, DBCHANGE_TOOL is unset, and no dist/dbchange/ lies at or above "
                + workingDirectory + ".",
            "Run ci/publish.sh, or ci/publish.ps1 on Windows, in dbchange's own repository, "
                + "or set DBCHANGE_TOOL to a published tool folder; then run dbchange doctor.");

    /// <summary>
    /// The project a ref's worktree holds, as a path from its root: the one named, else its one .sqlproj outside hidden folders, bin/ and
    /// obj/.
    /// </summary>
    public static Result<string> Project(string worktree, string? named)
    {
        List<string> found = named is not null ? [named] : Directory.EnumerateFiles(worktree, "*.sqlproj", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(worktree, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.')))
                .Order(StringComparer.Ordinal).ToList();
        return found is [var project] && File.Exists(Path.Combine(worktree, project)) ? project : new Error("build.no-project",
            found.Count > 1 ? "The repository holds several projects: " + string.Join(", ", found) + "."
                : "The repository holds no project at " + (named ?? "any path") + ".",
            "Name the .sqlproj to build with --project, by its path from the repository's root.");
    }

    /// <summary>
    /// How long a project's build may run: V3_MILESTONES.md gives M5's whole gate ten minutes, so a build still running then has failed the
    /// gate; a 300-table classic project builds in about two minutes.
    /// </summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The build's environment over dbchange's own: telemetry off; none of the SDK's first-run actions (no banner, no ASP.NET Core
    /// development certificate, no PATH change); English messages; UTF-8 output; no MSBuild server, as -nodeReuse:false keeps no node; and
    /// DBCHANGE_SQL withheld, since a project's own targets can read any variable as a property.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> BuildEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DACFX_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_UI_LANGUAGE"] = "en-US", ["DOTNET_CLI_FORCE_UTF8_ENCODING"] = "1",
        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false", ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["DBCHANGE_SQL"] = null,
    };

    /// <summary>
    /// The project a ref holds, built at the ref's commit (io/Git.At, then Build) under the repository's .dbchange/build/, with the tool
    /// folder dbchange runs from or the one <paramref name="toolVariable"/> (DBCHANGE_TOOL) names: the package the build wrote, and the
    /// commit.
    /// </summary>
    public static Result<(Built Built, string Commit)> Build(string repositoryRoot, string reference, string? project, string? toolVariable,
        string workingDirectory, TimeSpan? bound = null) =>
        Git.At(repositoryRoot, reference)
            .Bind(at => Project(at.Path, project).Bind(file => Tool(AppContext.BaseDirectory, toolVariable, workingDirectory)
            .Bind(tool => Build(at, file, tool, new LocalState(repositoryRoot).Builds, bound: bound))).Map(built => (built, at.Commit)));

    /// <summary>
    /// A project at a path, built under outputRoot/&lt;the first 16 digits of its inputs' fingerprint&gt;/. The inputs are the files under
    /// the project's folder and the tool folder's build files, so a file the project reads from outside its folder (a :r of ../shared.sql,
    /// a referenced project) changes the package and not the folder, and a build of it reuses the package built before (DF-10's limit).
    /// </summary>
    public static Result<Built> Build(string project, string toolFolder, string outputRoot, Runner? run = null,
        CancellationToken cancel = default, TimeSpan? bound = null) =>
        Build(project, toolFolder, outputRoot, run ?? Command.Run, null, bound ?? BuildTimeout, cancel);

    /// <summary>
    /// A project as a ref holds it: its path from the repository's root, found in the ref's worktree, built under outputRoot/&lt;the
    /// commit&gt;/&lt;the first 16 digits of the targets' fingerprint&gt;/.
    /// </summary>
    public static Result<Built> Build(Git.Worktree at, string project, string toolFolder, string outputRoot, Runner? run = null,
        CancellationToken cancel = default, TimeSpan? bound = null) =>
        Path.IsPathRooted(project)
            ? new Error("build.no-project", project + " is not a path from the repository's root, where a ref's project is found.",
                "Name the .sqlproj by its path from the repository's root.")
            : Build(Path.Combine(at.Path, project), toolFolder, outputRoot, run ?? Command.Run, at.Commit, bound ?? BuildTimeout, cancel);

    /// <summary>
    /// Builds a classic .sqlproj as §1 fact 1 does, with the SDK that dotnet --list-sdks, through run, lists: dotnet build against the tool
    /// folder's targets and reference assemblies, telemetry off, its output and intermediate files in a folder of their own, so nothing is
    /// written beside the project and two refs never share a folder. A folder whose marker names the same commit, inputs and targets, and
    /// whose package's bytes fingerprint as the marker says, is returned without running anything. Otherwise the build takes the folder's
    /// build.lock (io/FileLock), waiting for another dbchange process's build of it up to <paramref name="bound"/>, looks at the marker
    /// again, and builds into the folder emptied of all but the lock; the marker is written last, so a build cut off leaves none. A missing
    /// project, SDK band or tool folder, and a tool folder whose build task is another DacFx release, are each an error before anything
    /// builds.
    /// </summary>
    private static Result<Built> Build(string project, string toolFolder, string outputRoot, Runner run, string? commit, TimeSpan bound,
        CancellationToken cancel)
    {
        var (file, tool) = (Path.GetFullPath(project), Path.TrimEndingDirectorySeparator(Path.GetFullPath(toolFolder)));
        var directory = Path.GetDirectoryName(file)!;
        if (!File.Exists(file))
        {
            return new Error("build.no-project", "No project at " + file + ".",
                "Name the .sqlproj to build, by its path from the working directory.");
        }

        var targets = BuildTargets.Of(tool);
        (string Output, Fingerprint Inputs, BuildTargets Targets)? folder = targets is Result<BuildTargets>.Ok { Value: var known }
            ? Folder(outputRoot, commit, Inputs(directory, known), known) : null;
        if (folder is { } ready && Cached(ready.Output, commit, ready.Inputs, ready.Targets) is { } reused)
        {
            return reused;
        }

        if (Doctor.Sdk(directory, run, cancel) is { Remedy: { } install } sdk)
        {
            return new Error("sdk.missing", "dotnet build loads DacFx's net10.0 build task, and this machine has " + sdk.Found + ".",
                install.TrimEnd('.') + ", then run dbchange doctor.");
        }

        if (Doctor.Tool(tool) is { Remedy: not null } found)
        {
            return new Error("tool.missing", tool + " is " + found.Found + ".",
                "Run ci/publish.sh, or ci/publish.ps1 on Windows, in dbchange's own repository "
                    + "and build with the dist/dbchange/ it writes, then run dbchange doctor.");
        }

        if (targets is Result<BuildTargets>.Failed { Error: var mismatch })
        {
            return mismatch;
        }

        var (output, inputs, built) = folder!.Value;   // made whenever the targets were read
        return FileLock.Take(Path.Combine(output, "build.lock"), bound, cancel).Bind(held =>
        {
            using (held)
            {
                return Cached(output, commit, inputs, built) is { } cached ? cached
                    : Cleared(output).Bind(_ => Run(file, tool, output, run, bound, cancel))
                        .Bind(dacpac => Marked(output, commit, inputs, built, dacpac));
            }
        });
    }

    /// <summary>
    /// The output folder emptied of all but its lock before a build: a package or intermediate file left by a build that failed, was cut
    /// off, or was changed afterwards would otherwise look up to date to MSBuild and be kept.
    /// </summary>
    private static Result<string> Cleared(string output)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(output).EnumerateFileSystemInfos().Where(entry => entry.Name != "build.lock"))
            {
                if (entry is DirectoryInfo folder)
                {
                    folder.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }

            return output;
        }
        catch (Exception e) when (Write.FileSystemFailure(e))
        {
            return Write.Unwritable(output, e);
        }
    }

    /// <summary>
    /// The folder a build writes to: outputRoot/&lt;the commit&gt;/&lt;the targets' fingerprint&gt;/ for a ref, or outputRoot/&lt;the
    /// inputs' fingerprint&gt;/ for a path, each fingerprint's first 16 digits, so MSBuild's paths stay short on Windows.
    /// </summary>
    private static (string Output, Fingerprint Inputs, BuildTargets Targets) Folder(string outputRoot, string? commit, Fingerprint inputs,
        BuildTargets targets) =>
        (Path.Combine(Path.GetFullPath(outputRoot),
            commit is null ? inputs.ToString()[..16] : Path.Combine(commit, targets.Fingerprint.ToString()[..16])), inputs, targets);

    /// <summary>
    /// The package in <paramref name="output"/> when the marker there names this commit, these inputs and these targets, and the package's
    /// bytes fingerprint as the marker says; else null.
    /// </summary>
    private static Built? Cached(string output, string? commit, Fingerprint inputs, BuildTargets targets)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(output, Marker))) is JsonObject marker
                && (string?)marker["commit"] == commit && (string?)marker["inputs"] == inputs.ToString()
                && (string?)marker["targets"] == targets.Fingerprint.ToString()
                && (string?)marker["file"] is { } name && name == Path.GetFileName(name)
                && name.EndsWith(".dacpac", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(output, name))
                && Fingerprint.Of(File.ReadAllBytes(Path.Combine(output, name))).ToString() == (string?)marker["package"]
                ? new Built(Path.Combine(output, name), inputs, targets)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or FormatException)
        {
            return null;   // no marker, or one dbchange did not write: the folder is built again
        }
    }

    /// <summary>
    /// The package a build wrote, with the marker written beside it: the commit (null for a path), the inputs, the targets, the package's
    /// file and the fingerprint of its bytes.
    /// </summary>
    private static Result<Built> Marked(string output, string? commit, Fingerprint inputs, BuildTargets targets, string dacpac)
    {
        var name = Path.GetFileName(dacpac);
        if (!string.Equals(Path.GetFullPath(Path.Combine(output, name)), Path.GetFullPath(dacpac),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return new Built(dacpac, inputs, targets);   // a project that writes its package elsewhere builds each time
        }

        var marker = new JsonObject
        {
            ["commit"] = commit, ["inputs"] = inputs.ToString(), ["targets"] = targets.Fingerprint.ToString(), ["file"] = name,
            ["package"] = Fingerprint.Of(File.ReadAllBytes(dacpac)).ToString(),
        };
        return Write.Text(Path.Combine(output, Marker), marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n")
            .Map(_ => new Built(dacpac, inputs, targets));
    }

    /// <summary>
    /// dotnet build through io/Command into <paramref name="output"/>, its two streams read as UTF-8 whatever the console's code page (the
    /// SDK writes UTF-8 to a pipe) and joined to read MSBuild's lines; every path passed as a property escaped for MSBuild, which splits a
    /// property at ',' and ';' and unescapes %XX; stopped after <paramref name="bound"/> as build.timed-out, quoting the last lines read.
    /// The package's path.
    /// </summary>
    private static Result<string> Run(string project, string tool, string output, Runner run, TimeSpan bound, CancellationToken cancel)
    {
        var directory = Path.GetDirectoryName(project)!;
        var folder = Path.TrimEndingDirectorySeparator(output) + "/";
        var build = new Command("dotnet",
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false",
            "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true", "-p:NETCoreTargetsPath=" + Escaped(tool),
            "-p:SQLDBExtensionsRefPath=" + Escaped(tool),
            "-p:TargetFrameworkRootPath=" + Escaped(Path.Combine(tool, "refasm")),
            "-p:OutputPath=" + Escaped(folder), "-p:BaseIntermediateOutputPath=" + Escaped(folder + "obj/"),
            "-p:IntermediateOutputPath=" + Escaped(folder + "obj/"),
        ], bound) { Directory = directory, Environment = BuildEnvironment };
        var name = Path.GetFileName(project);
        return run(build, cancel) switch
        {
            Ran.NotFound notFound => new Error("sdk.missing",
                "dotnet build loads DacFx's net10.0 build task, and dotnet does not run here: " + notFound.Why,
                "Install the .NET SDK global.json names and put dotnet on the PATH, then run dbchange doctor."),
            Ran.TimedOut timedOut => new Error("build.timed-out",
                "dotnet build of " + name + " ran for " + Command.Written(bound)
                    + " without finishing, and dbchange stopped it; its last lines:\n"
                    + string.Join('\n', Last(timedOut.Output + timedOut.Errors)),
                "Run dotnet build " + name + " -v:n in " + directory + " to see where it stops."),
            Ran.Exited exited => Wrote(exited.Code, exited.Output + exited.Errors, name, directory),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// What the build wrote, read from MSBuild's lines: the package the SqlTasks targets named; build.failed with each error at its file
    /// and line, or the last lines when MSBuild named no error; or build.no-package when the build succeeded and named no package it wrote.
    /// </summary>
    private static Result<string> Wrote(int exit, string log, string name, string directory)
    {
        var errors = log.Split('\n').Select(line => BuildError.Match(line.TrimEnd('\r'))).Where(m => m.Success)
            .Select(m => Located(m, directory)).Distinct().ToList();
        var dacpac = PackageLine.Matches(log).Select(m => m.Groups["path"].Value).LastOrDefault();
        return exit != 0 || errors.Count > 0
            ? new Error("build.failed", "dotnet build of " + name + " failed:\n" + string.Join('\n', errors.Count > 0 ? errors : Last(log)),
                "Fix each error at the file and line it names, then build again.")
            : dacpac is not null && File.Exists(dacpac) ? dacpac
            : new Error("build.no-package",
                "dotnet build of " + name
                + " exited 0 and wrote no .dacpac; the project is not a database project, or its targets are not DacFx's.",
                "Set OutputType to Database in the project, and build against a published tool folder.");
    }

    /// <summary>The last twenty non-blank lines of a build's output, trimmed.</summary>
    private static IEnumerable<string> Last(string log) =>
        log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(20);

    /// <summary>
    /// A path as an MSBuild property value carries it exactly: '%', ';' and ',' written as %25, %3B and %2C, in that order, since MSBuild
    /// splits a value at the two separators and unescapes %XX (measured: MSB1006 for a comma, %41 read as A).
    /// </summary>
    private static string Escaped(string path) =>
        path.Replace("%", "%25", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal)
        .Replace(",", "%2C", StringComparison.Ordinal);

    /// <summary>
    /// An error as MSBuild writes it, with a file under the project named from the project's folder and the project's bracket dropped.
    /// </summary>
    private static string Located(Match error, string directory)
    {
        var origin = error.Groups["origin"].Value;
        var file = Path.IsPathFullyQualified(origin) && !Path.GetRelativePath(directory, origin).StartsWith("..", StringComparison.Ordinal)
            ? Path.GetRelativePath(directory, origin).Replace('\\', '/')
            : origin;
        return file + error.Groups["position"].Value + ": error " + error.Groups["code"].Value + ": " + error.Groups["text"].Value;
    }

    /// <summary>
    /// The fingerprint of what a build reads: each file under the project's folder by its relative path and its bytes, except
    /// under bin/, obj/ and hidden folders such as .git/ and .dbchange/; then the tool folder's build files, so a build against
    /// another release's targets never reuses a package built against these.
    /// </summary>
    private static Fingerprint Inputs(string directory, BuildTargets targets) => Fingerprint.Of(string.Join('\n',
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(directory, file))))
            .Append("tool " + targets.Fingerprint)));
}
