using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Permission = Microsoft.SqlServer.Dac.Model.Permission;

namespace Estate.Io;

/// <summary>
/// The SSDT project and its package, read whole (V3_MILESTONES.md §2.2): Build runs the project's own build against the
/// published tool folder's DacFx targets (§1 fact 1) and reuses a package built before from the same inputs, Open reads a package from
/// its bytes, RefactorLog reads a refactorlog, and Elements reads a package's model into kernel Elements. An error's code names what
/// went wrong; cli/Contract.cs maps its category to the exit.
/// </summary>
/// <remarks>
/// No Visual Studio fallback: S1's windows-latest half answered that the committed route builds a classic project there.
/// Visual Studio's MSBuild with its own SSDT targets, found through vswhere and stamped with that build's DacFx release, lands only if S1's
/// laptop half finds the estate's project cannot build this way (WP 1.1).
/// </remarks>
public static class Ssdt
{
    /// <summary>
    /// What a build wrote: the package; the fingerprint of the inputs it read, the project's folder and the tool folder's build files; and those
    /// build files. The marker beside the package records the commit, the inputs, the targets and the package's own fingerprint.
    /// </summary>
    public sealed record Built(string Path, Fingerprint Inputs, BuildTargets Targets);

    /// <summary>
    /// The tool folder's build files a package depends on besides the project (§1 fact 1): the fingerprint of the SqlTasks targets and of the
    /// build task's assembly, and the DacFx release that task is. A package built by another release's task is not the one estate reads with
    /// its own DacFx, so <see cref="Of(string)"/> refuses a folder whose task is not the running release.
    /// </summary>
    public sealed record BuildTargets(Fingerprint Fingerprint, DacFxVersion TaskVersion)
    {
        /// <summary>The build task's assembly, whose file version names the DacFx release that builds a package.</summary>
        public const string Task = "Microsoft.Data.Tools.Schema.Tasks.Sql.dll";

        /// <summary>The targets a classic project imports from the tool folder.</summary>
        public const string Targets = "Microsoft.Data.Tools.Schema.SqlTasks.targets";

        /// <summary>The tool folder's build files, checked against the DacFx estate runs.</summary>
        public static Result<BuildTargets> Of(string toolFolder) => Of(toolFolder, DacFx.Version);

