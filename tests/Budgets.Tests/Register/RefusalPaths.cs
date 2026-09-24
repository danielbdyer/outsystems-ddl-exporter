using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;
using Microsoft.SqlServer.Dac.Model;

namespace Estate.Budgets.Tests.Register;

/// <summary>
/// Every way to a refusal the kernel and io construct, each with an input that takes it there. Register.Refusals reads each
/// refusal for the register; Io.Tests' "no output contains Password=" plants a password in every input that can carry a value
/// (<see cref="Case.Plants"/>) and searches what comes back. A driver writes only under the scratch folder it is given, one per
/// case, and leaves it deletable. The kernel's schema refusals, io/Ssdt's and io/Git's quote what they refuse, a name, a
/// version, a path, a ref or a branch, and plant nothing. io/Git's are reached in a repository made under the scratch folder.
/// io/SqlServer's and io/Substrate's reach no server: each is refused before anything connects, and a SQL Server error reaches
/// its refusal through Database.Refused, the one door every failure against a server passes through.
/// </summary>
internal static class RefusalPaths
{
    /// <summary>One way to a refusal: what it is, the code it must take, whether its input carries the planted value, and its driver (scratch, planted).</summary>
    public sealed record Case(string Label, string Code, bool Plants, Func<string, string, Refusal> Drive);

    private const string Pipeline = "estate/profiles/pipeline.publish.xml";

    private static readonly ElementKey Table = Made(ElementKey.Of("Table", Made(Name.Of("dbo", "Customer"))));

    /// <summary>The paths a CommitAndPush of the evidence names, none of which the scratch repository's one empty commit holds.</summary>
    private static readonly string[] Evidence = ["estate/evidence.shape.json"];

