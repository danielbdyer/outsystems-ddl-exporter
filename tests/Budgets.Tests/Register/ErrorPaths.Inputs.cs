using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;

namespace DbChange.Budgets.Tests.Register;

/// <summary>
/// The inputs the drivers of <see cref="All"/> build under their scratch folders: repositories, environments files, references,
/// publish profiles, packages and tool folders, and the arrangements of git, of file modes and of access a driver needs.
/// </summary>
internal static partial class ErrorPaths
{
    /// <summary>
    /// The error dbchange answers a command with, run in this process against <paramref name="here"/>: the one finding of severity error
    /// its --json answer carries.
    /// </summary>
    private static Error Answered(string[] arguments, Checkout here)
    {
        using var output = new MemoryStream();
        Cli.Program.Run([.. arguments, "--json"], output, here);
        var findings = JsonNode.Parse(output.ToArray())!["findings"]!.AsArray();
        return findings.Count == 1 && (string?)findings[0]!["severity"] == "error" && (string?)findings[0]!["remedy"] is { } remedy
            ? new Error((string)findings[0]!["code"]!, (string)findings[0]!["message"]!, remedy)
            : throw new InvalidOperationException("the answer carries no one error: " + Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>The copy a planted registry holds, made on localhost,11433.</summary>
    private const string Copied = "dbchange_host_1_0a1b2c3d";

    private static Target Target(string text) => Made(SqlServer.Target(text, "--target"));

    /// <summary>
    /// The repository root with .dbchange/copies.json holding <see cref="Copied"/>, as io/LocalServer writes a row, so copy: reaches R15
    /// without a server.
    /// </summary>
    private static string Registered(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".dbchange"));
        File.WriteAllText(Path.Combine(root, ".dbchange", "copies.json"),
            "{ \"copies\": [ { \"name\": \"" + Copied
                + "\", \"server\": \"localhost,11433\", \"machine\": \"host\", \"pid\": 1, \"created\": \"2026-09-24T00:00:00Z\" } ] }");
        return root;
    }

    /// <summary>The named environment dev, its connection a file under the scratch folder naming a server that is never reached.</summary>
    private static SqlServer.Database DevDatabase(string scratch) =>
        Made(SqlServer.Resolve(Target("env:dev"),
            Initialized(RepositoryAt(scratch,
            Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev")))))));

    /// <summary>
    /// A file: reference to a file written under the scratch folder, outside the repository root and in no git repository, and read by its
    /// owner alone; its path with '/' so the environments file's JSON carries it as it is.
    /// </summary>
    private static string Reference(string scratch, string file, string text) => "file:"
        + OwnerOnly(Written(scratch, file, text)).Replace('\\', '/');

    /// <summary>
    /// On Linux and macOS, the file's mode set to 0600, as io/SqlServer reads a connection file; Windows keeps no such mode.
    /// </summary>
    private static string OwnerOnly(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return file;
    }

    /// <summary>
    /// What <paramref name="use"/> returns while this identity may not list the folder, or read the file, at <paramref name="path"/>:
    /// on Windows a deny entry for RD (FILE_LIST_DIRECTORY on a folder, FILE_READ_DATA on a file) in its ACL, made and removed by
    /// icacls; on Linux and macOS mode 0300 for a folder and 0200 for a file, then 0700 or 0600 again. The denial is undone
    /// however use ends, so the scratch folder deletes.
    /// </summary>
    internal static T Denied<T>(string path, Func<T> use)
    {
        var (full, folder) = (Path.GetFullPath(path), Directory.Exists(path));
        var identity = Environment.UserDomainName + "\\" + Environment.UserName;
        var execute = folder ? UnixFileMode.UserExecute : UnixFileMode.None;
        if (OperatingSystem.IsWindows())
        {
            Icacls(full, "/deny", identity + ":(RD)");
        }
        else
        {
            File.SetUnixFileMode(full, UnixFileMode.UserWrite | execute);
        }

        try
        {
            return use();
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Icacls(full, "/remove:d", identity);
            }
            else
            {
                File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | execute);
            }
        }
    }

    /// <summary>
    /// What <paramref name="use"/> returns while this identity may not read the attributes of <paramref name="file"/>, so File.Exists
    /// answers false for it, as it does where no file is: on Windows a deny entry for RA (FILE_READ_ATTRIBUTES) on the file and one
    /// for RD (FILE_LIST_DIRECTORY) on its folder, since the right to list the folder also grants its files' attributes; on Linux and
    /// macOS mode 0600 on the folder, which withholds the search that stat needs, then 0700 again. The denial is undone however use
    /// ends, so the scratch folder deletes.
    /// </summary>
    internal static T Unexaminable<T>(string file, Func<T> use)
    {
        var full = Path.GetFullPath(file);
        var folder = Path.GetDirectoryName(full)!;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                return use();
            }
            finally
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var identity = Environment.UserDomainName + "\\" + Environment.UserName;
        Icacls(full, "/deny", identity + ":(RA)");
        try
        {
            return Denied(folder, use);
        }
        finally
        {
            Icacls(full, "/remove:d", identity);
        }
    }

    private static void Icacls(params string[] arguments)
    {
        if (new Command("icacls", arguments, TimeSpan.FromMinutes(1)).Run() is not Ran.Exited { Code: 0 } and var icacls)
        {
            throw new InvalidOperationException("icacls " + string.Join(' ', arguments) + " did not succeed: " + icacls);
        }
    }

    /// <summary>
    /// On Linux and macOS, the mode of the file a file: reference names set to 0640, so its group can read it; the reference as given.
    /// Windows keeps no such mode, and there the driver asks io/SqlServer of the mode directly.
    /// </summary>
    private static string GroupReadable(string reference)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(reference["file:".Length..], UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        return reference;
    }

    /// <summary>
    /// The repository root made a git repository, as a clone is, so io/SqlServer can ask git whether it would commit a file: reference's
    /// file.
    /// </summary>
    private static string Initialized(string root)
    {
        Arrange(root, "init", "-q");
        return root;
    }

    /// <summary>A machine whose dotnet lists the SDK global.json pins, and runs every other program as it is.</summary>
    private static Ran Sdk(Command command, System.Threading.CancellationToken cancel) => command.Arguments[0] == "--list-sdks"
        ? new Ran.Exited(0, (string)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]!["version"]!
            + " [sdk]\n", "")
        : Command.Run(command, cancel);