        /// <summary>
        /// The tool folder's build files against <paramref name="running"/>: toolchain.targets-mismatch when the folder holds no build task,
        /// when the task's assembly carries no file version, or when its release differs from the running one.
        /// </summary>
        internal static Result<BuildTargets> Of(string toolFolder, Result<DacFxVersion> running) => running.Bind(estate =>
        {
            var task = System.IO.Path.Combine(toolFolder, Task);
            var released = File.Exists(task) ? DacFx.ReleaseOf(task) : null;
            var mismatch = !File.Exists(task) ? toolFolder + " holds no build task, " + Task + ", and estate runs DacFx " + estate + "."
                : released is null ? "The tool folder's build task, " + task + ", carries no file version, so the DacFx release that builds a package is unknown; estate runs DacFx " + estate + "."
                : released is Result<DacFxVersion>.Ok { Value: var built } && built.CompareTo(estate) != 0 ? "The tool folder's build task is DacFx " + built + " and estate runs DacFx " + estate + "."
                : null;
            return mismatch is not null
                ? new Error("toolchain.targets-mismatch", mismatch, "Run ci/publish.sh, or ci/publish.ps1 on Windows, so the tool folder and estate carry one DacFx.")
                : released!.Map(version => new BuildTargets(Fingerprint.Of(string.Join('\n', ((string[])[Targets, Task]).Where(file => File.Exists(System.IO.Path.Combine(toolFolder, file)))
                    .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(System.IO.Path.Combine(toolFolder, file)))))), version));
        });
    }

    /// <summary>
    /// A package as DacFx reads it, opened once from its bytes (DECISIONS.md, 2026-09-24: never by path, so no assembly beside a dacpac
    /// loads into this process): where it was read from; DacFx's package, which a plan and a publish take; its model, which Elements reads,
    /// read with ThrowOnModelErrors off so a model with blocking errors still reads whole; the pre- and post-deploy scripts as the build
    /// inlined them and its pre-plan script, each null when it has none; its refactorlog's operations in file order; the SQLCMD variables it
    /// declares; and the platform it targets. Its elements are read once, when first asked for. Disposing it releases the package and the model.
    /// </summary>
    public sealed class Package : IDisposable
    {
        private readonly Lazy<Result<ModelElements>> elements;

        internal Package(string source, DacPackage dac, TSqlModel model, string? preDeploy, string? postDeploy, string? prePlan, IReadOnlyList<RefactorLogOperation> refactors,
            SortedArray<SqlCmdName> declared, Platform platform)
        {
            (Source, Dac, Model, PreDeploy, PostDeploy, PrePlan, Refactors, Declared, Platform) = (source, dac, model, preDeploy, postDeploy, prePlan, refactors, declared, platform);
            elements = new(() => Ssdt.ReadModel(this));
        }

        /// <summary>Where the package was read from, as a message names it: its path, or the target it was extracted from.</summary>
        public string Source { get; }

        internal DacPackage Dac { get; }

        public TSqlModel Model { get; }

        public string? PreDeploy { get; }

        public string? PostDeploy { get; }

        /// <summary>The script DacFx runs against a live target before it plans; a plan package to package runs none.</summary>
        public string? PrePlan { get; }

        public IReadOnlyList<RefactorLogOperation> Refactors { get; }

        /// <summary>The SQLCMD variables the package declares (DacPackage.SqlCmdVariables), each needing a value before a plan.</summary>
        public SortedArray<SqlCmdName> Declared { get; }

        public Platform Platform { get; }

        /// <summary>The package's model read into elements, once.</summary>
        public Result<ModelElements> Elements => elements.Value;

        public void Dispose()
        {
            Model.Dispose();
            Dac.Dispose();
        }
    }

    /// <summary>
    /// One refactorlog operation as SSDT writes it (an Operation element): its key; its name as written (Rename Refactor, Move Schema) and
    /// what estate reads it as; its ChangeDateTime as written; the element it acts on and that element's serialized type; the parent and
    /// its type, where the element has one; and the new name of a rename or the new schema of a move.
    /// </summary>
    public sealed record RefactorLogOperation(
        string Key, string Operation, string? ChangeDateTime, string ElementName, string? ElementType, string? ParentName, string? ParentType, string? NewName, string? NewSchema)
    {
        /// <summary>
        /// What the operation does to its element's key, read from what SSDT records with it: a NewName is a rename (Rename Refactor), a
        /// NewSchema a move to another schema (Move Schema), the two that move a key; any other operation moves nothing.
        /// </summary>
        public RefactorOperationKind Kind => (NewName, NewSchema) switch
        {
            (not null, _) => RefactorOperationKind.Rename,
            (_, not null) => RefactorOperationKind.MoveSchema,
            _ => RefactorOperationKind.Other,
        };
    }

    /// <summary>What a refactorlog operation does to an element's key: renames it, moves it to another schema, or nothing (any other operation SSDT records).</summary>
    public enum RefactorOperationKind
    {
        Rename,
        MoveSchema,
        Other,
    }

    private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>The line in which the SqlTasks targets name the package they wrote; minimal verbosity prints it.</summary>
    private static readonly Regex PackageLine = new(@" -> (?<path>.+\.dacpac)\r?$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>An MSBuild error: its origin (a file and its position, or a tool), an optional subcategory, the code and the text, then the project in brackets.</summary>
    private static readonly Regex BuildError = new(
        @"^\s*(?<origin>.+?)(?<position>\(\d+(?:,\d+)*\))?\s*:\s*(?:[\w ]+ )?error (?<code>[A-Za-z]+\d+)\s*:\s*(?<text>.*?)(?:\s+\[[^\]]*\])?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>The marker a build writes beside its package, last: the commit, the inputs, the targets, the package's file and its fingerprint.</summary>
    private const string Marker = "built.json";

    public static Result<string> Tool() => Tool(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ESTATE_TOOL"), Directory.GetCurrentDirectory());

    /// <summary>
    /// The tool folder: the one estate runs from, when it carries the targets; else the one ESTATE_TOOL names; else dist/estate/
    /// in the nearest directory at or above the working directory, as in the estate tool's repository once ci/publish has run in it.
    /// </summary>
    public static Result<string> Tool(string running, string? variable, string workingDirectory) =>
        Doctor.Tool(running).Remedy is null ? running
        : !string.IsNullOrEmpty(variable) && Doctor.Tool(variable) is { Remedy: not null } named
            ? new Error("tool.missing", "ESTATE_TOOL names " + variable + ", which is " + named.Found + ".",
                "Run ci/publish.sh, or ci/publish.ps1 on Windows, in the estate tool's repository and set ESTATE_TOOL to the dist/estate/ it writes, or unset ESTATE_TOOL; then run estate doctor.")
        : !string.IsNullOrEmpty(variable) ? variable
        : Nearest(new DirectoryInfo(workingDirectory)) is { } nearest ? nearest
        : new Error(
            "tool.missing",
            "estate does not run from a published tool folder, ESTATE_TOOL is unset, and no dist/estate/ lies at or above " + workingDirectory + ".",
            "Run ci/publish.sh, or ci/publish.ps1 on Windows, in the estate tool's repository, or set ESTATE_TOOL to a published tool folder; then run estate doctor.");

    /// <summary>dist/estate/ in the nearest directory at or above <paramref name="directory"/> where that is a published tool folder, else null.</summary>
    private static string? Nearest(DirectoryInfo? directory) => directory is null ? null
        : Path.Combine(directory.FullName, "dist", "estate") is var tool && Doctor.Tool(tool).Remedy is null ? tool : Nearest(directory.Parent);

    /// <summary>The project a ref's worktree holds, as a path from its root: the one named, else its one .sqlproj outside hidden folders, bin/ and obj/.</summary>
    public static Result<string> Project(string worktree, string? named)
    {
        List<string> found = named is not null ? [named] : Directory.EnumerateFiles(worktree, "*.sqlproj", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(worktree, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.'))).Order(StringComparer.Ordinal).ToList();
        return found is [var project] && File.Exists(Path.Combine(worktree, project)) ? project : new Error("build.no-project",
            found.Count > 1 ? "The repository holds several projects: " + string.Join(", ", found) + "." : "The repository holds no project at " + (named ?? "any path") + ".",
            "Name the .sqlproj to build with --project, by its path from the repository's root.");
    }

    /// <summary>How long a project's build may run: V3_MILESTONES.md gives M5's whole gate ten minutes, so a build still running then has failed the gate; a 300-table classic project builds in about two minutes.</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The build's environment over estate's own: telemetry off; none of the SDK's first-run actions (no banner, no ASP.NET Core development
    /// certificate, no PATH change); English messages; UTF-8 output; no MSBuild server, as -nodeReuse:false keeps no node; and ESTATE_SQL
    /// withheld, since a project's own targets can read any variable as a property.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> BuildEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DACFX_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_UI_LANGUAGE"] = "en-US", ["DOTNET_CLI_FORCE_UTF8_ENCODING"] = "1",
        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false", ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["ESTATE_SQL"] = null,
    };

    /// <summary>
    /// The project a ref holds, built at the ref's commit (io/Git.At, then Build) under the estate's .estate/build/, with the tool folder
    /// estate runs from or the one <paramref name="toolVariable"/> (ESTATE_TOOL) names: the package the build wrote, and the commit.
    /// </summary>
    public static Result<(Built Built, string Commit)> Build(string estateRoot, string reference, string? project, string? toolVariable, string workingDirectory, TimeSpan? bound = null) =>
        Git.At(estateRoot, reference).Bind(at => Project(at.Path, project).Bind(file => Tool(AppContext.BaseDirectory, toolVariable, workingDirectory)
            .Bind(tool => Build(at, file, tool, new LocalState(estateRoot).Builds, bound: bound))).Map(built => (built, at.Commit)));

    /// <summary>
    /// A project at a path, built under outputRoot/&lt;the first 16 digits of its inputs' fingerprint&gt;/. The inputs are the files under the
    /// project's folder and the tool folder's build files, so a file the project reads from outside its folder (a :r of ../shared.sql, a
    /// referenced project) changes the package and not the folder, and a build of it reuses the package built before (DF-10's limit).
    /// </summary>
    public static Result<Built> Build(string project, string toolFolder, string outputRoot, Runner? run = null, CancellationToken cancel = default, TimeSpan? bound = null) =>
        Build(project, toolFolder, outputRoot, run ?? Command.Run, null, bound ?? BuildTimeout, cancel);

    /// <summary>A project as a ref holds it: its path from the repository's root, found in the ref's worktree, built under outputRoot/&lt;the commit&gt;/&lt;the first 16 digits of the targets' fingerprint&gt;/.</summary>
    public static Result<Built> Build(Git.Worktree at, string project, string toolFolder, string outputRoot, Runner? run = null, CancellationToken cancel = default, TimeSpan? bound = null) =>
        Path.IsPathRooted(project)
            ? new Error("build.no-project", project + " is not a path from the repository's root, where a ref's project is found.", "Name the .sqlproj by its path from the repository's root.")
            : Build(Path.Combine(at.Path, project), toolFolder, outputRoot, run ?? Command.Run, at.Commit, bound ?? BuildTimeout, cancel);

    /// <summary>
    /// Builds a classic .sqlproj as §1 fact 1 does, with the SDK that dotnet --list-sdks, through run, lists: dotnet build against the tool
    /// folder's targets and reference assemblies, telemetry off, its output and intermediate files in a folder of their own, so nothing is
    /// written beside the project and two refs never share a folder. A folder whose marker names the same commit, inputs and targets, and
    /// whose package's bytes fingerprint as the marker says, is returned without running anything. Otherwise the build takes the folder's
    /// build.lock (io/FileLock), waiting for another estate process's build of it up to <paramref name="bound"/>, looks at the marker again,
    /// and builds into the folder emptied of all but the lock; the marker is written last, so a build cut off leaves none. A missing project,
    /// SDK band or tool folder, and a tool folder whose build task is another DacFx release, are each an error before anything builds.
    /// </summary>
    private static Result<Built> Build(string project, string toolFolder, string outputRoot, Runner run, string? commit, TimeSpan bound, CancellationToken cancel)
    {
        var (file, tool) = (Path.GetFullPath(project), Path.TrimEndingDirectorySeparator(Path.GetFullPath(toolFolder)));
        var directory = Path.GetDirectoryName(file)!;
        if (!File.Exists(file))
        {
            return new Error("build.no-project", "No project at " + file + ".", "Name the .sqlproj to build, by its path from the working directory.");
        }

        var targets = BuildTargets.Of(tool);
        (string Output, Fingerprint Inputs, BuildTargets Targets)? folder = targets is Result<BuildTargets>.Ok { Value: var known } ? Folder(outputRoot, commit, Inputs(directory, known), known) : null;
        if (folder is { } ready && Cached(ready.Output, commit, ready.Inputs, ready.Targets) is { } reused)
        {
            return reused;
        }

        if (Doctor.Sdk(directory, run, cancel) is { Remedy: { } install } sdk)
        {
            return new Error("sdk.missing", "dotnet build loads DacFx's net10.0 build task, and this machine has " + sdk.Found + ".", install.TrimEnd('.') + ", then run estate doctor.");
        }

        if (Doctor.Tool(tool) is { Remedy: not null } found)
        {
            return new Error("tool.missing", tool + " is " + found.Found + ".",
                "Run ci/publish.sh, or ci/publish.ps1 on Windows, in the estate tool's repository and build with the dist/estate/ it writes, then run estate doctor.");
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
                    : Cleared(output).Bind(_ => Run(file, tool, output, run, bound, cancel)).Bind(dacpac => Marked(output, commit, inputs, built, dacpac));
            }
        });
    }

    /// <summary>
    /// The output folder emptied of all but its lock before a build: a package or intermediate file left by a build that failed, was cut off,
    /// or was changed afterwards would otherwise look up to date to MSBuild and be kept.
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
    /// The folder a build writes to: outputRoot/&lt;the commit&gt;/&lt;the targets' fingerprint&gt;/ for a ref, or outputRoot/&lt;the inputs'
    /// fingerprint&gt;/ for a path, each fingerprint's first 16 digits, so MSBuild's paths stay short on Windows.
    /// </summary>
    private static (string Output, Fingerprint Inputs, BuildTargets Targets) Folder(string outputRoot, string? commit, Fingerprint inputs, BuildTargets targets) =>
        (Path.Combine(Path.GetFullPath(outputRoot), commit is null ? inputs.ToString()[..16] : Path.Combine(commit, targets.Fingerprint.ToString()[..16])), inputs, targets);

    /// <summary>The package in <paramref name="output"/> when the marker there names this commit, these inputs and these targets, and the package's bytes fingerprint as the marker says; else null.</summary>
    private static Built? Cached(string output, string? commit, Fingerprint inputs, BuildTargets targets)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(output, Marker))) is JsonObject marker
                && (string?)marker["commit"] == commit && (string?)marker["inputs"] == inputs.ToString() && (string?)marker["targets"] == targets.Fingerprint.ToString()
                && (string?)marker["file"] is { } name && name == Path.GetFileName(name) && name.EndsWith(".dacpac", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(output, name)) && Fingerprint.Of(File.ReadAllBytes(Path.Combine(output, name))).ToString() == (string?)marker["package"]
                ? new Built(Path.Combine(output, name), inputs, targets)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;   // no marker, or one estate did not write: the folder is built again
        }
    }

    /// <summary>The package a build wrote, with the marker written beside it: the commit (null for a path), the inputs, the targets, the package's file and the fingerprint of its bytes.</summary>
    private static Result<Built> Marked(string output, string? commit, Fingerprint inputs, BuildTargets targets, string dacpac)
    {
        var name = Path.GetFileName(dacpac);
        if (!string.Equals(Path.GetFullPath(Path.Combine(output, name)), Path.GetFullPath(dacpac), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return new Built(dacpac, inputs, targets);   // a project that writes its package elsewhere builds each time
        }

        var marker = new JsonObject
        {
            ["commit"] = commit, ["inputs"] = inputs.ToString(), ["targets"] = targets.Fingerprint.ToString(), ["file"] = name, ["package"] = Fingerprint.Of(File.ReadAllBytes(dacpac)).ToString(),
        };
        return Write.Text(Path.Combine(output, Marker), marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n").Map(_ => new Built(dacpac, inputs, targets));
    }

    /// <summary>
    /// dotnet build through io/Command into <paramref name="output"/>, its two streams read as UTF-8 whatever the console's code page (the SDK
    /// writes UTF-8 to a pipe) and joined to read MSBuild's lines; every path passed as a property escaped for MSBuild, which splits a property
    /// at ',' and ';' and unescapes %XX; stopped after <paramref name="bound"/> as build.timed-out, quoting the last lines read. The package's path.
    /// </summary>
    private static Result<string> Run(string project, string tool, string output, Runner run, TimeSpan bound, CancellationToken cancel)
    {
        var directory = Path.GetDirectoryName(project)!;
        var folder = Path.TrimEndingDirectorySeparator(output) + "/";
        var build = new Command("dotnet",
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false",
            "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true", "-p:NETCoreTargetsPath=" + Escaped(tool), "-p:SQLDBExtensionsRefPath=" + Escaped(tool),
            "-p:TargetFrameworkRootPath=" + Escaped(Path.Combine(tool, "refasm")),
            "-p:OutputPath=" + Escaped(folder), "-p:BaseIntermediateOutputPath=" + Escaped(folder + "obj/"), "-p:IntermediateOutputPath=" + Escaped(folder + "obj/"),
        ], bound) { Directory = directory, Environment = BuildEnvironment };
        var name = Path.GetFileName(project);
        return run(build, cancel) switch
        {
            Ran.NotFound notFound => new Error("sdk.missing", "dotnet build loads DacFx's net10.0 build task, and dotnet does not run here: " + notFound.Why,
                "Install the .NET SDK global.json names and put dotnet on the PATH, then run estate doctor."),
            Ran.TimedOut timedOut => new Error("build.timed-out",
                "dotnet build of " + name + " ran for " + Command.Written(bound) + " without finishing, and estate stopped it; its last lines:\n" + string.Join('\n', Last(timedOut.Output + timedOut.Errors)),
                "Run dotnet build " + name + " -v:n in " + directory + " to see where it stops."),
            Ran.Exited exited => Wrote(exited.Code, exited.Output + exited.Errors, name, directory),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// What the build wrote, read from MSBuild's lines: the package the SqlTasks targets named; build.failed with each error at its file and
    /// line, or the last lines when MSBuild named no error; or build.no-package when the build succeeded and named no package it wrote.
    /// </summary>
    private static Result<string> Wrote(int exit, string log, string name, string directory)
    {
        var errors = log.Split('\n').Select(line => BuildError.Match(line.TrimEnd('\r'))).Where(m => m.Success).Select(m => Located(m, directory)).Distinct().ToList();
        var dacpac = PackageLine.Matches(log).Select(m => m.Groups["path"].Value).LastOrDefault();
        return exit != 0 || errors.Count > 0
            ? new Error("build.failed", "dotnet build of " + name + " failed:\n" + string.Join('\n', errors.Count > 0 ? errors : Last(log)), "Fix each error at the file and line it names, then build again.")
            : dacpac is not null && File.Exists(dacpac) ? dacpac
            : new Error("build.no-package", "dotnet build of " + name + " exited 0 and wrote no .dacpac; the project is not a database project, or its targets are not DacFx's.",
                "Set OutputType to Database in the project, and build against a published tool folder.");
    }

    /// <summary>The last twenty non-blank lines of a build's output, trimmed.</summary>
    private static IEnumerable<string> Last(string log) => log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(20);

    /// <summary>A path as an MSBuild property value carries it exactly: '%', ';' and ',' written as %25, %3B and %2C, in that order, since MSBuild splits a value at the two separators and unescapes %XX (measured: MSB1006 for a comma, %41 read as A).</summary>
    private static string Escaped(string path) => path.Replace("%", "%25", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);

    /// <summary>An error as MSBuild writes it, with a file under the project named from the project's folder and the project's bracket dropped.</summary>
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
    /// under bin/, obj/ and hidden folders such as .git/ and .estate/; then the tool folder's build files, so a build against
    /// another release's targets never reuses a package built against these.
    /// </summary>
    private static Fingerprint Inputs(string directory, BuildTargets targets) => Fingerprint.Of(string.Join('\n',
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(directory, file))))
            .Append("tool " + targets.Fingerprint)));

    /// <summary>
    /// The package at <paramref name="path"/>, its bytes read once and opened as <see cref="Open(byte[], string)"/> opens them: package.unreadable
    /// for no file there or a file this identity cannot read.
    /// </summary>
    public static Result<Package> Open(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Error("package.unreadable", "No package at " + path + ".", "Name a .dacpac a build wrote, or build its project again.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("package.unreadable", path + " cannot be read: " + e.Message.TrimEnd('.') + ".", "Grant this identity read access to the package, or build its project again.");
        }

        return Open(bytes, path);
    }

    /// <summary>
    /// A package from its bytes, named <paramref name="source"/> in messages: its refactor.xml read through a zip archive over the bytes, then
    /// DacPackage.Load and TSqlModel.LoadFromDacpac each over a stream of the same bytes, so nothing opens the package by path. Bytes that
    /// are not a package DacFx reads are package.unreadable, quoting DacFx's messages; a refactor.xml that is not a refactorlog is
    /// refactorlog.unreadable, naming the package.
    /// </summary>
    internal static Result<Package> Open(byte[] bytes, string source)
    {
        IReadOnlyList<RefactorLogOperation> refactors;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            using var log = zip.GetEntry("refactor.xml")?.Open();
            refactors = log is null ? [] : Operations(log);
        }
        catch (InvalidDataException e)
        {
            return Unreadable(source, e.Message);
        }
        catch (XmlException e)
        {
            return new Error("refactorlog.unreadable", "refactor.xml inside " + source + " is not a refactorlog SSDT reads: " + e.Message,
                "Restore the project's refactorlog from git, then build the package again.");
        }

        var loaded = DacFx.Guard(() => DacPackage.Load(new MemoryStream(bytes, writable: false), DacSchemaModelStorageType.Memory, FileAccess.Read), failure => Unreadable(source, Quoted(failure)));
        if (loaded is not Result<DacPackage>.Ok { Value: var dac })
        {
            return ((Result<DacPackage>.Failed)loaded).Error;
        }

        var modelled = DacFx.Guard(() => TSqlModel.LoadFromDacpac(new MemoryStream(bytes, writable: false),
            new ModelLoadOptions(DacSchemaModelStorageType.Memory, loadAsScriptBackedModel: false) { ThrowOnModelErrors = false }), failure => Unreadable(source, Quoted(failure)));
        var made = modelled.Bind(model => Result.All(dac.SqlCmdVariables.Select(name => SqlCmdName.Of(source + " declares a SQLCMD variable that", name))).Bind(declared =>
            Platform.Of(dac.TargetPlatform.ToString()).Map(platform =>
                new Package(source, dac, model, Text(dac.PreDeploymentScript), Text(dac.PostDeploymentScript), Text(dac.PrePlanScript), refactors, SortedArray.Of(declared), platform))));
        if (made is Result<Package>.Failed)
        {
            (modelled as Result<TSqlModel>.Ok)?.Value.Dispose();
            dac.Dispose();
        }

        return made;
    }

    /// <summary>A .refactorlog file's operations, read as Open reads the copy a build puts in the package.</summary>
    public static Result<IReadOnlyList<RefactorLogOperation>> RefactorLog(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Result.Ok(Operations(file));
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            return new Error("refactorlog.unreadable", path + " is not a refactorlog SSDT reads: " + e.Message, "Restore the file from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
        }
    }

    /// <summary>
    /// The operations of a refactorlog, in file order. A build's copy leaves the root element outside the namespace its
    /// operations carry, so the operations are found by name wherever they sit; one with no key, name or element makes the whole file unreadable.
    /// </summary>
    private static IReadOnlyList<RefactorLogOperation> Operations(Stream log)
    {
        using var reader = XmlReader.Create(log, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return [.. XDocument.Load(reader).Descendants(Dac + "Operation").Select(operation =>
        {
            string? Property(string name) => (string?)operation.Elements(Dac + "Property").FirstOrDefault(p => (string?)p.Attribute("Name") == name)?.Attribute("Value");
            string Required(string? value, string what) => value ?? throw new XmlException("A refactorlog operation has no " + what + ".");
            return new RefactorLogOperation(
                Required((string?)operation.Attribute("Key"), "Key"), Required((string?)operation.Attribute("Name"), "Name"), (string?)operation.Attribute("ChangeDateTime"),
                Required(Property("ElementName"), "ElementName"), Property("ElementType"), Property("ParentElementName"), Property("ParentElementType"), Property("NewName"), Property("NewSchema"));
        })];
    }

    private static Error Unreadable(string source, string why) => new Error("package.unreadable", source + " is not a package DacFx reads: " + why,
        "Name a .dacpac a build wrote, or build its project again.");

    /// <summary>A DacFx failure as a message quotes it: its messages, or the chain's text where it has none.</summary>
    private static string Quoted(DacFx.DacFxFailure failure) => failure.Messages.Count > 0 ? string.Join(' ', failure.Messages.Select(m => m.ToString())) : failure.Text;

    private static string? Text(Stream? script)
    {
        using var reader = script is null ? null : new StreamReader(script);
        return reader?.ReadToEnd();
    }

    /// <summary>
    /// The properties Elements leaves out, each holding a password or a secret: SQL Server never returns one, so DacFx makes up a new
    /// value for a login's password each time it loads a model from a database, and a package carries whatever its script wrote. Besides the passwords and
    /// credential secrets: a symmetric key's KEY_SOURCE and IDENTITY_VALUE, from which SQL Server derives the key; a linked server's
    /// provider string (sp_addlinkedserver's @provstr) and an external data source's CONNECTION_OPTIONS, each a connection string
    /// whose documented form carries PWD=, left out whole, so an edit to either is not seen. DacFx 170.5.96's metadata marks none of
    /// them as secret, so the list is kept here; Io.Tests' ModelElementsTests plants each, and lists every other string-typed property DacFx declares
    /// with the reason it is not a secret. DacFx fills these static fields when its model schema initializes, which the first
    /// TSqlModel a process makes does, so the list is made on first use, after one.
    /// </summary>
    public static IReadOnlySet<ModelPropertyClass> Secrets => Secret.Value;

    private static readonly Lazy<HashSet<ModelPropertyClass>> Secret = new(() =>
    {
        new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions()).Dispose();
        return
        [
            Login.Password, User.Password, ApplicationRole.Password, MasterKey.Password, AsymmetricKey.Password, SymmetricKeyPassword.Password,
            Certificate.EncryptionPassword, Certificate.PrivateKeyDecryptionPassword, Certificate.PrivateKeyEncryptionPassword,
            Credential.Secret, DatabaseCredential.Secret, LinkedServerLogin.LinkedServerPassword, SignatureEncryptionMechanism.Password,
            SymmetricKey.KeySource, SymmetricKey.IdentityValue, LinkedServer.ProviderString, ExternalDataSource.ConnectionOptions,
        ];
    });

    /// <summary>
    /// A model read whole into elements (§2.1 rule 1); the renames its package's refactorlog records, as Change.Between takes them, which a
    /// database's model has none of; and the errors DacFx found loading the model (TSqlModel.GetModelErrors: a script it cannot parse), each
    /// a note and never a refusal, since the rest of the model reads.
    /// </summary>
    public sealed record ModelElements(SortedArray<Element> Elements, SortedArray<Rename> Renames, SortedArray<DacFxMessage> Errors = default)
    {
        /// <summary>A model.error note for each error, naming the model's source.</summary>
        public IEnumerable<Finding> Notes(string source) => Errors.Select(error => Finding.Note("model.error", source,
            "DacFx found " + error + " in the model of " + source + "; the rest of the model reads whole, and the object the error concerns may lack what DacFx could not read."));
    }

    /// <summary>
    /// The collation a model's names compare under (decision 2.26): its DatabaseOptions element's Collation property, which Elements reads
    /// from a package as its project's default collation and from a database as the database's; the comparison that follows no database
    /// when the model has none.
    /// </summary>
    public static Result<Collation> CollationOf(SortedArray<Element> elements) =>
        elements.FirstOrDefault(e => e.Key.Type == "DatabaseOptions")?["Collation"] is Value.Text { Content: var name } ? Collation.Of(name) : Result.Ok(Collation.CaseSensitive);

    /// <summary>The package's model read into elements, with its deploy scripts and its refactorlog's operations, as <see cref="ReadModel(TSqlModel, string?, string?, IReadOnlyList{RefactorLogOperation})"/> reads them.</summary>
    public static Result<ModelElements> ReadModel(Package package) => ReadModel(package.Model, package.PreDeploy, package.PostDeploy, package.Refactors);

    /// <summary>
    /// A model read into elements, one element for each deploy script and each refactorlog operation, and the operations' renames. An
    /// operation's element type, written as DacFx serializes it (SqlSimpleColumn), is read through the one map of DacFx's types (ModelTypes),
    /// so a type the model no longer holds still keys, and the rename to it is a drop and an add.
    /// </summary>
    internal static Result<ModelElements> ReadModel(TSqlModel model, string? preDeploy, string? postDeploy, IReadOnlyList<RefactorLogOperation> refactors) => ModelObjects(model).Bind(objects =>
    {
        var scripts = new[] { preDeploy is { } pre ? Element.PreDeploy(LineEndings.Lf(pre)) : null, postDeploy is { } post ? Element.PostDeploy(LineEndings.Lf(post)) : null }.OfType<Element>();
        return Result.All(refactors.Select(Entry)).Bind(entries =>
            Result.All(refactors.Where(r => r.Kind != RefactorOperationKind.Other).Select(Renaming))
                .Map(renames => new ModelElements(SortedArray.Of(objects.Select(o => o.Element).Concat(scripts).Concat(entries)), SortedArray.Of(renames), Errors(model))));
    });

    /// <summary>
    /// The errors DacFx found loading a model (TSqlModel.GetModelErrors): measured on DacFx 170.5.96, a script it cannot parse (SQL46010);
    /// an unresolved reference, such as a user whose login is gone, is Validate's to find, which is not called, and a database whose user
    /// lost its login extracts that user with no login.
    /// </summary>
    private static SortedArray<DacFxMessage> Errors(TSqlModel model) => SortedArray.Of(model.GetModelErrors().Select(e =>
        new DacFxMessage(e.Severity == ModelErrorSeverity.Warning ? DacFxMessageType.Warning : DacFxMessageType.Error, e.Prefix, e.ErrorCode, e.Message, null)));

    /// <summary>
    /// A model read whole, no code per type (§1 fact 6): each user-defined top-level object but the two grants to public SQL Server
    /// makes in every new database (<see cref="Default"/>) and, depth first, what its composing relationships reach, each object
    /// once, with every property its type declares that the model's platform carries (a Sql110 model's column has no GraphType) but a
    /// password or a secret (<see cref="Secrets"/>), a module's Definition as
    /// written too, and every relationship's targets in DacFx's order; a target's own property (an index column's Ascending) is
    /// Relationship[position].Property. A key is the name while it has one or two parts and nothing
    /// composes the object; else the parent's key (the composer, or the hierarchical parent: an index's table, a grant's securable)
    /// and the name parts the parent's name does not hold. An unnamed default, check, unique or foreign key constraint on exactly one
    /// column is keyed under that column by the relationship that names it (TargetColumn, ExpressionDependencies, Columns), so it
    /// moves with the column's rename, and a column added beside it, or a table whose columns a database holds in another order
    /// (a publish under IgnoreColumnOrder appends a column the project inserts), leaves its key as it was. Any other unnamed object
    /// (a primary key, a constraint on several columns) is keyed by the relationship to its table. Several unnamed objects of one
    /// type under one parent are numbered from 1 in the order of the names they reference, then of their own values, never by a
    /// generated name or DacFx's order; a package and the database it was published to hold the same names, so they number alike,
    /// and a rename of a column that a constraint on several columns references can renumber that constraint and its siblings. SQL
    /// Server normalizes a check's text, so two checks on one column may number apart in a package and its database. Two objects
    /// keyed alike are the error model.duplicate-key; an unresolved reference is keyed as the type Unresolved. Models are compared only between like sources and, for databases, like identities:
    /// SQL Server shows a server-scoped login only to a reader with permission on it (sysadmin, VIEW ANY DEFINITION, or its own), and
    /// a db_datareader login holding VIEW DEFINITION read Query Store's database options differently from sa when measured on 2026-09-24.
    /// </summary>
    public static Result<SortedArray<Element>> ReadModel(TSqlModel model) => ModelObjects(model).Map(objects => SortedArray.Of(objects.Select(o => o.Element)));

    /// <summary>
    /// A grant SQL Server makes in every new database, copying it from model: VIEW ANY COLUMN ENCRYPTION KEY DEFINITION and VIEW ANY
    /// COLUMN MASTER KEY DEFINITION to public, which Always Encrypted's client drivers read. A database holds both whatever its project
    /// says, and a project imported from a database may hold them too, so Elements leaves both out of every model; a REVOKE of either
    /// is therefore not seen.
    /// </summary>
    private static bool Default(TSqlObject o) => o.ObjectType == Permission.TypeClass
        && o.GetProperty<PermissionAction>(Permission.PermissionAction) == PermissionAction.Grant && !o.GetProperty<bool>(Permission.WithGrantOption)
        && o.GetProperty<PermissionType>(Permission.PermissionType) is PermissionType.ViewAnyColumnEncryptionKeyDefinition or PermissionType.ViewAnyColumnMasterKeyDefinition
        && o.GetReferenced(Permission.Grantee, DacQueryScopes.All).ToArray() is [var grantee] && grantee.Name.Parts is [var role] && string.Equals(role, "public", StringComparison.OrdinalIgnoreCase);

    /// <summary>Each object of the model as an element, with the object's name as model.xml writes it ([dbo].[Customer].[Email]), null for an unnamed object.</summary>
    private static Result<IReadOnlyList<(Element Element, string? Name)>> ModelObjects(TSqlModel model)
    {
        var composers = new Dictionary<TSqlObject, (TSqlObject Parent, string Relationship)>();
        var reached = new HashSet<TSqlObject>();
        void Descend(TSqlObject o)
        {
            var composed = o.ObjectType.Relationships.Where(r => r.Type == RelationshipType.Composing).SelectMany(r => o.GetReferenced(r, DacQueryScopes.All).Select(c => (r, c)));
            foreach (var (r, child) in reached.Add(o) ? composed : [])
            {
                composers[child] = (o, r.Name);
                Descend(child);
            }
        }

        model.GetObjects(DacQueryScopes.UserDefined).Where(o => !Default(o)).ToList().ForEach(Descend);

        // The relationship through which each type of unnamed constraint that can sit on one column names its columns.
        var on = new Dictionary<ModelTypeClass, ModelRelationshipClass>
        {
            [DefaultConstraint.TypeClass] = DefaultConstraint.TargetColumn, [CheckConstraint.TypeClass] = CheckConstraint.ExpressionDependencies,
            [UniqueConstraint.TypeClass] = UniqueConstraint.Columns, [ForeignKeyConstraint.TypeClass] = ForeignKeyConstraint.Columns,
        };
        (TSqlObject? Parent, string Relationship) Anchor(TSqlObject o) => composers.TryGetValue(o, out var composer) ? composer
            : !o.Name.HasName && on.TryGetValue(o.ObjectType, out var through)
                && o.GetReferenced(through, DacQueryScopes.All).Where(c => c.ObjectType == Column.TypeClass).Distinct().ToArray() is [var column]
                ? (column, through.Name)
            : o.GetParent(DacQueryScopes.All) is not { } parent ? (null, o.ObjectType.Name)
            : (parent, o.ObjectType.Relationships.FirstOrDefault(r => r.Type == RelationshipType.Hierarchical && o.GetReferenced(r, DacQueryScopes.All).Contains(parent))?.Name ?? o.ObjectType.Name);

        string References(TSqlObject o) => string.Join('\n', o.ObjectType.Relationships.Where(r => r.Type != RelationshipType.Composing)
            .SelectMany(r => o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).Select(i => r.Name + " " + i.ObjectName)));
        var secrets = Secrets;

        // A property the model's platform does not carry reads as DacFx's default (measured: a Sql110 column's GraphType 0 and IsHidden
        // false; four of DatabaseOptions' on Sql160), a value SQL Server never held, so it is not read.
        var platform = Enum.TryParse<TSqlPlatforms>(model.Version.ToString(), out var carried) ? carried : TSqlPlatforms.All;
        IEnumerable<ModelPropertyClass> Kept(IEnumerable<ModelPropertyClass> declared) => declared.Where(p => !secrets.Contains(p) && (p.SupportedPlatforms & platform) != 0);
        string Values(TSqlObject o) => string.Join('\n', Kept(o.ObjectType.Properties).Select(p => p.Name + " " + ValueOf(() => o.GetProperty(p), p.DataType)));
        var unnamed = reached.Where(o => !o.Name.HasName).GroupBy(o => (Anchor: Anchor(o), Type: o.ObjectType.Name))
            .SelectMany(g => g.OrderBy(References, StringComparer.Ordinal).ThenBy(Values, StringComparer.Ordinal)
                .Select((o, i) => (Object: o, Name: g.Count() == 1 ? g.Key.Anchor.Relationship : string.Create(CultureInfo.InvariantCulture, $"{g.Key.Anchor.Relationship} {i + 1}"))))
            .ToDictionary(u => u.Object, u => u.Name);

        var keys = new Dictionary<TSqlObject, Result<ElementKey>>();
        Result<ElementKey> Key(TSqlObject o) => keys.TryGetValue(o, out var key) ? key : keys[o] = KeyOf(o);
        Result<ElementKey> KeyOf(TSqlObject o)
        {
            string[] own = o.Name.HasName ? [.. o.Name.Parts] : [unnamed.GetValueOrDefault(o) ?? Anchor(o).Relationship];
            var parent = o.Name.HasName && own.Length <= 2 && !composers.ContainsKey(o) ? null : Anchor(o).Parent;
            return parent is null ? ModelTypes.Key(o.ObjectType.Name, own, null) : Key(parent).Bind(home => ModelTypes.Key(o.ObjectType.Name, ModelTypes.Beneath(own, [.. parent.Name.Parts]), home));
        }

        Result<Element> ElementOf(TSqlObject o)
        {
            var relationships = o.ObjectType.Relationships.Select(r => (Class: r, Instances: o.GetReferencedRelationshipInstances(r, DacExternalQueryScopes.All).ToArray())).ToArray();
            var properties = Kept(o.ObjectType.Properties).Select(p => (p.Name, Value: ValueOf(() => o.GetProperty(p), p.DataType)))
                .Append((Name: "Definition", Value: Module(o.ObjectType) ? ScriptOf(() => o.TryGetScript(out var script) ? script : null) : null))
                .Concat(relationships.SelectMany(r => r.Instances.SelectMany((i, n) => Kept(r.Class.Properties).Select(p =>
                    (Name: string.Create(CultureInfo.InvariantCulture, $"{r.Class.Name}[{n}].{p.Name}"), Value: ValueOf(() => i.GetProperty(p), p.DataType))))))
                .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, p.Value!));
            var targets = relationships.Select(r => Result.All(r.Instances.Select(i => i.Object is { } target ? Key(target) : ModelTypes.Key("Unresolved", [.. i.ObjectName.ExternalParts ?? [], .. i.ObjectName.Parts], null)))
                .Map(to => Element.Relationship.Of(r.Class.Name, to)));
            return Key(o).Bind(key => Result.All(targets).Bind(rs => Element.Of(key, properties, rs)));
        }

        return Result.All(reached.Select(o => ElementOf(o).Map(e => (Element: e, Name: o.Name.HasName ? ModelTypes.Key(o.ObjectType.Name, [.. o.Name.Parts], null).Match<string?>(k => k.Path, _ => null) : null))))
            .Bind(elements => elements.GroupBy(e => e.Element.Key).FirstOrDefault(g => g.Count() > 1) is not { } alike ? Result.Ok(elements) : new Error("model.duplicate-key",
                $"{alike.Count()} {alike.Key.Type} objects of the model are keyed alike, as {alike.Key}: {string.Join(", ", alike.Select(e => e.Name ?? "unnamed"))}.",
                "Report the model's source with this error: a key names one object, so io/Ssdt.ReadModel keys this type ambiguously, a defect in estate."));
    }

    /// <summary>
    /// An object's property as the kernel's closed Value, or null where DacFx cannot read it, and Elements skips it. A property DacFx
    /// declares as its SqlScriptProperty (a default's or a check's expression, a computed column's, an extended property's value) is
    /// T-SQL text and reads as a Value.Script, which the printer alone prints (decision 2.27). An enumeration reaches an untyped read
    /// as its integer, so the declared type names its member; any other type (a double, as a spatial index's bounds) is its
    /// invariant string, so no value is dropped for its type. Text has CRLF and a lone CR made LF.
    /// </summary>
    public static Value? ValueOf(TSqlObject o, ModelPropertyClass property) => ValueOf(() => o.GetProperty(property), property.DataType);

    /// <summary>A module's script (a procedure's, a function's, a trigger's body) as a Value.Script, or null where DacFx gives none.</summary>
    private static Value? ScriptOf(Func<string?> read) => read() is { } script ? new Value.Script(LineEndings.Lf(script)) : null;

    private static Value? ValueOf(Func<object?> read, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        try
        {
            var value = read();
            return value switch
            {
                null => new Value.Null(),
                bool b => new Value.Boolean(b),
                string s when type.Name == "SqlScriptProperty" => new Value.Script(LineEndings.Lf(s)),
                string s => new Value.Text(LineEndings.Lf(s)),
                Enum or sbyte or byte or short or ushort or int or uint or long when type.IsEnum => new Value.Enumeration(type.Name, Enum.Format(type, Enum.ToObject(type, value), "G")),
                sbyte or byte or short or ushort or int or uint or long => new Value.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                IFormattable f => new Value.Text(f.ToString(null, CultureInfo.InvariantCulture)),
                _ => new Value.Text(LineEndings.Lf(value.ToString() ?? "")),
            };
        }
        catch (DacModelException)
        {
            return null;
        }
    }

    /// <summary>A module, whose body DacFx reads for BodyDependencies and holds in no property of its script type: a procedure, a function, a trigger (a view's is SelectStatement).</summary>
    private static bool Module(ModelTypeClass type) => type.Relationships.Any(r => r.Name == "BodyDependencies") && type.Properties.All(p => p.DataType.Name != "SqlScriptProperty");

    /// <summary>A refactorlog operation as an element: its key, and as text each attribute and property the file gives it.</summary>
    private static Result<Element> Entry(RefactorLogOperation r) => Element.RefactorLogEntry(r.Key, new (string Name, string? Value)[] {
        ("Operation", r.Operation), ("ChangeDateTime", r.ChangeDateTime), ("ElementName", r.ElementName), ("ElementType", r.ElementType),
        ("ParentElementName", r.ParentName), ("ParentElementType", r.ParentType), ("NewName", r.NewName), ("NewSchema", r.NewSchema) }
        .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, new Value.Text(p.Value!))));

    /// <summary>
    /// An operation's rename: its element's key (past two parts, under its parent's, as Elements keys it) to the key its NewName or NewSchema
    /// gives, each serialized type read through ModelTypes and each name through ScriptDom.
    /// </summary>
    private static Result<Rename> Renaming(RefactorLogOperation r) =>
        Parts(r.ElementName).Bind(parts => parts.Length > 2 && r.ParentName is { } parent
                ? Parts(parent).Bind(home => ModelTypes.Key(TypeOf(r.ParentType), home, null).Bind(key => ModelTypes.Key(TypeOf(r.ElementType), ModelTypes.Beneath(parts, home), key)))
                : ModelTypes.Key(TypeOf(r.ElementType), parts, null))
            .Bind(before => r.Kind == RefactorOperationKind.Rename
                ? Parts(r.NewName!).Bind(n => Rename.Of(before, n[^1]))
                : Parts(r.NewSchema!).Bind(s => Name.Of(s[^1], before.Name.Base)).Bind(n => ElementKey.Of(before.Type, n)).Map(after => new Rename(before, after)));

    /// <summary>An operation's element type, written as DacFx serializes it; a name the map cannot place keys as written.</summary>
    private static string TypeOf(string? serialized) => serialized is null ? "" : ModelTypes.Element(serialized) ?? serialized;

    private static Result<string[]> Parts(string name) => TSql.NameParts(name) is { } parts ? parts
        : new Error("refactorlog.name", "The refactorlog names " + name + ", which is not a name of one to four parts.", "Restore the refactorlog from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
}