    public static IReadOnlyList<Case> All { get; } =
    [
        new("a blank name part", "name.blank", false, (_, _) => Refused(Name.Of(" "))),
        new("an overlong name part", "name.too-long", true, (_, planted) => Refused(Name.Of(planted + new string('x', 129)))),
        new("a control character in a name part", "name.control-character", true, (_, planted) => Refused(Name.Of(planted + "\u0001"))),
        new("a DacFx version that is none", "engine.dacfx-version", false, (_, _) => Refused(Engine.Of("v170"))),
        new("an image digest that is none", "engine.image-digest", false, (_, _) => Refused(Engine.Of("170.5.96", "sha256:0"))),
        new("a fingerprint that is none", "fingerprint.malformed", false, (_, _) => Refused(Fingerprint.Parse("0"))),
        new("an element with a blank type", "element.type-blank", true, (_, planted) => Refused(ElementKey.Of(" ", Made(Name.Of(planted))))),
        new("an element with no name", "element.name-missing", false, (_, _) => Refused(ElementKey.Of("Table", default))),
        new("a child element named in two parts", "element.child-name", false, (_, _) => Refused(ElementKey.Of(Table, "Column", Made(Name.Of("dbo", "Email"))))),
        new("an element with a property given twice", "element.property-name", false, (_, _) =>
            Refused(Element.Of(Table, [new("Nullable", new Value.Boolean(true)), new("Nullable", new Value.Boolean(false))], []))),
        new("an element with a relationship given twice", "element.relationship-name", false, (_, _) =>
            Refused(Element.Of(Table, [], [Element.Relationship.Of("Columns", [Table]), Element.Relationship.Of("Columns", [Table])]))),
        new("a read with two elements on one key", "change.duplicate-key", false, (_, _) =>
            Refused(Change.Between(Seq.Of(Made(Element.Of(Table, [], [])), Made(Element.Of(Table, [new("Nullable", new Value.Null())], []))), [], []))),

        new("ESTATE_TOOL naming no tool folder", "tool.missing", false, (scratch, _) => Refused(Ssdt.Tool(Bare(scratch), Bare(scratch), scratch))),
        new("no tool folder anywhere", "tool.missing", false, (scratch, _) => Refused(Ssdt.Tool(Bare(scratch), null, scratch))),
        new("a build against no tool folder", "tool.missing", false, (scratch, _) => Refused(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), Sdk))),
        new("a build of no project", "build.no-project", false, (scratch, _) => Refused(Ssdt.Build(Path.Combine(scratch, "none.sqlproj"), Bare(scratch), Output(scratch), Sdk))),
        new("a build without the SDK band", "sdk.missing", false, (scratch, _) => Refused(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), (_, _) => (0, "8.0.100 [sdk]\n")))),
        new("a build that fails", "build.failed", false, (scratch, _) => Refused(Ssdt.Build(Project(scratch), Hollow(scratch), Output(scratch), Sdk))),
        new("a package that is none", "package.unreadable", false, (scratch, _) => Refused(Ssdt.Load(Written(scratch, "not.dacpac", "not a package")))),
        new("a refactorlog that is none", "refactorlog.unreadable", false, (scratch, _) => Refused(Ssdt.RefactorLog(Written(scratch, "not.refactorlog", "not a refactorlog")))),
        new("a refactorlog entry naming no object", "refactorlog.name", false, (_, _) =>
        {
            using var package = new Ssdt.Package(Model(), null, null,
                [new Ssdt.RefactorEntry("0a1b2c3d-0000-4000-8000-000000000001", "Rename Refactor", null, "[dbo].[Customer", "SqlTable", null, null, "[Client]", null)],
                new Dictionary<string, string>(StringComparer.Ordinal));
            return Refused(Ssdt.Walk(package));
        }),
        new("two objects of a model keyed alike", "walk.duplicate-key", false, (_, _) =>
        {
            using var model = Model("CREATE TABLE dbo.Customer (Id INT NOT NULL);", "CREATE TABLE dbo.Customer (Id INT NOT NULL);");
            return Refused(Ssdt.Walk(model));
        }),

        new("a git program that does not start", "git.missing", false, (scratch, _) => Refused(Git.At(scratch, "HEAD", git: Path.Combine(scratch, "no-git")))),
        new("a folder in no repository", "git.not-a-repository", false, (scratch, _) => Refused(Git.ChangedPaths(Path.Combine(scratch, "no-repository"), "HEAD~1", "HEAD"))),
        new("a ref that names no commit", "ref.unresolved", false, (scratch, _) => InRepository(scratch, root => Git.At(root, "no-such-tag"))),
        new("two refs whose histories never meet", "ref.unrelated", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "switch", "-q", "--orphan", "unrelated");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "unrelated");
            return Git.MergeBase(root, "main", "unrelated");
        })),
        new("a branch name git does not take", "branch.malformed", false, (scratch, _) => InRepository(scratch, root => Git.CommitAndPush(root, Evidence, "evidence", "estate/..evidence"))),
        new("a branch that exists here", "branch.taken", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "branch", "estate/evidence");
            return Git.CommitAndPush(root, Evidence, "evidence", "estate/evidence");
        })),
        new("an origin that does not answer", "origin.unreachable", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "no-origin.git"));
            return Git.CommitAndPush(root, Evidence, "evidence", "estate/evidence");
        })),
        new("a commit of a path the working tree lacks", "git.failed", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "init", "-q", "--bare", Path.Combine(scratch, "origin.git"));
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "origin.git"));
            return Git.CommitAndPush(root, Evidence, "evidence", "estate/evidence");
        })),

        new("no posture", "posture.missing", false, (scratch, _) => Refused(Profiles.Environments(scratch))),
        new("a posture that is not JSON", "posture.unreadable", true, (scratch, planted) => Refused(Profiles.Environments(Estate(scratch, "{ \"environments\": { \"dev\": " + planted + " } }")))),
        new("a posture giving a key twice", "posture.unreadable", true, (scratch, planted) => Posture(scratch, Dev("\"cohorts\": [" + Quoted(planted) + "], \"cohorts\": []"))),
        new("a literal connection string", "posture.literal-connection", true, (scratch, planted) => Posture(scratch, Dev(connection: "Server=db;User ID=estate;Password=" + planted))),
        new("a literal connection string as a key", "posture.literal-connection", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Data Source=db;Password=" + planted + "\": \"env:A\" }"))),
        new("an unknown key", "posture.unknown-key", true, (scratch, planted) => Posture(scratch, Dev("\"password\": " + Quoted(planted)))),
        new("an unknown key beside a SQLCMD literal", "posture.unknown-key", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": { \"literal\": \"dev\", \"sensitive\": false, \"secret\": " + Quoted(planted) + " } }"))),
        new("a value of the wrong JSON kind", "posture.malformed", true, (scratch, planted) => Posture(scratch, Dev("\"cohorts\": " + Quoted(planted)))),
        new("an environment with no connection", "posture.malformed", true, (scratch, planted) =>
            Posture(scratch, "\"dev\": { \"profile\": \"" + Pipeline + "\", \"cohorts\": [" + Quoted(planted) + "] }")),
        new("a SQLCMD literal not marked non-sensitive", "posture.unmarked-literal", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": { \"literal\": " + Quoted(planted) + " } }"))),
        new("a SQLCMD value that is a bare literal", "posture.unmarked-literal", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": " + Quoted(planted) + " }"))),
        new("a connection that is no reference", "reference.malformed", true, (scratch, planted) => Posture(scratch, Dev(connection: "env:" + planted))),
        new("a file reference that is a connection string", "reference.malformed", true, (_, planted) =>
            Refused(SecretReference.Of("--connection", "file:Server=db;User ID=sa;Password=" + planted))),
        new("a substrate that is neither docker nor localdb", "posture.malformed", true, (scratch, planted) =>
            Refused(Profiles.Environments(Estate(scratch, "{ \"environments\": {}, \"substrate\": " + Quoted(planted) + " }")))),
        new("an environment misnamed", "posture.environment-name", true, (scratch, planted) => Posture(scratch, Dev("\"cohorts\": [" + Quoted(planted) + "]", name: "DEV"))),
        new("a cohort given twice", "posture.cohort", true, (scratch, planted) => Posture(scratch, Dev("\"cohorts\": [" + Quoted(planted) + ", " + Quoted(planted) + "]"))),
        new("a profile path outside the estate", "posture.profile-path", true, (scratch, planted) => Posture(scratch, Dev(profile: "../" + planted + ".publish.xml"))),
        new("a SQLCMD variable given twice in two cases", "posture.sqlcmd-repeated", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": \"env:A\", \"tag\": \"env:B\" }, \"cohorts\": [" + Quoted(planted) + "]"))),
        new("a classification that is none", "posture.classification", true, (scratch, planted) => Posture(scratch, Dev("\"classification\": " + Quoted(planted)))),
        new("a synthetic environment unconfirmed", "posture.unconfirmed", true, (scratch, planted) =>
            Posture(scratch, Dev("\"classification\": \"synthetic\", \"cohorts\": [" + Quoted(planted) + "]"))),
        new("a confirmation with no date", "posture.confirmation", true, (scratch, planted) => Posture(scratch, Dev("\"classification\": \"synthetic\", \"confirmedBy\": " + Quoted(planted)))),
        new("a SQLCMD variable misnamed", "sqlcmd.name", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag Name\": \"env:A\" }, \"cohorts\": [" + Quoted(planted) + "]"))),
        new("a SQLCMD literal under a credential's name in the posture", "sqlcmd.literal-credential", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"ServicePassword\": { \"literal\": " + Quoted(planted) + ", \"sensitive\": false } }"))),
        new("a script using a variable with no value", "sqlcmd.undefined", true, (_, planted) =>
            Refused(SqlCmdVariable.Substitute("PRINT '$(Missing)';", new Dictionary<string, string>(StringComparer.Ordinal) { ["Tag"] = planted }))),

        new("no profile", "profile.missing", false, (scratch, _) => Refused(Profiles.Load(Path.Combine(scratch, "none.publish.xml")))),
        new("a profile that is not XML", "profile.unreadable", true, (scratch, planted) => Refused(Profiles.Load(Written(scratch, "broken.publish.xml", "<Project>" + planted + "</Projec>")))),
        new("a profile DacFx does not read", "profile.unreadable", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>" + planted + "</BlockOnPossibleDataLoss>")))),
        new("a profile holding a password", "profile.password", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "<TargetConnectionString>Data Source=db;User ID=sa;Password=" + planted + "</TargetConnectionString>")))),
        new("a profile holding a password a comment splits", "profile.password", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "", ("LinkedServer", "Server=db;User ID=sa;Pass<!-- -->word=" + planted))))),
        new("a profile giving a SQLCMD value that is a connection string", "profile.literal-connection", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "", ("LinkedServer", "Data Source=" + planted + ";Initial Catalog=Orders;Integrated Security=True"))))),
        new("a profile with the guard off", "profile.guard-off", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted))))),
        new("a named environment whose profile has the guard off", "profile.guard-off", true, (scratch, planted) =>
        {
            var root = Estate(scratch, Environments(Dev(profile: "estate/profiles/relaxed.publish.xml")));
            File.Move(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted)), Path.Combine(root, "estate", "profiles", "relaxed.publish.xml"));
            return Refused(Profiles.Of(Made(Profiles.Environments(root)).Single(), root));
        }),
        new("a SQLCMD literal under a credential's name in a profile", "sqlcmd.literal-credential", true, (scratch, planted) =>
            Refused(Profiles.Load(Profile(scratch, "", ("ApiToken", planted))))),

        new("a target of no form the grammar knows", "target.unknown", true, (_, planted) => Refused(SqlServer.Target.Parse("sql:" + planted))),
        new("a literal connection string where a target goes", "connection.literal", true, (_, planted) =>
            Refused(SqlServer.Target.Parse("Server=db;User ID=sa;Password=" + planted, "--target"))),
        new("a git ref where a database is asked for", "target.not-a-database", false, (scratch, _) => Refused(SqlServer.Resolve(Target("ref:main"), scratch))),
        new("an environment the posture does not name", "target.unnamed", false, (scratch, _) => Refused(SqlServer.Resolve(Target("env:qa"), Estate(scratch, Environments(Dev()))))),
        new("the Twin before its milestone", "twin.not-built", false, (scratch, _) => Refused(SqlServer.Resolve(Target("twin"), scratch))),
        new("a connection whose variable is unset", "connection.unresolved", false, (scratch, _) =>
            Refused(SqlServer.Resolve(Target("env:dev"), Estate(scratch, Environments(Dev(connection: "env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant())))))),
        new("a connection file holding no connection string", "connection.malformed", true, (scratch, planted) =>
            Refused(SqlServer.Resolve(Target("env:dev"), Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "garbled " + planted))))))),
        new("a copy the registry does not hold", "copy.unregistered", false, (scratch, _) => Refused(SqlServer.Resolve(Target("copy:estate_nowhere_1_00000000"), scratch))),
        new("a copy registry that is not JSON", "registry.unreadable", true, (scratch, planted) =>
        {
            Directory.CreateDirectory(Path.Combine(scratch, ".estate"));
            File.WriteAllText(Path.Combine(scratch, ".estate", "copies.json"), "{ \"copies\": [ " + planted);
            return Refused(SqlServer.Resolve(Target("copy:estate_nowhere_1_00000000"), scratch));
        }),
        new("a substrate on the host an environment's reference names", "copy.named-host", true, (scratch, planted) =>
            Refused(Substrate.Create(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=127.0.0.1,1433;Initial Catalog=Dev;User ID=reader;Password=" + planted)))),
                "Server=localhost,11433;Initial Catalog=master;User ID=sa;Password=" + planted))),
        new("no substrate server anywhere", "substrate.missing", false, (scratch, _) => Refused(Substrate.Server(null, Path.Combine(scratch, "no-sql.env"), localDb: false))),
        new("a named environment's login denied", "server.denied", true, (scratch, planted) => DevDatabase(scratch).Refused(18456, "Login failed for user '" + planted + "'.")),
        new("a named environment that does not answer", "server.unreachable", true, (scratch, planted) =>
            DevDatabase(scratch).Refused(53, "A network-related or instance-specific error occurred while establishing a connection to " + planted + ".")),
        new("a named environment's statement failing", "server.failed", true, (scratch, planted) =>
            DevDatabase(scratch).Refused(245, "Conversion failed when converting the nvarchar value '" + planted + "' to data type int.")),
        new("a probe the allowlist refuses", "probe.refused", true, (_, planted) =>
            Refused(SqlServer.Probe.Of("SELECT MAX(Email) FROM dbo.Customer WHERE Name = N'" + planted + "';", "dbo.Customer.Email Fits"))),
        new("a SQLCMD reference that does not resolve", "sqlcmd.unresolved", false, (scratch, _) =>
        {
            var root = Estate(scratch, Environments(Dev("\"sqlcmd\": { \"ServiceToken\": \"env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "\" }",
                connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev"))));
            File.Copy(Path.Combine(Repository.Root, "tests", "Golden", "proving-ground", "profiles", "pipeline.publish.xml"), Path.Combine(root, "estate", "profiles", "pipeline.publish.xml"));
            var dev = Made(SqlServer.Resolve(Target("env:dev"), root));
            return Refused(SqlServer.Plan(Path.Combine(scratch, "none.dacpac"), dev, Made(Profiles.Of(((SqlServer.Named)dev).Environment, root))));
        }),
    ];

    private static SqlServer.Target Target(string text) => Made(SqlServer.Target.Parse(text));

    /// <summary>The named environment dev, its connection a file under the scratch folder naming a server that is never reached.</summary>
    private static SqlServer.Database DevDatabase(string scratch) =>
        Made(SqlServer.Resolve(Target("env:dev"), Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev"))))));

    /// <summary>A file: reference to a file written under the scratch folder, its path with '/' so the posture's JSON carries it as it is.</summary>
    private static string Reference(string scratch, string file, string text) => "file:" + Written(scratch, file, text).Replace('\\', '/');

    /// <summary>Each refusal code the kernel and io construct, as their sources write it: a literal code, or the literal start of a composed one (element.).</summary>
    public static IEnumerable<string> InTheSources() => Repository.Files
        .Where(f => (f.StartsWith("kernel/", StringComparison.Ordinal) || f.StartsWith("io/", StringComparison.Ordinal)) && f.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(Repository.Read(f), @"new\s+Refusal\(\s*""([a-z0-9.-]+)""").Select(m => m.Groups[1].Value))
        .Distinct()
        .Order(StringComparer.Ordinal);

    private static (int Exit, string Output)? Sdk(string file, IReadOnlyList<string> arguments) =>
        (0, (string)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]!["version"]! + " [sdk]\n");

    /// <summary>A dev environment in posture JSON: its connection, its profile and whatever else is given.</summary>
    private static string Dev(string extra = "", string connection = "env:ESTATE_DEV", string profile = Pipeline, string name = "dev") =>
        Quoted(name) + ": { \"connection\": " + Quoted(connection) + ", \"profile\": " + Quoted(profile) + (extra.Length > 0 ? ", " + extra : "") + " }";

    private static string Environments(string environments) => "{ \"environments\": { " + environments + " } }";

    private static Refusal Posture(string scratch, string environments) => Refused(Profiles.Environments(Estate(scratch, Environments(environments))));

    /// <summary>An estate's root under the scratch folder, holding estate/posture.json with the text given.</summary>
    private static string Estate(string scratch, string posture)
    {
        var root = Path.Combine(scratch, "estate-root");
        Directory.CreateDirectory(Path.Combine(root, "estate", "profiles"));
        File.WriteAllText(Path.Combine(root, "estate", "posture.json"), posture);
        return root;
    }

    /// <summary>A publish profile under the scratch folder: the given properties, and one SQLCMD variable per pair.</summary>
    private static string Profile(string scratch, string properties, params (string Name, string Value)[] sqlCmd) => Written(scratch, "profile.publish.xml",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Project ToolsVersion=\"Current\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n  <PropertyGroup>\n    "
        + properties + "\n  </PropertyGroup>\n  <ItemGroup>\n"
        + string.Concat(sqlCmd.Select(v => "    <SqlCmdVariable Include=\"" + v.Name + "\">\n      <Value>" + v.Value + "</Value>\n    </SqlCmdVariable>\n"))
        + "  </ItemGroup>\n</Project>\n");

    private static string Written(string scratch, string file, string text)
    {
        File.WriteAllText(Path.Combine(scratch, file), text);
        return Path.Combine(scratch, file);
    }

    /// <summary>A copy of the classic-minimal project with the corpus's stop files, so the engine's build settings stay out.</summary>
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

    /// <summary>A model built in memory, each script added as its own source, as io/Ssdt.Walk reads one.</summary>
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
    /// A drive of io/Git in a repository of its own at scratch/repository, holding one empty commit on main; after it, git's
    /// objects, which it writes read-only, are made writable, so the scratch folder deletes.
    /// </summary>
    private static Refusal InRepository<T>(string scratch, Func<string, Result<T>> drive)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "repository")).FullName;
        try
        {
            Arrange(root, "init", "-q", "--initial-branch=main");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "estate");
            return Refused(drive(root));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    /// <summary>git as a driver arranges the scratch repository: in it, never looking above the scratch folder, as an identity of its own, unsigned.</summary>
    private static void Arrange(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git", ["-c", "user.name=Estate Test", "-c", "user.email=estate-test@example.invalid", "-c", "commit.gpgsign=false", .. arguments])
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(root);
        foreach (var variable in (string[])["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR"])
        {
            start.Environment.Remove(variable);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " exited " + process.ExitCode + ": " + errors.Result);
        }
    }

    private static string Output(string scratch) => Path.Combine(scratch, "build");

    private static string Bare(string scratch) => Directory.CreateDirectory(Path.Combine(scratch, "bare")).FullName;

    /// <summary>A folder holding, empty, the files a published tool folder carries, so the build starts and its targets fail to load.</summary>
    private static string Hollow(string scratch)
    {
        var tool = Path.Combine(scratch, "hollow");
        foreach (var file in (string[])["Microsoft.Data.Tools.Schema.SqlTasks.targets", "refasm/.NETFramework/v4.7.2/mscorlib.dll", "refasm/.NETFramework/v4.7.2/RedistList/FrameworkList.xml"])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(tool, file))!);
            File.WriteAllText(Path.Combine(tool, file), "");
        }

        return tool;
    }

    private static string Quoted(string text) => "\"" + text + "\"";

    private static T Made<T>(Result<T> result) => result.Match(value => value, refusal => throw new InvalidOperationException(refusal.Code + ": " + refusal.Message));

    private static Refusal Refused<T>(Result<T> result) => result.Match(value => throw new InvalidOperationException("accepted where a refusal was due: " + value), refusal => refusal);
}
