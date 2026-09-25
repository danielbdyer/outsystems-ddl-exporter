using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Estate.Cli;
using Estate.Io;
using Estate.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Contract = Estate.Cli.Contract;

namespace Estate.Budgets.Tests.Register;

/// <summary>
/// Every way to an error the kernel, io and the cli construct, each with an input that takes it there. Register.Refusals reads each
/// error for the register; Io.Tests' "no output contains Password=" plants a password in every input that can carry a value
/// (<see cref="Case.Plants"/>) and searches what comes back. A driver writes only under the scratch folder it is given, one per
/// case, and leaves it deletable. The kernel's schema errors, io/Ssdt's and io/Git's quote what they reject, a name, a
/// version, a path, a ref or a branch, and plant nothing. io/Git's are reached in a repository made under the scratch folder.
/// io/SqlServer's and io/ScratchServer's reach no server: each is an error before anything connects, and a SQL Server or DacFx error reaches
/// its code through Database.ErrorOf, the one door every failure against a server passes through. The scratch server's own choice
/// and Create on a given server are io's alone, so R15 is reached through copy: and a planted registry row. The cli's reject
/// arguments, through Contract.Flags and estate check's own answer.
/// </summary>
internal static class RefusalPaths
{
    /// <summary>One way to an error: what it is, the code it must take, whether its input carries the planted value, and its driver (scratch, planted).</summary>
    public sealed record Case(string Label, string Code, bool Plants, Func<string, string, Error> Drive);

    private const string Pipeline = "estate/profiles/pipeline.publish.xml";

    private static readonly ElementKey Table = Made(ElementKey.Of("Table", Made(Name.Of("dbo", "Customer"))));

    /// <summary>The paths a CommitAndPush of the evidence names, none of which the scratch repository's one empty commit holds.</summary>
    private static readonly string[] Evidence = ["estate/evidence.shape.json"];