    /// <summary>
    /// A dev environment in the environments file's JSON: its host, its connection, its profile and whatever else is given.
    /// </summary>
    private static string Dev(string extra = "", string connection = "env:DBCHANGE_DEV", string profile = Pipeline, string name = "dev",
        string host = "dev-sql") =>
        Quoted(name) + ": { \"host\": " + Quoted(host) + ", \"connection\": " + Quoted(connection) + ", \"profile\": " + Quoted(profile)
            + (extra.Length > 0 ? ", " + extra : "") + " }";

    private static string Environments(string environments) => "{ \"environments\": { " + environments + " } }";

    private static Error ReadEnvironments(string scratch, string environments) =>
        Failed(Io.EnvironmentsFile.Read(RepositoryAt(scratch, Environments(environments))));

    /// <summary>A repository root under the scratch folder, holding dbchange/environments.json with the text given.</summary>
    private static string RepositoryAt(string scratch, string environmentsFile)
    {
        var root = Path.Combine(scratch, "dbchange-root");
        Directory.CreateDirectory(Path.Combine(root, "dbchange", "profiles"));
        File.WriteAllText(Path.Combine(root, "dbchange", "environments.json"), environmentsFile);
        return root;
    }

    /// <summary>A publish profile under the scratch folder: the given properties, and one SQLCMD variable per pair.</summary>
    private static string Profile(string scratch, string properties, params (string Name, string Value)[] sqlCmd) =>
        Written(scratch, "profile.publish.xml",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<Project ToolsVersion=\"Current\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n  <PropertyGroup>\n    "
            + properties + "\n  </PropertyGroup>\n  <ItemGroup>\n"
            + string.Concat(sqlCmd.Select(v =>
                "    <SqlCmdVariable Include=\"" + v.Name + "\">\n      <Value>" + v.Value + "</Value>\n    </SqlCmdVariable>\n"))
            + "  </ItemGroup>\n</Project>\n");

    /// <summary>
    /// A zip under the scratch folder holding the entries given and nothing DacFx needs: what Ssdt.Load opens as a package.
    /// </summary>
    private static string Zipped(string scratch, string file, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(scratch, file);
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            Added(zip, entries);
        }

