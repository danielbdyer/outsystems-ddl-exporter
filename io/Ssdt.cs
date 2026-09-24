using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace Estate.Io;

/// <summary>
/// The SSDT project and its package, read whole (V3_MILESTONES.md §2.2): Build runs the project's own build against the
/// published tool folder's DacFx targets (§1 fact 1), Load reads what the build wrote, and RefactorLog reads a refactorlog.
/// WP 1.2 adds the walk. A refusal's code names what was refused; cli/Contract.cs maps its area to the exit.
/// </summary>
/// <remarks>
/// No Visual Studio fallback: S1's windows-latest half answered that the committed route builds a classic project there.
/// Visual Studio's MSBuild with its own SSDT targets, found through vswhere and stamped as that engine, lands only if S1's
/// laptop half finds the estate's project cannot build this way (WP 1.1).
/// </remarks>
public static class Ssdt
{
    /// <summary>What a build wrote: the package, and the fingerprint of the inputs whose folder holds it.</summary>
    public sealed record Dacpac(string Path, Fingerprint Inputs);

    /// <summary>
    /// A package as DacFx reads it: the model; the pre- and post-deploy scripts as the build inlined them, null when the
    /// project has none; and its refactorlog's entries in file order. Disposing it releases the model.
    /// </summary>
    public sealed record Package(TSqlModel Model, string? PreDeploy, string? PostDeploy, IReadOnlyList<RefactorEntry> Refactors) : IDisposable
    {
        public void Dispose() => Model.Dispose();
    }

    /// <summary>
    /// One refactorlog operation as SSDT wrote it: its key; its name (Rename Refactor, Move Schema); its ChangeDateTime as
    /// written; the element it acts on and that element's type; the parent and its type, where the element has one; and the
    /// new name of a rename or the new schema of a move.
    /// </summary>
    public sealed record RefactorEntry(
        string Key, string Operation, string? ChangeDateTime, string ElementName, string? ElementType, string? ParentName, string? ParentType, string? NewName, string? NewSchema);