    public static IReadOnlyList<Case> All { get; } =
    [
        new("an empty name part", "name.blank", false, (_, _) => Failed(Name.Of(""))),
        new("an overlong name part", "name.too-long", true, (_, planted) => Failed(Name.Of(planted + new string('x', 129)))),
        new("a DacFx version that is none", "engine.dacfx-version", false, (_, _) => Failed(Engine.Of("v170"))),
        new("an image digest that is none", "engine.image-digest", false, (_, _) => Failed(Engine.Of("170.5.96", "sha256:0"))),
        new("a fingerprint that is none", "fingerprint.malformed", false, (_, _) => Failed(Fingerprint.Parse("0"))),
        new("an engine outside the pin's window", "toolchain.outside-window", false, (_, _) =>
            Made(Pin.Of("170.6.10", "170.5.96")).Rejects(Made(Engine.Of("170.7.2"))) ?? throw new InvalidOperationException("the window admitted a newer engine")),
        new("a toolchain ledger with no row for this estate", "toolchain.unrecorded", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 2.9.0 | UNPINNED | — |"), "3.0.0+0123abcd"))),
        new("a toolchain ledger whose pin is no release", "toolchain.malformed", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 3.0.0 | latest | — |"), "3.0.0"))),
        new("a toolchain ledger whose release before is newer than its pin", "toolchain.window-order", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 3.0.0 | 170.5.96 | 170.6.10 |"), "3.0.0"))),
        new("an element with a blank type", "element.type-blank", true, (_, planted) => Failed(ElementKey.Of(" ", Made(Name.Of(planted))))),
        new("an element with no name", "element.name-missing", false, (_, _) => Failed(ElementKey.Of("Table", default))),
        new("a child element named in two parts", "element.child-name", false, (_, _) => Failed(ElementKey.Of(Table, "Column", Made(Name.Of("dbo", "Email"))))),
        new("an element with a property given twice", "element.property-name", false, (_, _) =>
            Failed(Element.Of(Table, [new("Nullable", new Value.Boolean(true)), new("Nullable", new Value.Boolean(false))], []))),
        new("an element with a relationship given twice", "element.relationship-name", false, (_, _) =>
            Failed(Element.Of(Table, [], [Element.Relationship.Of("Columns", [Table]), Element.Relationship.Of("Columns", [Table])]))),
        new("a model with two elements on one key", "change.duplicate-key", false, (_, _) =>
            Failed(Change.Between(SortedArray.Of(Made(Element.Of(Table, [], [])), Made(Element.Of(Table, [new("Nullable", new Value.Null())], []))), [], []))),
        new("a collation name with no case rule", "model.collation", true, (_, planted) => Failed(Collation.Of("Latin1_General_" + planted))),

        new("ESTATE_TOOL naming no tool folder", "tool.missing", false, (scratch, _) => Failed(Ssdt.Tool(Bare(scratch), Bare(scratch), scratch))),
        new("no tool folder anywhere", "tool.missing", false, (scratch, _) => Failed(Ssdt.Tool(Bare(scratch), null, scratch))),
        new("a build against no tool folder", "tool.missing", false, (scratch, _) => Failed(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), Sdk))),
        new("a build of no project", "build.no-project", false, (scratch, _) => Failed(Ssdt.Build(Path.Combine(scratch, "none.sqlproj"), Bare(scratch), Output(scratch), Sdk))),
        new("a repository holding two projects", "build.no-project", false, (scratch, _) =>
        {
            Written(scratch, "a/One.sqlproj", "<Project />");
            Written(scratch, "b/Two.sqlproj", "<Project />");
            return Failed(Ssdt.Project(scratch, null));
        }),
        new("a build without the SDK band", "sdk.missing", false, (scratch, _) => Failed(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), (_, _) => (0, "8.0.100 [sdk]\n")))),
        new("a build that fails", "build.failed", false, (scratch, _) => Failed(Ssdt.Build(Project(scratch), Hollow(scratch), Output(scratch), Sdk))),
        new("a package that is none", "package.unreadable", false, (scratch, _) => Failed(Ssdt.Load(Written(scratch, "not.dacpac", "not a package")))),
        new("a refactorlog that is none", "refactorlog.unreadable", false, (scratch, _) => Failed(Ssdt.RefactorLog(Written(scratch, "not.refactorlog", "not a refactorlog")))),
        new("a refactorlog entry naming no object", "refactorlog.name", false, (_, _) =>
        {
            using var package = new Ssdt.Package(Model(), null, null,
                [new Ssdt.RefactorEntry("0a1b2c3d-0000-4000-8000-000000000001", "Rename Refactor", null, "[dbo].[Customer", "SqlTable", null, null, "[Client]", null)],
                new Dictionary<string, string>(StringComparer.Ordinal));
            return Failed(Ssdt.Elements(package));
        }),
        new("two objects of a model keyed alike", "model.duplicate-key", false, (_, _) =>
        {
            using var model = Model("CREATE TABLE dbo.Customer (Id INT NOT NULL);", "CREATE TABLE dbo.Customer (Id INT NOT NULL);");
            return Failed(Ssdt.Elements(model));
        }),