        return path;
    }

    /// <summary>
    /// A package DacFx built from an empty model, under the scratch folder, with the entries given added to it, as a build adds its
    /// refactor.xml.
    /// </summary>
    private static string Packaged(string scratch, string file, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(scratch, file);
        using (var model = Model())
        {
            DacPackageExtensions.BuildPackage(path, model, new PackageMetadata());
        }

        using (var zip = new System.IO.Compression.ZipArchive(File.Open(path, FileMode.Open, FileAccess.ReadWrite),
            System.IO.Compression.ZipArchiveMode.Update))
        {
            Added(zip, entries);
        }

        return path;
    }

    private static void Added(System.IO.Compression.ZipArchive zip, (string Entry, string Text)[] entries)
    {
        foreach (var (entry, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }
    }

    private static string Written(string scratch, string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, file))!);
        File.WriteAllText(Path.Combine(scratch, file), text);
        return Path.Combine(scratch, file);
    }

    /// <summary>A repository root whose toolchain ledger is the sample's, its one row replaced.</summary>
    private static string Ledger(string scratch, string row)
    {
        var sample = File.ReadAllText(Path.Combine(Repository.Root, "tests", "Golden", "dbchange", "ledgers", "toolchain.md"));
        Written(scratch, Doctor.Ledger, sample.Replace("| 2026-09-24 | 3.0.0 | UNPINNED | — |", row, StringComparison.Ordinal));
        return scratch;
    }

    /// <summary>A copy of the classic-minimal project with the corpus's stop files, so dbchange's own build settings stay out.</summary>
    private static string Project(string scratch)
    {
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(golden, "classic-minimal"), "*", SearchOption.AllDirectories)
            .Concat([Path.Combine(golden, "Directory.Build.props"), Path.Combine(golden, "Directory.Packages.props")])
            .Where(f => !Path.GetRelativePath(golden, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var to = Path.Combine(scratch, "golden", Path.GetRelativePath(golden, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }

        return Path.Combine(scratch, "golden", "classic-minimal", "ClassicMinimal.sqlproj");
    }

    /// <summary>
    /// A package of one table, built by DacFx for the platform given under the scratch folder, under the collation given or the model's
    /// default.
    /// </summary>
    private static string Packaged(string scratch, string name, SqlServerVersion platform, string? collation = null)
    {
        var dacpac = Path.Combine(scratch, name + ".dacpac");
        using var model = new TSqlModel(platform, new TSqlModelOptions { Collation = collation });
        model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL);");
        DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        return dacpac;
    }

    /// <summary>The first refusal Classify raises between two models built in memory, as the error it carries.</summary>
    private static Error Refusal(string[] before, string[] after, Classify.Inputs? inputs = null)
    {
        using var was = Model(before);
        using var now = Model(after);
        var (from, to) = (Made(Ssdt.ReadModel(was)), Made(Ssdt.ReadModel(now)));
        var refusal = Classify.Of(from, to, Made(Change.Between(from, to, [])), [], inputs ?? Classify.Inputs.None).Findings
            .FirstOrDefault(f => f.Severity == Severity.Error) ?? throw new InvalidOperationException("Classify refused nothing");
        return new Error(refusal.Code, refusal.Message, refusal.Remedy!);
    }

    /// <summary>A model built in memory, each script added as its own source, as io/Ssdt.ReadModel reads one.</summary>
    private static TSqlModel Model(params string[] scripts)
    {
        var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        return model;
    }

    /// <summary>
    /// A drive of io/Git in a repository of its own at scratch/repository, holding one empty commit on main; after it, this process's
    /// holds on its worktrees are released and git's objects, which it writes read-only, are made writable, so the scratch folder deletes.
    /// </summary>
    private static Error InRepository<T>(string scratch, Func<string, Result<T>> drive)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "repository")).FullName;
        try
        {
            Arrange(root, "init", "-q", "--initial-branch=main");
            // dbchange's own commit (the push drivers) reads the repository's configuration alone, and a CI runner has no global identity.
            Arrange(root, "config", "user.name", "DbChange Test");
            Arrange(root, "config", "user.email", "dbchange-test@example.invalid");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "dbchange");
            return Failed(drive(root));
        }
        finally
        {
            Git.Release(root);
            foreach (var file in Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    /// <summary>
    /// git as a driver arranges the scratch repository: in it, never looking above the scratch folder, as an identity of its own, unsigned.
    /// </summary>
    private static void Arrange(string root, params string[] arguments) => Programs.TestGit(root, Path.GetDirectoryName(root)!, arguments);

    private static string Output(string scratch) => Path.Combine(scratch, "build");

    private static string Bare(string scratch) => Directory.CreateDirectory(Path.Combine(scratch, "bare")).FullName;

    /// <summary>
    /// A folder holding, empty, the files a published tool folder carries, and DacFx's own assembly in the build task's place, whose file
    /// version is the release dbchange runs, so the build starts and its targets fail to load.
    /// </summary>
    private static string Hollow(string scratch)
    {
        var tool = Path.Combine(scratch, "hollow");
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll",
            "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(tool, file))!);
            File.WriteAllText(Path.Combine(tool, file), "");
        }

        File.Copy(typeof(DacServices).Assembly.Location, Path.Combine(tool, Ssdt.BuildTargets.Task), overwrite: true);
        return tool;
    }

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>The literals of a repository whose model defines none, against which a declared witness is read.</summary>
    private static IReadOnlySet<string> Literals => new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The error a verb's answer carries as its one finding of severity error, where the cli fails inside a verb rather than in a Result.
    /// </summary>
    private static Error Carried(Envelope answer) => answer.Findings is [{ Severity: Severity.Error, Remedy: { } remedy } finding]
        ? new Error(finding.Code, finding.Message, remedy)
        : throw new InvalidOperationException("the answer carries no one error: " + answer.Message);

    private static T Made<T>(Result<T> result) => Expect.Value(result);

    private static Error Failed<T>(Result<T> result) => Expect.Failed(result);
}