    private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>The line in which the SqlTasks targets name the package they wrote; minimal verbosity prints it.</summary>
    private static readonly Regex Wrote = new(@" -> (?<path>.+\.dacpac)\r?$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>An MSBuild error: its origin (a file and its position, or a tool), an optional subcategory, the code and the text, then the project in brackets.</summary>
    private static readonly Regex Error = new(
        @"^\s*(?<origin>.+?)(?<position>\(\d+(?:,\d+)*\))?\s*:\s*(?:[\w ]+ )?error (?<code>[A-Za-z]+\d+)\s*:\s*(?<text>.*?)(?:\s+\[[^\]]*\])?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>The engine's files a build's output depends on besides the project: the targets and the build task.</summary>
    private static readonly string[] Engine = ["Microsoft.Data.Tools.Schema.SqlTasks.targets", "Microsoft.Data.Tools.Schema.Tasks.Sql.dll"];

    public static Result<string> Tool() => Tool(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ESTATE_TOOL"), Directory.GetCurrentDirectory());

    /// <summary>
    /// The tool folder: the one estate runs from, when it carries the targets; else the one ESTATE_TOOL names; else dist/estate/
    /// in the nearest directory at or above the working directory, as in a clone of the engine that ci/publish has run in.
    /// </summary>
    public static Result<string> Tool(string running, string? variable, string workingDirectory)
    {
        if (Doctor.Tool(running).Remedy is null)
        {
            return running;
        }

        if (!string.IsNullOrEmpty(variable))
        {
            return Doctor.Tool(variable) is { Remedy: { } publish } named
                ? new Refusal("tool.missing", "ESTATE_TOOL names " + variable + ", which is " + named.Found + ".", publish + ", and set ESTATE_TOOL to it or unset it; then estate doctor")
                : variable;
        }

        for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
        {
            if (Doctor.Tool(Path.Combine(directory.FullName, "dist", "estate")) is { Remedy: null })
            {
                return Path.Combine(directory.FullName, "dist", "estate");
            }
        }

        return new Refusal(
            "tool.missing",
            "estate does not run from a published tool folder, ESTATE_TOOL is unset, and no dist/estate/ lies at or above " + workingDirectory + ".",
            "run ci/publish.sh, or ci/publish.ps1 on Windows, in a clone of the engine, or set ESTATE_TOOL to a published tool folder; then estate doctor");
    }

    public static Result<Dacpac> Build(string project, string toolFolder, string outputRoot) => Build(project, toolFolder, outputRoot, Doctor.Run);

    /// <summary>
    /// Builds a classic .sqlproj as §1 fact 1 does, with the SDK the probe lists: dotnet build against the tool folder's targets
    /// and reference stub, telemetry off, its output and intermediate files under outputRoot/&lt;the inputs' fingerprint&gt;/ so
    /// nothing is written beside the project. A missing SDK band or tool folder is refused before anything builds.
    /// </summary>
    public static Result<Dacpac> Build(string project, string toolFolder, string outputRoot, Doctor.Command probe)
    {
        var (file, tool) = (Path.GetFullPath(project), Path.TrimEndingDirectorySeparator(Path.GetFullPath(toolFolder)));
        var directory = Path.GetDirectoryName(file)!;
        return (File.Exists(file), Doctor.Sdk(directory, probe), Doctor.Tool(tool)) switch
        {
            (false, _, _) => new Refusal("build.no-project", "No project at " + file + ".", "Name the .sqlproj to build, by its path from the working directory."),
            (_, { Remedy: { } install } sdk, _) => new Refusal(
                "sdk.missing", "dotnet build loads DacFx's net10.0 build task, and this machine has " + sdk.Found + ".", install + "; then estate doctor"),
            (_, _, { Remedy: { } publish } found) => new Refusal("tool.missing", tool + " is " + found.Found + ".", publish + "; then estate doctor"),
            _ => Run(file, tool, Path.GetFullPath(outputRoot)),
        };
    }

    private static Result<Dacpac> Run(string project, string tool, string outputRoot)
    {
        var directory = Path.GetDirectoryName(project)!;
        var inputs = Inputs(directory, tool);

        // outputRoot/<the inputs' fingerprint, its first 16 digits so MSBuild's paths stay short on Windows>/. Once WP 1.6's
        // io/Git.At exists, a build of a ref names its folder by the ref's commit sha instead.
        var output = Path.Combine(outputRoot, inputs.ToString()[..16]) + "/";
        var (exit, log) = Dotnet(directory,
        [
            "build", project, "-c", "Release", "--no-restore", "-nologo", "-tl:off", "-v:m", "-nodeReuse:false",
            "-p:DacFxTelemetryEnabled=false", "-p:NetCoreBuild=true", "-p:NETCoreTargetsPath=" + tool, "-p:SQLDBExtensionsRefPath=" + tool,
            "-p:TargetFrameworkRootPath=" + Path.Combine(tool, "refasm"),
            "-p:OutputPath=" + output, "-p:BaseIntermediateOutputPath=" + output + "obj/", "-p:IntermediateOutputPath=" + output + "obj/",
        ]);
        var errors = log.Split('\n').Select(line => Error.Match(line.TrimEnd('\r'))).Where(m => m.Success).Select(m => Located(m, directory)).Distinct().ToList();
        var dacpac = Wrote.Matches(log).Select(m => m.Groups["path"].Value).LastOrDefault();
        return exit == 0 && errors.Count == 0 && dacpac is not null && File.Exists(dacpac)
            ? new Dacpac(dacpac, inputs)
            : new Refusal(
                "build.failed",
                "dotnet build of " + Path.GetFileName(project) + " failed:\n" + string.Join('\n', errors.Count > 0 ? errors : log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(20)),
                "Fix each error at the file and line it names, then build again.");
    }

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
    /// under bin/, obj/ and hidden folders such as .git/ and .estate/; then the engine's files, so a new engine never reuses
    /// an old engine's folder.
    /// </summary>
    private static Fingerprint Inputs(string directory, string tool) => Fingerprint.Of(string.Join('\n',
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').SkipLast(1).Any(folder => folder is "bin" or "obj" || folder.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .Select(file => file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(directory, file))))
            .Concat(Engine.Where(file => File.Exists(Path.Combine(tool, file))).Select(file => "tool/" + file + " " + Fingerprint.Of(File.ReadAllBytes(Path.Combine(tool, file)))))));

    /// <summary>dotnet with the arguments, from the directory, telemetry off: its exit code, and its output and errors together.</summary>
    private static (int Exit, string Log) Dotnet(string directory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DACFX_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        try
        {
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + errors.Result);
        }
        catch (Win32Exception e)
        {
            return (-1, "dotnet did not start: " + e.Message);
        }
    }

    /// <summary>A package's model, its deploy scripts and its refactorlog; a file DacFx cannot read as a package is refused.</summary>
    public static Result<Package> Load(string dacpac)
    {
        try
        {
            using var package = DacPackage.Load(dacpac, DacSchemaModelStorageType.Memory, FileAccess.Read);
            using var zip = ZipFile.OpenRead(dacpac);
            using var log = zip.GetEntry("refactor.xml")?.Open();
            var (pre, post, refactors) = (Text(package.PreDeploymentScript), Text(package.PostDeploymentScript), log is null ? [] : Entries(log));
            return new Package(TSqlModel.LoadFromDacpac(dacpac, new ModelLoadOptions(DacSchemaModelStorageType.Memory, loadAsScriptBackedModel: false)), pre, post, refactors);
        }
        catch (Exception e) when (e is DacServicesException or DacModelException or IOException or InvalidDataException or XmlException or UnauthorizedAccessException)
        {
            return new Refusal("package.unreadable", dacpac + " is not a package DacFx reads: " + e.Message, "Name a .dacpac a build wrote, or build its project again.");
        }
    }

    /// <summary>A .refactorlog file's entries, read as Load reads the copy a build puts in the package.</summary>
    public static Result<IReadOnlyList<RefactorEntry>> RefactorLog(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Result.Ok<IReadOnlyList<RefactorEntry>>(Entries(file));
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            return new Refusal("refactorlog.unreadable", path + " is not a refactorlog SSDT reads: " + e.Message, "Restore the file from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
        }
    }

    /// <summary>
    /// The operations of a refactorlog, in file order. A build's copy leaves the root element outside the namespace its
    /// operations carry, so the operations are found by name wherever they sit; one with no key, name or element is refused.
    /// </summary>
    private static List<RefactorEntry> Entries(Stream log)
    {
        using var reader = XmlReader.Create(log, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader).Descendants(Dac + "Operation").Select(operation =>
        {
            string? Property(string name) => (string?)operation.Elements(Dac + "Property").FirstOrDefault(p => (string?)p.Attribute("Name") == name)?.Attribute("Value");
            string Required(string? value, string what) => value ?? throw new XmlException("A refactorlog operation has no " + what + ".");
            return new RefactorEntry(
                Required((string?)operation.Attribute("Key"), "Key"), Required((string?)operation.Attribute("Name"), "Name"), (string?)operation.Attribute("ChangeDateTime"),
                Required(Property("ElementName"), "ElementName"), Property("ElementType"), Property("ParentElementName"), Property("ParentElementType"), Property("NewName"), Property("NewSchema"));
        }).ToList();
    }

    private static string? Text(Stream? script)
    {
        using var reader = script is null ? null : new StreamReader(script);
        return reader?.ReadToEnd();
    }
}