        new("a git program that does not start", "git.missing", false, (scratch, _) => Failed(Git.At(scratch, "HEAD", git: Path.Combine(scratch, "no-git")))),
        new("a folder in no repository", "git.not-a-repository", false, (scratch, _) => Failed(Git.ChangedPaths(Path.Combine(scratch, "no-repository"), "HEAD~1", "HEAD"))),
        new("a ref that names no commit", "ref.unresolved", false, (scratch, _) => InRepository(scratch, root => Git.At(root, "no-such-tag"))),
        new("two refs whose histories never meet", "ref.unrelated", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "switch", "-q", "--orphan", "unrelated");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "unrelated");
            return Git.MergeBase(root, "main", "unrelated");
        })),
        new("a branch name git does not take", "git-branch.malformed", false, (scratch, _) => InRepository(scratch, root => Git.CommitAndPush(root, Evidence, "evidence", "estate/..evidence"))),
        new("a branch that exists here", "git-branch.exists", false, (scratch, _) => InRepository(scratch, root =>
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

        new("no posture", "posture.missing", false, (scratch, _) => Failed(Profiles.Environments(scratch))),
        new("a posture that is not JSON", "posture.unreadable", true, (scratch, planted) => Failed(Profiles.Environments(Estate(scratch, "{ \"environments\": { \"dev\": " + planted + " } }")))),
        new("a posture giving a key twice", "posture.unreadable", true, (scratch, planted) => Posture(scratch, Dev("\"readers\": [" + Quoted(planted) + "], \"readers\": []"))),
        new("a literal connection string", "posture.literal-connection", true, (scratch, planted) => Posture(scratch, Dev(connection: "Server=db;User ID=estate;Password=" + planted))),
        new("a literal connection string as a key", "posture.literal-connection", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Data Source=db;Password=" + planted + "\": \"env:A\" }"))),
        new("an unknown key", "posture.unknown-key", true, (scratch, planted) => Posture(scratch, Dev("\"password\": " + Quoted(planted)))),
        new("an unknown key beside a SQLCMD literal", "posture.unknown-key", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": { \"literal\": \"dev\", \"sensitive\": false, \"secret\": " + Quoted(planted) + " } }"))),
        new("a value of the wrong JSON kind", "posture.malformed", true, (scratch, planted) => Posture(scratch, Dev("\"readers\": " + Quoted(planted)))),
        new("an environment with no connection", "posture.malformed", true, (scratch, planted) =>
            Posture(scratch, "\"dev\": { \"profile\": \"" + Pipeline + "\", \"readers\": [" + Quoted(planted) + "] }")),
        new("a SQLCMD literal not marked non-sensitive", "posture.unmarked-literal", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": { \"literal\": " + Quoted(planted) + " } }"))),
        new("a SQLCMD value that is a bare literal", "posture.unmarked-literal", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": " + Quoted(planted) + " }"))),
        new("a connection that is no reference", "reference.malformed", true, (scratch, planted) => Posture(scratch, Dev(connection: "env:" + planted))),
        new("a file reference that is a connection string", "reference.malformed", true, (_, planted) =>
            Failed(SecretReference.Of("--connection", "file:Server=db;User ID=sa;Password=" + planted))),
        new("a scratch server that is neither docker nor localdb", "posture.malformed", true, (scratch, planted) =>
            Failed(Profiles.Environments(Estate(scratch, "{ \"environments\": {}, \"scratchServer\": " + Quoted(planted) + " }")))),
        new("an environment misnamed", "posture.environment-name", true, (scratch, planted) => Posture(scratch, Dev("\"readers\": [" + Quoted(planted) + "]", name: "DEV"))),
        new("a reader group given twice", "posture.readers", true, (scratch, planted) => Posture(scratch, Dev("\"readers\": [" + Quoted(planted) + ", " + Quoted(planted) + "]"))),
        new("a profile path outside the estate", "posture.profile-path", true, (scratch, planted) => Posture(scratch, Dev(profile: "../" + planted + ".publish.xml"))),
        new("a SQLCMD variable given twice in two cases", "posture.sqlcmd-repeated", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"Tag\": \"env:A\", \"tag\": \"env:B\" }, \"readers\": [" + Quoted(planted) + "]"))),
        new("a classification that is none", "posture.classification", true, (scratch, planted) => Posture(scratch, Dev("\"classification\": " + Quoted(planted)))),
        new("a synthetic environment unconfirmed", "posture.unconfirmed", true, (scratch, planted) =>
            Posture(scratch, Dev("\"classification\": \"synthetic\", \"readers\": [" + Quoted(planted) + "]"))),
        new("a confirmation with no date", "posture.confirmation", true, (scratch, planted) => Posture(scratch, Dev("\"classification\": \"synthetic\", \"confirmedBy\": " + Quoted(planted)))),
        new("a SQLCMD variable misnamed", "sqlcmd.name", true, (scratch, planted) => Posture(scratch, Dev("\"sqlcmd\": { \"Tag Name\": \"env:A\" }, \"readers\": [" + Quoted(planted) + "]"))),
        new("a SQLCMD literal under a credential's name in the posture", "sqlcmd.literal-credential", true, (scratch, planted) =>
            Posture(scratch, Dev("\"sqlcmd\": { \"ServicePassword\": { \"literal\": " + Quoted(planted) + ", \"sensitive\": false } }"))),
        new("a script using a variable with no value", "sqlcmd.undefined", true, (_, planted) =>
            Failed(SqlCmdVariable.Substitute("PRINT '$(Missing)';", new Dictionary<string, string>(StringComparer.Ordinal) { ["Tag"] = planted }))),

        new("no profile", "profile.missing", false, (scratch, _) => Failed(Profiles.Load(Path.Combine(scratch, "none.publish.xml")))),
        new("a profile that is not XML", "profile.unreadable", true, (scratch, planted) => Failed(Profiles.Load(Written(scratch, "broken.publish.xml", "<Project>" + planted + "</Projec>")))),
        new("a profile DacFx does not read", "profile.unreadable", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>" + planted + "</BlockOnPossibleDataLoss>")))),
        new("a profile holding a password", "profile.password", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "<TargetConnectionString>Data Source=db;User ID=sa;Password=" + planted + "</TargetConnectionString>")))),
        new("a profile holding a password a comment splits", "profile.password", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "", ("LinkedServer", "Server=db;User ID=sa;Pass<!-- -->word=" + planted))))),
        new("a profile giving a SQLCMD value that is a connection string", "profile.literal-connection", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "", ("LinkedServer", "Data Source=" + planted + ";Initial Catalog=Orders;Integrated Security=True"))))),
        new("a profile that allows data loss", "profile.data-loss-allowed", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted))))),
        new("a named environment whose profile allows data loss", "profile.data-loss-allowed", true, (scratch, planted) =>
        {
            var root = Estate(scratch, Environments(Dev(profile: "estate/profiles/relaxed.publish.xml")));
            File.Move(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted)), Path.Combine(root, "estate", "profiles", "relaxed.publish.xml"));
            return Failed(Profiles.Of(Made(Profiles.Environments(root)).Single(), root));
        }),
        new("a SQLCMD literal under a credential's name in a profile", "sqlcmd.literal-credential", true, (scratch, planted) =>
            Failed(Profiles.Load(Profile(scratch, "", ("ApiToken", planted))))),

        new("a target of no form the grammar knows", "target.unknown", true, (_, planted) => Failed(SqlServer.Target.Parse("sql:" + planted))),
        new("a copy named as no copy can be", "copy.unregistered", true, (_, planted) => Failed(SqlServer.Target.Parse("copy:" + planted, "--target"))),
        new("a literal connection string where a target goes", "connection.literal", true, (_, planted) =>
            Failed(SqlServer.Target.Parse("Server=db;User ID=sa;Password=" + planted, "--target"))),
        new("a git ref where a database is asked for", "target.not-a-database", false, (scratch, _) => Failed(SqlServer.Resolve(Target("ref:main"), scratch))),
        new("an environment the posture does not name", "target.unnamed", false, (scratch, _) => Failed(SqlServer.Resolve(Target("env:qa"), Estate(scratch, Environments(Dev()))))),
        new("the synthetic copy before its milestone", "synthetic-copy.not-built", false, (scratch, _) => Failed(SqlServer.Resolve(Target("synthetic-copy"), scratch))),
        new("a connection whose variable is unset", "connection.unresolved", false, (scratch, _) =>
            Failed(SqlServer.Resolve(Target("env:dev"), Estate(scratch, Environments(Dev(connection: "env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant())))))),
        new("a connection file holding no connection string", "connection.malformed", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("env:dev"), Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "garbled " + planted)))))))),
        new("a connection file git tracks", "reference.tracked", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "estate/posture.json", Environments(Dev(connection: "file:estate/dev.connection")));
            OwnerOnly(Written(root, "estate/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            Arrange(root, "add", "--", "estate/dev.connection");
            Arrange(root, "commit", "-q", "-m", "the connection file");
            return SqlServer.Resolve(Target("env:dev"), root);
        })),
        new("a connection file git does not ignore", "reference.not-ignored", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "estate/posture.json", Environments(Dev(connection: "file:estate/dev.connection")));
            OwnerOnly(Written(root, "estate/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            return SqlServer.Resolve(Target("env:dev"), root);
        })),
        new("a connection file named by a spelling its folder does not list", "reference.unlisted", OperatingSystem.IsWindows(), (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, ".gitignore", ".estate/\n");
            Written(root, "estate/posture.json", Environments(Dev(connection: "file:.estate/dev.connection::$DATA")));
            var file = OwnerOnly(Written(root, ".estate/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            return OperatingSystem.IsWindows()   // Windows opens the default data stream as name::$DATA; elsewhere that name opens no file, and the driver asks io/SqlServer of it directly
                ? SqlServer.Resolve(Target("env:dev"), root).Map(database => database.Target)
                : SqlServer.Listed("env:dev's connection, file:.estate/dev.connection::$DATA,", file + "::$DATA");
        })),
        new("a connection file in a folder this identity cannot list", "reference.unlistable", true, (scratch, planted) =>
        {
            var root = Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "locked/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Denied(Path.Combine(scratch, "locked"), () => Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file this identity cannot read", "reference.unreadable", true, (scratch, planted) =>
        {
            var root = Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Denied(Path.Combine(scratch, "dev.connection"), () => Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file whose attributes this identity cannot read", "reference.inaccessible", true, (scratch, planted) =>
        {
            var root = Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "locked/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Unexaminable(Path.Combine(scratch, "locked", "dev.connection"), () => Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file of an estate in no git repository", "reference.no-repository", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("env:dev"), Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Password=" + planted))))))),
        new("a connection file its group can read, where files carry a Unix mode", "reference.readable-by-others", !OperatingSystem.IsWindows(), (scratch, planted) => OperatingSystem.IsWindows()
            ? SqlServer.ReadableByOthers("env:dev's connection, file:.estate/dev.connection,", (UnixFileMode)0b110_100_000) ?? throw new InvalidOperationException("mode 0640 was not refused")
            : Failed(SqlServer.Resolve(Target("env:dev"), Initialized(Estate(scratch, Environments(Dev(connection:
                GroupReadable(Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted))))))))),
        new("a copy the registry does not hold", "copy.unregistered", false, (scratch, _) => Failed(SqlServer.Resolve(Target("copy:estate_nowhere_1_00000000"), scratch))),
        new("a copy registry that is not JSON", "registry.unreadable", true, (scratch, planted) =>
        {
            Directory.CreateDirectory(Path.Combine(scratch, ".estate"));
            File.WriteAllText(Path.Combine(scratch, ".estate", "copies.json"), "{ \"copies\": [ " + planted);
            return Failed(SqlServer.Resolve(Target("copy:estate_nowhere_1_00000000"), scratch));
        }),
        new("a copy made on the host an environment's reference names", "copy.named-host", true, (scratch, planted) => Failed(SqlServer.Resolve(Target("copy:" + Copied),
            Registered(Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=127.0.0.1,1433;User ID=reader;Password=" + planted))))))))),
        new("a copy beside an environment whose connection SqlClient cannot read", "connection.malformed", true, (scratch, planted) => Failed(SqlServer.Resolve(Target("copy:" + Copied),
            Registered(Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Nonsense " + planted + " = 1"))))))))),
        new("no scratch server server anywhere", "scratch-server.missing", false, (scratch, _) => Failed(ScratchServer.ServerName(null, Path.Combine(scratch, "no-sql.env"), localDb: false))),
        new("a scratch server server SqlClient cannot read", "scratch-server.missing", true, (scratch, planted) =>
            Failed(ScratchServer.ServerName("Server=db;Password=" + planted + ";Nonsense " + planted + " = 1", Path.Combine(scratch, "no-sql.env"), localDb: false))),
        new("a named environment's login denied", "server.denied", true, (scratch, planted) => DevDatabase(scratch).ErrorOf(18456, "Login failed for user '" + planted + "'.")),
        new("a named environment that does not answer", "server.unreachable", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(53, "A network-related or instance-specific error occurred while establishing a connection to " + planted + ".")),
        new("a named environment's statement failing", "server.failed", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(245, "Conversion failed when converting the nvarchar value '" + planted + "' to data type int.")),
        new("a SQL Server error DacFx quotes by its number, with no SqlException inside", "server.failed", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(new DacServicesException("Could not deploy package.", new InvalidOperationException("Error SQL72014: Core Microsoft SqlClient Data Provider: "
                + "Msg 2627, Level 14, State 1, Line 1 Violation of PRIMARY KEY constraint 'PK_Customer'. The duplicate key value is (" + planted + ").")))),
        new("DacFx failing with no SQL Server error inside", "dacfx.failed", false, (scratch, _) =>
            DevDatabase(scratch).ErrorOf(new DacServicesException("An error occurred during deployment plan generation. Deployment cannot continue.",
                new InvalidOperationException("A project which specifies SQL Server vNext as the target platform cannot be published to SQL Server 2022.")))),
        new("an aggregate query the allowlist refuses", "aggregate-query.refused", true, (_, planted) =>
            Failed(SqlServer.AggregateQuery.Of("SELECT MAX(Email) FROM dbo.Customer WHERE Name = N'" + planted + "';", "dbo.Customer.Email Fits"))),
        new("a SQLCMD reference that does not resolve", "sqlcmd.unresolved", false, (scratch, _) =>
        {
            var root = Initialized(Estate(scratch, Environments(Dev("\"sqlcmd\": { \"ServiceToken\": \"env:ESTATE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "\" }",
                connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev")))));
            File.Copy(Path.Combine(Repository.Root, "tests", "Golden", "project", "profiles", "pipeline.publish.xml"), Path.Combine(root, "estate", "profiles", "pipeline.publish.xml"));
            var dev = Made(SqlServer.Resolve(Target("env:dev"), root));
            return Failed(SqlServer.Plan(Path.Combine(scratch, "none.dacpac"), dev, Made(Profiles.Of(((SqlServer.EnvironmentDatabase)dev).Environment, root))));
        }),
        new("a SQLCMD reference to a file git tracks", "reference.tracked", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "estate/posture.json", Environments(Dev("\"sqlcmd\": { \"ServiceToken\": \"file:estate/token.txt\" }",
                connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev"))));
            OwnerOnly(Written(root, "estate/token.txt", planted));
            Arrange(root, "add", "--", "estate/token.txt");
            Arrange(root, "commit", "-q", "-m", "the token");
            Directory.CreateDirectory(Path.Combine(root, "estate", "profiles"));
            File.Copy(Path.Combine(Repository.Root, "tests", "Golden", "project", "profiles", "pipeline.publish.xml"), Path.Combine(root, "estate", "profiles", "pipeline.publish.xml"));
            var dev = Made(SqlServer.Resolve(Target("env:dev"), root));
            return SqlServer.Plan(Path.Combine(scratch, "none.dacpac"), dev, Made(Profiles.Of(((SqlServer.EnvironmentDatabase)dev).Environment, root)));
        })),

        new("a flag the verb does not take", "arguments.unknown-flag", false, (_, _) => Failed(Contract.Flags(["--no-such-flag"], [], [], []))),
        new("a required flag absent", "arguments.missing-flag", false, (_, _) => Failed(Contract.Flags([], ["--from"], [], []))),
        new("estate check with no check named", "arguments.unknown-check", false, (scratch, _) => Carried(Verbs.Check(new Checkout(scratch, scratch, null), []))),
        new("a word that names no verb", "arguments.unknown-verb", false, (scratch, _) => Answered(["frobnicate"], new Checkout(scratch, scratch, null))),
        new("a verb this build has no body for", "verb.not-built", false, (scratch, _) => Answered(["predict"], new Checkout(scratch, scratch, null))),
        new("an exception no verb expected", "internal.unexpected", false, (scratch, _) => Answered(["read", "--from", "dacpac:none.dacpac"], new Checkout(scratch, null!, null))),
    ];

    /// <summary>The error estate answers a command with, run in this process against <paramref name="here"/>: the one finding of severity error its --json answer carries.</summary>
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
    private const string Copied = "estate_host_1_0a1b2c3d";

    private static SqlServer.Target Target(string text) => Made(SqlServer.Target.Parse(text));

    /// <summary>The estate's root with .estate/copies.json holding <see cref="Copied"/>, as io/ScratchServer writes a row, so copy: reaches R15 without a server.</summary>
    private static string Registered(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".estate"));
        File.WriteAllText(Path.Combine(root, ".estate", "copies.json"),
            "{ \"copies\": [ { \"name\": \"" + Copied + "\", \"server\": \"localhost,11433\", \"host\": \"host\", \"pid\": 1, \"created\": \"2026-09-24T00:00:00Z\" } ] }");
        return root;
    }

    /// <summary>The named environment dev, its connection a file under the scratch folder naming a server that is never reached.</summary>
    private static SqlServer.Database DevDatabase(string scratch) =>
        Made(SqlServer.Resolve(Target("env:dev"), Initialized(Estate(scratch, Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev")))))));

    /// <summary>
    /// A file: reference to a file written under the scratch folder, outside the estate's root and in no git repository, and read by its
    /// owner alone; its path with '/' so the posture's JSON carries it as it is.
    /// </summary>
    private static string Reference(string scratch, string file, string text) => "file:" + OwnerOnly(Written(scratch, file, text)).Replace('\\', '/');

    /// <summary>On Linux and macOS, the file's mode set to 0600, as io/SqlServer reads a connection file; Windows keeps no such mode.</summary>
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
        using var icacls = Process.Start(new ProcessStartInfo("icacls", arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var errors = icacls.StandardError.ReadToEndAsync();
        var output = icacls.StandardOutput.ReadToEnd();
        icacls.WaitForExit();
        if (icacls.ExitCode != 0)
        {
            throw new InvalidOperationException("icacls " + string.Join(' ', arguments) + " exited " + icacls.ExitCode + ": " + output + errors.Result);
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

    /// <summary>The estate's root made a git repository, as a clone is, so io/SqlServer can ask git whether it would commit a file: reference's file.</summary>
    private static string Initialized(string root)
    {
        Arrange(root, "init", "-q");
        return root;
    }

    /// <summary>Each error code the kernel, io and the cli construct, as their sources write it: a literal code, or the literal start of a composed one (element.).</summary>
    public static IEnumerable<string> InTheSources() => ConstructedIn().Select(c => c.Code).Distinct().Order(StringComparer.Ordinal);

    /// <summary>Each source file of the kernel, io and the cli with each code, or literal start of a composed code, it constructs an Error of.</summary>
    public static IEnumerable<(string File, string Code)> ConstructedIn() => Repository.Files
        .Where(f => (f.StartsWith("kernel/", StringComparison.Ordinal) || f.StartsWith("io/", StringComparison.Ordinal) || f.StartsWith("cli/", StringComparison.Ordinal))
            && f.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(Repository.Read(f), @"new\s+Error\(\s*""([a-z0-9.-]+)""").Select(m => (File: f, Code: m.Groups[1].Value)))
        .Distinct();

    /// <summary>Whether a code is constructed by the kernel or the cli alone: the files that write it, or the start of it, lie outside io/.</summary>
    public static bool KernelOrCli(string code) => ConstructedIn()
        .Where(c => c.Code == code || (c.Code.EndsWith('.') && code.StartsWith(c.Code, StringComparison.Ordinal)))
        .ToList() is { Count: > 0 } sites && sites.All(c => !c.File.StartsWith("io/", StringComparison.Ordinal));

    private static (int Exit, string Output)? Sdk(string file, IReadOnlyList<string> arguments) =>
        (0, (string)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")))!["sdk"]!["version"]! + " [sdk]\n");

    /// <summary>A dev environment in posture JSON: its connection, its profile and whatever else is given.</summary>
    private static string Dev(string extra = "", string connection = "env:ESTATE_DEV", string profile = Pipeline, string name = "dev") =>
        Quoted(name) + ": { \"connection\": " + Quoted(connection) + ", \"profile\": " + Quoted(profile) + (extra.Length > 0 ? ", " + extra : "") + " }";

    private static string Environments(string environments) => "{ \"environments\": { " + environments + " } }";

    private static Error Posture(string scratch, string environments) => Failed(Profiles.Environments(Estate(scratch, Environments(environments))));

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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, file))!);
        File.WriteAllText(Path.Combine(scratch, file), text);
        return Path.Combine(scratch, file);
    }

    /// <summary>An estate's root whose toolchain ledger is the sample's, its one row replaced.</summary>
    private static string Ledger(string scratch, string row)
    {
        var sample = File.ReadAllText(Path.Combine(Repository.Root, "tests", "Golden", "estate", "ledgers", "toolchain.md"));
        Written(scratch, Doctor.Ledger, sample.Replace("| 2026-09-24 | 3.0.0 | UNPINNED | — |", row, StringComparison.Ordinal));
        return scratch;
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

    /// <summary>A model built in memory, each script added as its own source, as io/Ssdt.Elements reads one.</summary>
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
    private static Error InRepository<T>(string scratch, Func<string, Result<T>> drive)
    {
        var root = Directory.CreateDirectory(Path.Combine(scratch, "repository")).FullName;
        try
        {
            Arrange(root, "init", "-q", "--initial-branch=main");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "estate");
            return Failed(drive(root));
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

    /// <summary>The error a verb's answer carries as its one finding of severity error, where the cli fails inside a verb rather than in a Result.</summary>
    private static Error Carried(Envelope answer) => answer.Findings is [{ Severity: Severity.Error, Remedy: { } remedy } finding]
        ? new Error(finding.Code, finding.Message, remedy)
        : throw new InvalidOperationException("the answer carries no one error: " + answer.Message);

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new InvalidOperationException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => result.Match(value => throw new InvalidOperationException("accepted where an error was due: " + value), error => error);
}
