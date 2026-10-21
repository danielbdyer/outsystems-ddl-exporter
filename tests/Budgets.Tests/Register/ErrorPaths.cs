using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Contract = DbChange.Cli.Contract;
using Schema = DbChange.Kernel.Schema;

namespace DbChange.Budgets.Tests.Register;

/// <summary>
/// Every way to an error the kernel, io and the cli construct, each with an input that takes it there. Register.Errors reads each
/// error for the register; Io.Tests' "no output contains Password=" plants a password in every input that can carry a value
/// (<see cref="Case.Plants"/>) and searches what comes back. A driver writes only under the scratch folder it is given, one per
/// case, and leaves it deletable. The kernel's schema errors, io/Ssdt's and io/Git's quote what they reject, a name, a
/// version, a path, a ref or a branch, and plant nothing. io/Git's are reached in a repository made under the scratch folder.
/// io/SqlServer's and io/LocalServer's reach no server: each is an error before anything connects, and a SQL Server or DacFx error reaches
/// its code through Database.ErrorOf, the one method that classifies a failure against a server, which io/DacFx.Failed hands each DacFx
/// failure to. The local server's own choice
/// and Create on a given server are io's alone, so R15 is reached through copy: and a planted registry row. The cli's reject
/// arguments, through Contract.Flags and dbchange check's own answer.
/// </summary>
internal static partial class ErrorPaths
{
    /// <summary>
    /// One way to an error: what it is, the code it must take, whether its input carries the planted value, and its driver (scratch,
    /// planted).
    /// </summary>
    public sealed record Case(string Label, string Code, bool Plants, Func<string, string, Error> Drive);

    private const string Pipeline = "dbchange/profiles/pipeline.publish.xml";

    private const string Express = "Express Edition (64-bit)";

    private static readonly ElementKey Table = Made(ElementKey.Of("Table", Made(Name.Of("dbo", "Customer"))));

    /// <summary>The paths a CommitAndPush of the evidence names, none of which the scratch repository's one empty commit holds.</summary>
    private static readonly string[] Evidence = ["dbchange/evidence.shape.json"];

    public static IReadOnlyList<Case> All { get; } =
    [
        new("an empty name part", "name.blank", false, (_, _) => Failed(Name.Of(""))),
        new("an overlong name part", "name.too-long", true, (_, planted) => Failed(Name.Of(planted + new string('x', 129)))),
        new("a DacFx version that is none", "toolchain.dacfx-version", false, (_, _) => Failed(DacFxVersion.Of("v170"))),
        new("a DacFx assembly that carries no version", "toolchain.dacfx-version", false, (_, _) => Failed(DacFx.VersionOf("", null))),
        new("a SQL Server edition that is blank", "server.edition", false, (_, _) => Failed(Server.Of("16.0.4305.5", " ", 160))),
        new("a SQL Server product version of other than four numbers", "server.product-version", false,
            (_, _) => Failed(Server.Of("16.0", Express, 160))),
        new("a compatibility level SQL Server never had", "server.compatibility-level", false,
            (_, _) => Failed(Server.Of("16.0.4305.5", Express, 165))),
        new("a SQL Server older than the package's platform", "toolchain.server-window", false, (_, _) =>
            Made(Server.Of("15.0.4382.1", Express, 150)).Refuses(Made(Platform.Of("Sql160")))
                ?? throw new InvalidOperationException("the window admitted an older server")),
        new("a fingerprint that is none", "fingerprint.malformed", false, (_, _) => Failed(Fingerprint.Parse("0"))),
        new("a DacFx release outside the pin's window", "toolchain.outside-window", false, (_, _) =>
            Made(Pin.Of("170.6.10", "170.5.96")).Rejects(Made(DacFxVersion.Of("170.7.2")))
                ?? throw new InvalidOperationException("the window admitted a newer release")),
        new("a toolchain ledger with no row for this dbchange version", "toolchain.unrecorded", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 2.9.0 | UNPINNED | — |"), "3.0.0+0123abcd"))),
        new("a toolchain ledger whose pin is no release", "toolchain.malformed", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 3.0.0 | latest | — |"), "3.0.0"))),
        new("a toolchain ledger whose release before is newer than its pin", "toolchain.window-order", false, (scratch, _) =>
            Failed(Doctor.Toolchain(Ledger(scratch, "| 2026-09-24 | 3.0.0 | 170.5.96 | 170.6.10 |"), "3.0.0"))),
        new("an element with a blank type", "element.type-blank", true, (_, planted) => Failed(ElementKey.Of(" ", Made(Name.Of(planted))))),
        new("an element with no name", "element.name-missing", false, (_, _) => Failed(ElementKey.Of("Table", default))),
        new("a child element named in two parts", "element.child-name", false, (_, _) =>
            Failed(ElementKey.Of(Table, "Column", Made(Name.Of("dbo", "Email"))))),
        new("an element with a property given twice", "element.property-name", false, (_, _) =>
            Failed(Element.Of(Table, [new("Nullable", new Value.Boolean(true)), new("Nullable", new Value.Boolean(false))], []))),
        new("an element with a relationship given twice", "element.relationship-name", false, (_, _) =>
            Failed(Element.Of(Table, [], [Element.Relationship.Of("Columns", [Table]), Element.Relationship.Of("Columns", [Table])]))),
        new("a model with two elements on one key", "model.duplicate-key", false, (_, _) =>
            Failed(Change.Between(
                SortedArray.Of(Made(Element.Of(Table, [], [])), Made(Element.Of(Table, [new("Nullable", new Value.Null())], []))), [],
                []))),
        new("a collation name with no case rule", "model.collation", false, (_, _) => Failed(Collation.Of("Latin1_General"))),
        new("a column with no data type, as a computed column has none", "model.unresolved", false, (_, _) =>
            Failed(DbChange.Kernel.ColumnType.Of(Made(Element.Of(Made(ElementKey.Of(Table, "Column", Made(Name.Of("Twice")))), [], []))))),
        new("a foreign key the change creates naming no parent column", "model.unresolved", false, (_, _) =>
        {
            var column = Made(ElementKey.Of(Table, "Column", Made(Name.Of("Id"))));
            var key = Made(ElementKey.Of("ForeignKeyConstraint", Made(Name.Of("dbo", "FK_Customer"))));
            SortedArray<Element> after =
                [Made(Element.Of(key, [], [Element.Relationship.Of("Columns", [column]), Element.Relationship.Of("Host", [Table])]))];
            return Failed(Preconditions.Of([], after, Made(Change.Between([], after, [])), []));
        }),
        new("a table whose column the model does not hold", "model.unresolved", false, (_, _) =>
            Failed(Schema.Of([Made(Element.Of(Table, [],
                [Element.Relationship.Of("Columns", [Made(ElementKey.Of(Table, "Column", Made(Name.Of("Email"))))])]))]))),
        new("an identity seed beyond bigint", "model.identity", false, (_, _) =>
        {
            var column = Made(ElementKey.Of(Table, "Column", Made(Name.Of("Id"))));
            return Failed(Schema.Of([
                Made(Element.Of(Table, [], [Element.Relationship.Of("Columns", [column])])),
                Made(Element.Of(column,
                    [new("IdentityIncrement", new Value.Text("1")), new("IdentitySeed", new Value.Text("99999999999999999999")),
                        new("IsIdentity", new Value.Boolean(true))],
                    [Element.Relationship.Of("DataType", [Made(ElementKey.Of("DataType", Made(Name.Of("decimal"))))])])),
            ]));
        }),
        new("two tables referencing each other through NOT NULL foreign keys", "synthetic-copy.cycle", false, (_, _) =>
        {
            var (hen, egg) = (Made(Name.Of("dbo", "Hen")), Made(Name.Of("dbo", "Egg")));
            Kernel.Table Referencing(Name table, Name parent) => new(table, [], null, [],
                [new Reference(Made(ElementKey.Of("ForeignKeyConstraint", Made(Name.Of("dbo", "FK_" + table.Base)))), null, parent,
                    [new Reference.Leg(0, Made(Name.Of(parent.Base + "Id")), Made(Name.Of("Id")))], ReferentialAction.NoAction,
                    ReferentialAction.NoAction, false, false)], [], [], null);
            return Order.Of(new Schema([Referencing(hen, egg), Referencing(egg, hen)])).Cycles.Single().Error;
        }),
        new("a precondition on a column the environment lacks and the change does not create", "plan.missing-object", false, (_, _) =>
            Failed(Coordinates.Of(new Precondition(Table.Name, [Made(Name.Of("Nickname"))], new PreconditionKind.NotNull()),
                new Change([], [], [], []), []))),
        new("a site whose table the existing data holds no state for", "plan.uncounted", false, (_, _) =>
            Failed(DbChange.Kernel.Outcome.Predict(new ExistingData([]),
                [new BlockOnPossibleDataLossSite(Table.Name, "ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (256) NOT NULL;")],
                [], []))),
        new("a computed column's precondition no person declared a state for", "plan.undeclared", false, (_, _) =>
            Failed(DbChange.Kernel.Outcome.Predict(ExistingData.Of([]), [], [new Precondition(Table.Name, [Made(Name.Of("RefNum"))],
                new PreconditionKind.Computed("(CONVERT([int],[Ref]))"))], []))),
        new("a planted precondition on a table the copy lacks and the change does not create", "plan.missing-object", false, (_, _) =>
            Failed(Witness.Plan(new Schema([]), [new Precondition(Table.Name, [], new PreconditionKind.TableEmpty())],
                new ExistingData([])))),
        new("a planted precondition the existing data holds no state for", "plan.uncounted", false, (_, _) =>
            Failed(Witness.Plan(new Schema([new Kernel.Table(Table.Name, [], null, [], [], [], [], null)]),
                [new Precondition(Table.Name, [], new PreconditionKind.TableEmpty())], new ExistingData([])))),
        new("existing data answering a foreign key over a NOT NULL column clean and its parent empty", "plan.contradictory-answers", false,
            (_, _) =>
            {
                var (account, id, accountId) = (Made(Name.Of("dbo", "Account")), Made(Name.Of("Id")), Made(Name.Of("AccountId")));
                var integer = new DbChange.Kernel.ColumnType(Made(Name.Of("int")), 0, false, 0, 0, null);
                Kernel.Table Holding(Name table, Name column) => new(table,
                    [new Kernel.Column.Stored(1, column, false, integer, null, null, null, false, false)], null, [], [], [], [], null);
                var foreign = new Precondition(Table.Name, [accountId],
                    new PreconditionKind.ForeignKey(account, [new PreconditionKind.ForeignKey.Pair(accountId, id)]));
                var accounts = new Precondition(account, [], new PreconditionKind.TableEmpty());
                return Failed(Witness.Plan(new Schema([Holding(account, id), Holding(Table.Name, accountId)]), [accounts, foreign],
                    new ExistingData([new PreconditionState(accounts, false, false), new PreconditionState(foreign, true, false)])));
            }),
        new("a column renamed by typing, with no refactorlog entry", "rename.column-without-entry", false, (_, _) =>
            Refusal(["CREATE TABLE dbo.Customer (Id INT NOT NULL, Phone NVARCHAR(40) NULL);"],
                ["CREATE TABLE dbo.Customer (Id INT NOT NULL, Mobile NVARCHAR(40) NULL);"])),
        new("a table renamed by typing, with no refactorlog entry", "rename.table-without-entry", false, (_, _) =>
            Refusal(["CREATE TABLE dbo.Customer (Id INT NOT NULL);"], ["CREATE TABLE dbo.Client (Id INT NOT NULL);"])),
        new("a table moved to another schema by typing, with no refactorlog entry", "rename.schema-move-without-entry", false, (_, _) =>
            Refusal(["CREATE TABLE dbo.Customer (Id INT NOT NULL);"],
                ["CREATE SCHEMA sales;", "CREATE TABLE sales.Customer (Id INT NOT NULL);"])),
        new("a column created on a table CDC tracks", "cdc.column-list", false, (_, _) =>
            Refusal(["CREATE TABLE dbo.Customer (Id INT NOT NULL);"], ["CREATE TABLE dbo.Customer (Id INT NOT NULL, Tier INT NULL);"],
                Classify.Inputs.None with { CdcTracked = [Table.Name] })),
        new("a rebuild of a table CDC tracks in the environment's plan", "cdc.table-rebuild", false, (_, _) =>
            Refusal(["CREATE TABLE dbo.Customer (Id INT NOT NULL);"], ["CREATE TABLE dbo.Customer (Id INT NOT NULL);"],
                Classify.Inputs.None with { CdcTracked = [Table.Name], Plan = [new ScriptStatement.Rebuilds(Table.Name)] })),
        new("a recognize: block with a clause of no known kind", "pattern.malformed", false, (_, _) =>
            Failed(Pattern.Parse("add-optional", "recognize:\n  match: frobbed Column\n  ships: one release\n"))),
        new("an operation's file this identity cannot read", "pattern.unreadable", false, (scratch, _) =>
            Denied(Written(scratch, "patterns/add-optional.md", "# add-optional\n"),
                () => Failed(Patterns.Read(Path.Combine(scratch, "patterns"))))),
        new("a row of the CDC ledger whose table is named in one part", "file.ledger-row", true, (_, planted) =>
            Failed(CdcLedger.Parse("| dev | " + planted + " | 2026-10-21 |\n"))),
        new("a CDC ledger this identity cannot read", "file.unreadable", false, (scratch, _) =>
            Denied(Written(scratch, CdcLedger.File, "| Environment | Table | Measured |\n"), () => Failed(CdcLedger.Read(scratch)))),
        new("an aggregate query of check cdc SQL Server refused, its message planted", "cdc.unread", true, (_, planted) =>
        {
            var asked = Expect.Value(CdcCheck.Queries([Table.Name])).Single();
            return Failed(CdcCheck.Tracked(asked, new SqlServer.Measurement.Raised(asked.Query.Site, 229, planted)));
        }),

        new("DBCHANGE_TOOL naming no tool folder", "tool.missing", false, (scratch, _) =>
            Failed(Ssdt.Tool(Bare(scratch), Bare(scratch), scratch))),
        new("no tool folder anywhere", "tool.missing", false, (scratch, _) => Failed(Ssdt.Tool(Bare(scratch), null, scratch))),
        new("a build against no tool folder", "tool.missing", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), Sdk))),
        new("a build of no project", "build.no-project", false, (scratch, _) =>
            Failed(Ssdt.Build(Path.Combine(scratch, "none.sqlproj"), Bare(scratch), Output(scratch), Sdk))),
        new("a repository holding two projects", "build.no-project", false, (scratch, _) =>
        {
            Written(scratch, "a/One.sqlproj", "<Project />");
            Written(scratch, "b/Two.sqlproj", "<Project />");
            return Failed(Ssdt.Project(scratch, null));
        }),
        new("a build without the SDK band", "sdk.missing", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), (_, _) => new Ran.Exited(0, "8.0.100 [sdk]\n", "")))),
        new("a build with no dotnet on the PATH", "sdk.missing", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Bare(scratch), Output(scratch), (c, _) =>
            new Ran.NotFound(c.Program, "'dotnet' is on no folder of the PATH.")))),
        new("a build that fails", "build.failed", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Hollow(scratch), Output(scratch), Sdk))),
        new("a build past its timeout", "build.timed-out", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Hollow(scratch), Output(scratch),
            (c, t) => c.Arguments[0] == "build" ? new Ran.TimedOut(c.Timeout, "  Determining projects to restore...\n", "") : Sdk(c, t)))),
        new("a build that succeeds and writes no package", "build.no-package", false, (scratch, _) =>
            Failed(Ssdt.Build(Project(scratch), Hollow(scratch), Output(scratch),
            (c, t) => c.Arguments[0] == "build" ? new Ran.Exited(0, "  Build succeeded.\n", "") : Sdk(c, t)))),
        new("a tool folder whose build task carries no file version", "toolchain.targets-mismatch", false, (scratch, _) =>
        {
            var tool = Hollow(scratch);
            File.WriteAllBytes(Path.Combine(tool, Ssdt.BuildTargets.Task), []);
            return Failed(Ssdt.Build(Project(scratch), tool, Output(scratch), Sdk));
        }),
        new("a global.json that is not JSON", "sdk.global-json", true, (scratch, planted) =>
            Failed(Doctor.Pinned(
            Path.GetDirectoryName(Written(scratch, "global.json", "{ \"sdk\": { \"version\": " + planted + " } }"))!))),
        new("a toolchain ledger this identity cannot read", "toolchain.unreadable", false, (scratch, _) =>
            Denied(Path.Combine(Ledger(scratch, "| 2026-09-24 | 3.0.0 | UNPINNED | — |"), Doctor.Ledger), () =>
                Failed(Doctor.Toolchain(scratch, "3.0.0")))),
        new("a package that is none", "package.unreadable", false, (scratch, _) =>
            Failed(Ssdt.Open(Written(scratch, "not.dacpac", "not a package")))),
        new("no package where one is named", "package.unreadable", false, (scratch, _) =>
            Failed(Ssdt.Open(Path.Combine(scratch, "none.dacpac")))),
        new("a package holding no model.xml", "package.unreadable", false, (scratch, _) =>
            Failed(Ssdt.Open(Zipped(scratch, "hollow.dacpac", ("Origin.xml", "<DacOrigin />"))))),
        new("a package whose refactorlog holds an operation without its Key", "refactorlog.unreadable", false, (scratch, _) =>
            Failed(Ssdt.Open(Packaged(scratch, "keyless.dacpac",
            ("refactor.xml", "<Operations xmlns=\"http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02\"><Operation "
                + "Name=\"Rename Refactor\"><Property Name=\"ElementName\" Value=\"[dbo].[T]\" /></Operation></Operations>"))))),
        new("a refactorlog that is none", "refactorlog.unreadable", false, (scratch, _) =>
            Failed(Ssdt.RefactorLog(Written(scratch, "not.refactorlog", "not a refactorlog")))),
        new("a refactorlog carrying a DOCTYPE with an external entity", "refactorlog.unreadable", true, (scratch, planted) =>
            Failed(Ssdt.RefactorLog(Written(scratch, "entity.refactorlog",
            "<?xml version=\"1.0\"?><!DOCTYPE Operations [<!ENTITY secret SYSTEM \"file:///"
                + Written(scratch, "secret.txt", planted).Replace('\\', '/') + "\">]><Operations>&secret;</Operations>")))),
        new("a package whose refactor.xml is no refactorlog", "refactorlog.unreadable", false, (scratch, _) =>
        {
            var dacpac = Path.Combine(scratch, "refactored.dacpac");
            using (var model = Model("CREATE TABLE dbo.Customer (Id INT NOT NULL);"))
            {
                DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
            }

            using (var zip = System.IO.Compression.ZipFile.Open(dacpac, System.IO.Compression.ZipArchiveMode.Update))
            using (var log = new StreamWriter(zip.CreateEntry("refactor.xml").Open()))
            {
                log.Write("<Operations xmlns=\"http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02\"><Operation Key=\"k\" "
                    + "/></Operations>");
            }

            return Failed(Ssdt.Open(dacpac));
        }),
        new("a refactorlog entry naming no object", "refactorlog.name", false, (_, _) =>
        {
            using var model = Model();
            return Failed(Ssdt.ReadModel(model, null, null,
                [new Ssdt.RefactorLogOperation("0a1b2c3d-0000-4000-8000-000000000001", "Rename Refactor", null, "[dbo].[Customer",
                    "SqlTable", null, null, "[Client]", null)]));
        }),
        new("a platform DacFx names in other than letters and digits", "model.platform", false, (_, _) => Failed(Platform.Of("Sql 160"))),
        new("two objects of a model keyed alike", "model.duplicate-key", false, (_, _) =>
        {
            using var model = Model("CREATE TABLE dbo.Customer (Id INT NOT NULL);", "CREATE TABLE dbo.Customer (Id INT NOT NULL);");
            return Failed(Ssdt.ReadModel(model));
        }),

        new("a file standing where a written file's folder should be", "file.unwritable", false, (scratch, _) =>
            Failed(Write.Text(Path.Combine(Written(scratch, "file", ""), "under.txt"), "x\n"))),
        new("a lock file another dbchange process holds past the wait", "lock.timed-out", false, (scratch, _) =>
        {
            using var held = Made(FileLock.Take(Path.Combine(scratch, "state.lock"), TimeSpan.Zero));
            return Failed(FileLock.Take(Path.Combine(scratch, "state.lock"), TimeSpan.Zero));
        }),
        // Windows share modes ignore the switch, so the error is taken from its maker
        new("a lock on Linux or macOS with the runtime's file locking off", "lock.unsupported", false, (_, _) => FileLock.Unsupported),

        new("a git program that does not run", "git.missing", false, (scratch, _) =>
            Failed(Git.At(scratch, "HEAD", (c, _) => new Ran.NotFound(c.Program, "'git' is on no folder of the PATH (C:\\Windows).")))),
        new("a git command past its timeout", "git.timed-out", false, (scratch, _) =>
            Failed(Git.At(scratch, "HEAD", (c, _) => new Ran.TimedOut(c.Timeout, "", "")))),
        new("a repository git refuses for its owner", "git.dubious-ownership", false, (scratch, _) =>
            Failed(Git.At(scratch, "HEAD", (_, _) =>
                new Ran.Exited(128, "", "fatal: detected dubious ownership in repository at '" + scratch.Replace('\\', '/') + "'\n")))),
        new("a folder in no repository", "git.not-a-repository", false, (scratch, _) =>
            Failed(Git.ChangedPaths(Path.Combine(scratch, "no-repository"), "HEAD~1", "HEAD"))),
        new("a ref that names no commit", "ref.unresolved", false, (scratch, _) =>
            InRepository(scratch, root => Git.At(root, "no-such-tag"))),
        new("two refs whose histories never meet", "ref.unrelated", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "switch", "-q", "--orphan", "unrelated");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "unrelated");
            return Git.MergeBase(root, "main", "unrelated");
        })),
        new("two refs whose fork a shallow clone cannot see", "git.shallow-clone", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "switch", "-q", "--orphan", "unrelated");
            Arrange(root, "commit", "-q", "--allow-empty", "-m", "unrelated");
            return Git.MergeBase(root, "main", "unrelated", (c, t) => c.Arguments.Contains("--is-shallow-repository")
                ? new Ran.Exited(0, "true\n", "") : Command.Run(c, t));
        })),
        new("a branch name git does not take", "git-branch.malformed", false, (scratch, _) =>
            InRepository(scratch, root => Git.CommitAndPush(root, Evidence, "evidence", "dbchange/..evidence"))),
        new("a branch that exists here", "git-branch.exists", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "branch", "dbchange/evidence");
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence");
        })),
        new("a repository with no origin", "git.no-origin", false, (scratch, _) =>
            InRepository(scratch, root => Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence"))),
        new("an origin that does not answer", "origin.unreachable", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "no-origin.git"));
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence");
        })),
        new("an origin that refuses the credential", "origin.denied", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "init", "-q", "--bare", Path.Combine(scratch, "origin.git"));
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "origin.git"));
            Written(root, "dbchange/evidence.shape.json", "{}\n");
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence",
                (c, t) => c.Arguments.Contains("push")
                    ? new Ran.Exited(128, "", "fatal: Authentication failed for 'https://dev.azure.com/org/_git/ssdt/'\n")
                    : Command.Run(c, t));
        })),
        new("an origin whose hook refuses the branch", "origin.rejected", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "init", "-q", "--bare", Path.Combine(scratch, "origin.git"));
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "origin.git"));
            Written(root, "dbchange/evidence.shape.json", "{}\n");
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence",
                (c, t) => c.Arguments.Contains("push")
                    ? new Ran.Exited(1, "To origin\n!\tHEAD:refs/heads/dbchange/evidence\t"
                        + "[remote rejected] (pre-receive hook declined)\nDone\n", "")
                    : Command.Run(c, t));
        })),
        new("a commit on a machine with no git identity", "git.no-identity", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "init", "-q", "--bare", Path.Combine(scratch, "origin.git"));
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "origin.git"));
            Written(root, "dbchange/evidence.shape.json", "{}\n");
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence",
                (c, t) => c.Arguments.Contains("commit-tree")
                    ? new Ran.Exited(128, "", "Author identity unknown\n\n*** Please tell me who you are.\n") : Command.Run(c, t));
        })),
        new("a commit of a path the working tree lacks", "git.failed", false, (scratch, _) => InRepository(scratch, root =>
        {
            Arrange(root, "init", "-q", "--bare", Path.Combine(scratch, "origin.git"));
            Arrange(root, "remote", "add", "origin", Path.Combine(scratch, "origin.git"));
            return Git.CommitAndPush(root, Evidence, "evidence", "dbchange/evidence");
        })),

        new("no environments file", "environments.missing", false, (scratch, _) => Failed(Io.EnvironmentsFile.Read(scratch))),
        new("an environments file that is not JSON", "environments.unreadable", true, (scratch, planted) =>
            Failed(Io.EnvironmentsFile.Read(RepositoryAt(scratch, "{ \"environments\": { \"dev\": " + planted + " } }")))),
        new("an environments file giving a key twice", "environments.unreadable", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"readerGroups\": [" + Quoted(planted) + "], \"readerGroups\": []"))),
        new("a literal connection string", "environments.literal-connection", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev(connection: "Server=db;User ID=estate;Password=" + planted))),
        new("a literal connection string as a key", "environments.literal-connection", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"sqlcmd\": { \"Data Source=db;Password=" + planted + "\": \"env:A\" }"))),
        new("an unknown key", "environments.unknown-key", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"password\": " + Quoted(planted)))),
        new("an unknown key beside a SQLCMD literal", "environments.unknown-key", true, (scratch, planted) =>
            ReadEnvironments(scratch,
                Dev("\"sqlcmd\": { \"Tag\": { \"literal\": \"dev\", \"sensitive\": false, \"secret\": " + Quoted(planted) + " } }"))),
        new("a value of the wrong JSON kind", "environments.malformed", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"readerGroups\": " + Quoted(planted)))),
        new("an environment with no connection", "environments.malformed", true, (scratch, planted) =>
            ReadEnvironments(scratch, "\"dev\": { \"profile\": \"" + Pipeline + "\", \"readerGroups\": [" + Quoted(planted) + "] }")),
        new("a SQLCMD literal not marked non-sensitive", "environments.unmarked-literal", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"sqlcmd\": { \"Tag\": { \"literal\": " + Quoted(planted) + " } }"))),
        new("a SQLCMD value that is a bare literal", "environments.unmarked-literal", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"sqlcmd\": { \"Tag\": " + Quoted(planted) + " }"))),
        new("a connection that is no reference", "reference.malformed", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev(connection: "env:" + planted))),
        new("a file reference that is a connection string", "reference.malformed", true, (_, planted) =>
            Failed(SecretReference.Of("--connection", "file:Server=db;User ID=sa;Password=" + planted))),
        new("a host given with its port", "environments.host", true, (_, planted) =>
            Failed(Host.Of("environments.dev.host in dbchange/environments.json", planted + ",1433"))),
        new("an environment misnamed", "environments.environment-name", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"readerGroups\": [" + Quoted(planted) + "]", name: "DEV"))),
        new("a reader group given twice", "environments.reader-groups", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"readerGroups\": [" + Quoted(planted) + ", " + Quoted(planted) + "]"))),
        new("a profile path outside the repository", "environments.profile-path", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev(profile: "../" + planted + ".publish.xml"))),
        new("a SQLCMD variable given twice in two cases", "environments.sqlcmd-repeated", true, (scratch, planted) =>
            ReadEnvironments(scratch,
                Dev("\"sqlcmd\": { \"Tag\": \"env:A\", \"Version\": \"env:C\", \"tag\": \"env:B\" }, \"readerGroups\": [" + Quoted(planted)
                + "]"))),
        new("a classification that is none", "environments.classification", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"classification\": " + Quoted(planted)))),
        new("a synthetic environment unconfirmed", "environments.unconfirmed", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"classification\": \"synthetic\", \"readerGroups\": [" + Quoted(planted) + "]"))),
        new("a confirmation with no date", "environments.confirmation", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"classification\": \"synthetic\", \"confirmedBy\": " + Quoted(planted)))),
        new("a SQLCMD variable misnamed", "sqlcmd.name", true, (scratch, planted) =>
            ReadEnvironments(scratch, Dev("\"sqlcmd\": { \"Tag Name\": \"env:A\" }, \"readerGroups\": [" + Quoted(planted) + "]"))),
        new("a SQLCMD literal under a credential's name in the environments file", "sqlcmd.literal-credential", true, (scratch, planted) =>
            ReadEnvironments(scratch,
                Dev("\"sqlcmd\": { \"ServicePassword\": { \"literal\": " + Quoted(planted) + ", \"sensitive\": false } }"))),
        new("a script using a variable with no value", "sqlcmd.undefined", true, (_, planted) =>
            Failed(SqlCmdVariable.Substitute("PRINT '$(Missing)';",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Tag"] = planted }))),

        new("no profile", "profile.missing", false, (scratch, _) =>
            Failed(PublishProfiles.Load(Path.Combine(scratch, "none.publish.xml")))),
        new("a profile carrying a DOCTYPE with an external entity", "profile.unreadable", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Written(scratch, "entity.publish.xml",
            "<?xml version=\"1.0\"?><!DOCTYPE Project [<!ENTITY secret SYSTEM \"file:///"
                + Written(scratch, "secret.txt", planted).Replace('\\', '/') + "\">]><Project>&secret;</Project>")))),
        new("a profile that is not XML", "profile.unreadable", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Written(scratch, "broken.publish.xml", "<Project>" + planted + "</Projec>")))),
        new("a profile DacFx does not read", "profile.unreadable", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>" + planted + "</BlockOnPossibleDataLoss>")))),
        new("a profile holding a password", "profile.password", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(
                Profile(scratch, "<TargetConnectionString>Data Source=db;User ID=sa;Password=" + planted + "</TargetConnectionString>")))),
        new("a profile holding a password a comment splits", "profile.password", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Profile(scratch, "", ("LinkedServer", "Server=db;User ID=sa;Pass<!-- -->word=" + planted))))),
        new("a profile giving a SQLCMD value that is a connection string", "profile.literal-connection", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(
                Profile(scratch, "", ("LinkedServer", "Data Source=" + planted + ";Initial Catalog=Orders;Integrated Security=True"))))),
        new("a profile that allows data loss", "profile.data-loss-allowed", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted))))),
        new("a named environment whose profile allows data loss", "profile.data-loss-allowed", true, (scratch, planted) =>
        {
            var root = RepositoryAt(scratch, Environments(Dev(profile: "dbchange/profiles/relaxed.publish.xml")));
            File.Move(Profile(scratch, "<BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>", ("Tag", planted)),
                Path.Combine(root, "dbchange", "profiles", "relaxed.publish.xml"));
            return Failed(PublishProfiles.Of(Made(Io.EnvironmentsFile.Read(root)).All.Single(), root));
        }),
        new("a SQLCMD literal under a credential's name in a profile", "sqlcmd.literal-credential", true, (scratch, planted) =>
            Failed(PublishProfiles.Load(Profile(scratch, "", ("ApiToken", planted))))),

        new("a target of no form the grammar knows", "target.unknown", true, (_, planted) =>
            Failed(SqlServer.Target("sql:" + planted, "--target"))),
        new("a copy named as no copy can be", "copy.unregistered", true, (_, planted) =>
            Failed(SqlServer.Target("copy:" + planted, "--target"))),
        new("a git ref that is none", "ref.malformed", true, (_, planted) => Failed(GitRef.Of("--at", "-" + planted))),
        new("a literal connection string where a target goes", "connection.literal", true, (_, planted) =>
            Failed(SqlServer.Target("Server=db;User ID=sa;Password=" + planted, "--target"))),
        new("a git ref where a database is asked for", "target.not-a-database", false, (scratch, _) =>
            Failed(SqlServer.Resolve(Target("ref:main"), scratch))),
        new("an environment the environments file does not name", "target.unnamed", false, (scratch, _) =>
            Failed(SqlServer.Resolve(Target("env:qa"), RepositoryAt(scratch, Environments(Dev()))))),
        new("synthetic-copy where the registry holds no synthetic copy built to its end", "copy.synthetic-absent", false, (scratch, _) =>
        {
            var root = RepositoryAt(scratch, Environments(""));
            return Failed(LocalServer.Synthetic(root, Io.EnvironmentsFile.Read(root),
                () => "Server=tcp:127.0.0.1,11433;Integrated Security=true", _ => []));
        }),
        new("the synthetic copy's database another checkout made", "copy.foreign", false, (_, _) =>
            LocalServer.Foreign(CopyName.Synthetic(Fingerprint.Of("a base"), null), ServerName.Of("localhost,11433", "host"))),
        new("a constraint read back in another state than the copy restored", "synthetic-copy.trust", false, (_, _) =>
            SyntheticCopy.Restoring(new TrustState(Made(ElementKey.Of("ForeignKeyConstraint", Made(Name.Of("dbo", "FK_T")))), true, true),
                null) ?? throw new InvalidOperationException("a constraint read back absent was taken as restored")),
        new("an existing data file that cannot be read", "existing-data.unreadable", false, (scratch, _) =>
            Failed(ExistingDataFile.Read(Path.Combine(scratch, "absent.existing-data.json")))),
        new("an existing data file holding a key its schema does not name", "existing-data.malformed", true, (scratch, planted) =>
            Failed(ExistingDataFile.Read(Written(scratch, "existing-data.json",
                "{ \"schema\": \"dbchange.existing-data/1\", \"states\": [], " + Quoted(planted) + ": true }")))),
        new("a declared witness whose header names no table", "witness.header", false, (_, _) =>
            Failed(Witnesses.Parse("-- plants: [a].[b].[c]\nINSERT INTO dbo.T (C) VALUES (1);", EmptyModel))),
        new("a declared witness's statement before any header", "witness.outside-block", false, (_, _) =>
            Failed(Witnesses.Parse("DELETE FROM dbo.T;\n-- plants: [dbo].[T]\n", EmptyModel))),
        new("a declared witness that does not parse", "witness.unparsed", false, (_, _) =>
            Failed(Witnesses.Parse("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C VALUES (1);", EmptyModel))),
        new("a declared witness of two batches", "witness.batch", false, (_, _) =>
            Failed(Witnesses.Parse("-- plants: [dbo].[T]\nDELETE FROM dbo.T;\nGO\nDELETE FROM dbo.T;", EmptyModel))),
        new("a declared witness that changes the schema", "witness.not-dml", false, (_, _) =>
            Failed(Witnesses.Parse("-- plants: [dbo].[T]\nDROP TABLE dbo.T;", EmptyModel))),
        new("a declared witness carrying a literal no repository defines", "witness.not-synthetic", true, (_, planted) =>
            Failed(Witnesses.Parse("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) VALUES (N'" + planted + "');", EmptyModel))),
        new("a declared witness SQL Server refuses", "witness.failed", false, (_, _) =>
            Witnesses.Failed(Made(Witnesses.Parse("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) VALUES (NULL);", EmptyModel))[0], 1, 515,
                "Cannot insert the value NULL into column 'C', table 'copy.dbo.T'; column does not allow nulls. INSERT fails.")),
        new("a connection whose variable is unset", "connection.unresolved", false, (scratch, _) =>
            Failed(SqlServer.Resolve(Target("env:dev"),
                RepositoryAt(scratch,
                Environments(Dev(connection: "env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant())))))),
        new("a connection file holding no connection string", "connection.malformed", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("env:dev"),
                Initialized(RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "dev.connection", "garbled " + planted)))))))),
        new("a connection file git tracks", "reference.tracked", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "dbchange/environments.json", Environments(Dev(connection: "file:dbchange/dev.connection")));
            OwnerOnly(Written(root, "dbchange/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            Arrange(root, "add", "--", "dbchange/dev.connection");
            Arrange(root, "commit", "-q", "-m", "the connection file");
            return SqlServer.Resolve(Target("env:dev"), root);
        })),
        new("a connection file git does not ignore", "reference.not-ignored", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "dbchange/environments.json", Environments(Dev(connection: "file:dbchange/dev.connection")));
            OwnerOnly(Written(root, "dbchange/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            return SqlServer.Resolve(Target("env:dev"), root);
        })),
        new("a connection file named by a spelling its folder does not list", "reference.unlisted", OperatingSystem.IsWindows(),
            (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, ".gitignore", ".dbchange/\n");
            Written(root, "dbchange/environments.json", Environments(Dev(connection: "file:.dbchange/dev.connection::$DATA")));
            var file = OwnerOnly(
                Written(root, ".dbchange/dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted));
            // Windows opens the default data stream as name::$DATA; elsewhere that name opens no file, and the driver asks io/SqlServer of
            // it directly
            return OperatingSystem.IsWindows()
                ? SqlServer.Resolve(Target("env:dev"), root).Map(database => database.Target.ToString())
                : SqlServer.Listed("env:dev's connection, file:.dbchange/dev.connection::$DATA,", file + "::$DATA");
        })),
        new("a connection file in a folder this identity cannot list", "reference.unlistable", true, (scratch, planted) =>
        {
            var root = Initialized(
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "locked/dev.connection",
                "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Denied(Path.Combine(scratch, "locked"), () => Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file this identity cannot read", "reference.unreadable", true, (scratch, planted) =>
        {
            var root = Initialized(
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "dev.connection",
                "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Denied(Path.Combine(scratch, "dev.connection"), () => Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file whose attributes this identity cannot read", "reference.inaccessible", true, (scratch, planted) =>
        {
            var root = Initialized(
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "locked/dev.connection",
                "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password=" + planted)))));
            return Unexaminable(Path.Combine(scratch, "locked", "dev.connection"), () =>
                Failed(SqlServer.Resolve(Target("env:dev"), root)));
        }),
        new("a connection file of a repository root in no git repository", "reference.no-repository", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("env:dev"),
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Password=" + planted))))))),
        new("a connection file its group can read, where files carry a Unix mode", "reference.readable-by-others",
            !OperatingSystem.IsWindows(), (scratch, planted) => OperatingSystem.IsWindows()
            ? SqlServer.ReadableByOthers("env:dev's connection, file:.dbchange/dev.connection,", (UnixFileMode)0b110_100_000)
                ?? throw new InvalidOperationException("mode 0640 was not refused")
            : Failed(SqlServer.Resolve(Target("env:dev"), Initialized(RepositoryAt(scratch, Environments(Dev(connection:
                GroupReadable(Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev;User ID=reader;Password="
                    + planted))))))))),
        new("a copy the registry does not hold", "copy.unregistered", false, (scratch, _) =>
            Failed(SqlServer.Resolve(Target("copy:dbchange_nowhere_1_00000000"), scratch))),
        new("a copy registry that is not JSON", "registry.unreadable", true, (scratch, planted) =>
        {
            Directory.CreateDirectory(Path.Combine(scratch, ".dbchange"));
            File.WriteAllText(Path.Combine(scratch, ".dbchange", "copies.json"), "{ \"copies\": [ " + planted);
            return Failed(SqlServer.Resolve(Target("copy:dbchange_nowhere_1_00000000"), scratch));
        }),
        new("a copy made on the host an environment's reference names", "copy.named-host", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("copy:" + Copied),
            Registered(Initialized(
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=127.0.0.1,1433;User ID=reader;Password="
                + planted), host: "localhost")))))))),
        new("a local server holding a linked server to the host an environment runs on", "copy.named-host", true, (scratch, planted) =>
            Failed(LocalServer.Unlinked(Made(Io.EnvironmentsFile.Read(RepositoryAt(scratch, Environments(Dev())))),
                ServerName.Of("localhost,11434", Environment.MachineName),
                [new LocalServer.LinkedServer("prod-link", null, "Server=dev-sql;UID=reader;PWD=" + planted)], _ => []))),
        new("a copy beside an environment whose connection SqlClient cannot read", "connection.malformed", true, (scratch, planted) =>
            Failed(SqlServer.Resolve(Target("copy:" + Copied),
            Registered(Initialized(
                RepositoryAt(scratch,
                Environments(Dev(connection: Reference(scratch, "dev.connection", "Server=dev-sql;Nonsense " + planted + " = 1"))))))))),
        new("a backup of a copy on a server on another machine", "copy.uncached", false, (scratch, _) =>
            Failed(LocalServer.BackupFolderOf(scratch, ServerName.Of("build-sql.corp.example,1433", Environment.MachineName),
                _ => [System.Net.IPAddress.Parse("192.0.2.10")]))),
        new("a restore of a base the copy cache holds no backup of", "copy.uncached", false, (scratch, _) =>
            Failed(LocalServer.Restore(scratch, "Server=127.0.0.1,1;Integrated Security=true", Fingerprint.Of("a base"),
                SqlServer.QueryLog.Start(scratch)))),
        new("no local server anywhere", "local-server.missing", false, (scratch, _) =>
            Failed(LocalServer.ServerName(null, Path.Combine(scratch, "no-sql.env"), localDb: false))),
        new("an DBCHANGE_SQL SqlClient reads no connection string from", "connection.malformed", true, (scratch, planted) =>
            Failed(LocalServer.ServerName("Server=db;Password=" + planted + ";Nonsense " + planted + " = 1",
                Path.Combine(scratch, "no-sql.env"), localDb: false))),
        new("a named environment's login denied", "server.denied", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(18456, "Login failed for user '" + planted + "'.")),
        new("a named environment that does not answer", "server.unreachable", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(53, "A network-related or instance-specific error occurred while establishing a connection to "
                + planted + ".")),
        new("a named environment's statement running past its timeout", "server.timed-out", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(-2, "Execution Timeout Expired, the statement reading '" + planted + "'.", fatal: false,
                opened: true)),
        new("a named environment's statement failing", "server.failed", true, (scratch, planted) =>
            DevDatabase(scratch).ErrorOf(245, "Conversion failed when converting the nvarchar value '" + planted + "' to data type int.")),
        new("a SQL Server error DacFx quotes by its number, with no SqlException inside", "server.failed", true, (scratch, planted) =>
            DacFx.Failed(DevDatabase(scratch),
                new DacServicesException("Could not deploy package.",
                new InvalidOperationException("Error SQL72014: Core Microsoft SqlClient Data Provider: "
                + "Msg 2627, Level 14, State 1, Line 1 Violation of PRIMARY KEY constraint 'PK_Customer'. The duplicate key value is ("
                    + planted + ").")))),
        new("DacFx failing with no SQL Server error inside", "dacfx.failed", false, (scratch, _) =>
            DacFx.Failed(DevDatabase(scratch),
                new DacServicesException("Could not save package to file.",
                new InvalidOperationException("Error SQL71501: [dbo].[V] has an unresolved reference to object [dbo].[Missing].")))),
        new("a package of a newer platform than its target's", "plan.platform", false, (scratch, _) =>
        {
            using var newer = Made(Ssdt.Open(Packaged(scratch, "newer", SqlServerVersion.Sql170)));
            using var older = Made(Ssdt.Open(Packaged(scratch, "older", SqlServerVersion.Sql160)));
            return Failed(DacFx.Plan(newer, older, "Target", Made(PublishProfiles.Load(GoldenProject.Profile)), []));
        }),
        new("a case-insensitive package planned against a case-sensitive one", "plan.collation", false, (scratch, _) =>
        {
            using var insensitive =
                Made(Ssdt.Open(Packaged(scratch, "insensitive", SqlServerVersion.Sql160, "SQL_Latin1_General_CP1_CI_AS")));
            using var sensitive = Made(Ssdt.Open(Packaged(scratch, "sensitive", SqlServerVersion.Sql160, "Latin1_General_CS_AS")));
            return Failed(DacFx.Plan(insensitive, sensitive, "Target", Made(PublishProfiles.Load(GoldenProject.Profile)), []));
        }),
        new("a deploy report of another shape", "plan.report-unread", false, (_, _) =>
            Failed(DacFx.Report("<DeploymentReport xmlns=\"http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02\"><Warnings "
                + "/></DeploymentReport>", [], []))),
        new("an aggregate query the allowlist refuses", "aggregate-query.refused", true, (_, planted) =>
            Failed(SqlServer.AggregateQuery.Of("SELECT MAX(Email) FROM dbo.Customer WHERE Name = N'" + planted + "';",
                "dbo.Customer.Email Fits"))),
        new("a plan's script TSql160Parser does not read", "plan.unparsed", true, (_, planted) =>
            Failed(BlockOnPossibleDataLoss.Read("INSERT INTO [dbo].[Customer] VALUES (N'" + planted + "' ,,);\nGO\n", []))),
        new("a plan's script that does not hold the change's pre-deploy script verbatim", "plan.deploy-script", true, (_, planted) =>
            Failed(PlanStatements.Read("PRINT N'Update complete.';\nGO\n", [], new("PRINT N'" + planted + "';", null), new(null, null)))),
        new("a BlockOnPossibleDataLoss check that reads no table", "plan.check-table", true, (_, planted) =>
            Failed(BlockOnPossibleDataLoss.Read("IF EXISTS (SELECT TOP 1 1)\n    RAISERROR (N'" + planted + "', 16, 127) WITH NOWAIT\n", []))),
        new("a BlockOnPossibleDataLoss check that reads a SQLCMD value marked sensitive", "sqlcmd.sensitive", true, (_, planted) =>
            Failed(BlockOnPossibleDataLoss.Read("IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer] WHERE [Token] = N'$(ServiceToken)')\n"
                + "    RAISERROR (N'Rows were detected.', 16, 127) WITH NOWAIT\n",
                [new(Made(SqlCmdName.Of("test", "ServiceToken")), planted, true)]))),
        new("a SQLCMD reference that does not resolve", "sqlcmd.unresolved", false, (scratch, _) =>
        {
            var root = Initialized(
                RepositoryAt(scratch,
                Environments(Dev("\"sqlcmd\": { \"ServiceToken\": \"env:DBCHANGE_UNSET_"
                + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "\" }",
                connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev")))));
            File.Copy(GoldenProject.Profile, Path.Combine(root, "dbchange", "profiles", "pipeline.publish.xml"));
            return Failed(SqlServer.SqlCmdValues(Made(SqlServer.Resolve(Target("env:dev"), root))));
        }),
        new("a SQLCMD reference to a file git tracks", "reference.tracked", true, (scratch, planted) => InRepository(scratch, root =>
        {
            Written(root, "dbchange/environments.json", Environments(Dev("\"sqlcmd\": { \"ServiceToken\": \"file:dbchange/token.txt\" }",
                connection: Reference(scratch, "dev.connection", "Server=dev-sql;Initial Catalog=Dev"))));
            OwnerOnly(Written(root, "dbchange/token.txt", planted));
            Arrange(root, "add", "--", "dbchange/token.txt");
            Arrange(root, "commit", "-q", "-m", "the token");
            Directory.CreateDirectory(Path.Combine(root, "dbchange", "profiles"));
            File.Copy(GoldenProject.Profile, Path.Combine(root, "dbchange", "profiles", "pipeline.publish.xml"));
            return SqlServer.SqlCmdValues(Made(SqlServer.Resolve(Target("env:dev"), root)));
        })),

        new("a timeout that is no whole number of seconds", "arguments.timeout", false, (_, _) =>
            Failed(Cli.Program.Timeout(["read", "--timeout", "soon"]))),
        new("a flag the verb does not take", "arguments.unknown-flag", false, (_, _) =>
            Failed(Contract.Flags(["--no-such-flag"], [], [], []))),
        new("a required flag absent", "arguments.missing-flag", false, (_, _) => Failed(Contract.Flags([], ["--from"], [], []))),
        new("dbchange check with no check named", "arguments.unknown-check", false, (scratch, _) =>
            Carried(Verbs.Check(new Checkout(scratch, scratch, null, Cli.Contract.Version), SqlServer.QueryLog.Start(scratch), []))),
        new("dbchange synthetic-copy with nothing named to do", "arguments.unknown-subcommand", false, (scratch, _) =>
            Carried(Verbs.SyntheticCopy(new Checkout(scratch, scratch, null, Cli.Contract.Version), SqlServer.QueryLog.Start(scratch),
                []))),
        new("a word that names no verb", "arguments.unknown-verb", false, (scratch, _) =>
            Answered(["frobnicate"], new Checkout(scratch, scratch, null, Cli.Contract.Version))),
        new("a verb this build has no body for", "verb.not-built", false, (scratch, _) =>
            Answered(["predict"], new Checkout(scratch, scratch, null, Cli.Contract.Version))),
        new("an aggregate query answered with a type the allowlist never yields", "internal.answer-type", true, (_, planted) =>
            Failed(SqlServer.Integer(planted))),
        new("a working directory that no longer exists", "file.no-working-directory", false, (_, _) =>
            Failed(Checkout.Here(Cli.Contract.Version, () => throw new DirectoryNotFoundException("Could not find a part of the path.")))),
        new("an exception no verb expected", "internal.unexpected", false, (scratch, _) =>
            Answered(["read", "--from", "dacpac:none.dacpac"], new Checkout(scratch, null!, null, Cli.Contract.Version))),
    ];

    /// <summary>
    /// Each error code the kernel, io and the cli construct, as their sources write it: a literal code, or the literal start of a composed
    /// one (element.).
    /// </summary>
    public static IEnumerable<string> InTheSources() => ConstructedIn().Select(c => c.Code).Distinct().Order(StringComparer.Ordinal);

    /// <summary>
    /// Each source file of the kernel, io and the cli with each code, or literal start of a composed code, it constructs an Error of.
    /// </summary>
    public static IEnumerable<(string File, string Code)> ConstructedIn() => Repository.Files
        .Where(f => (f.StartsWith("kernel/", StringComparison.Ordinal) || f.StartsWith("io/", StringComparison.Ordinal)
            || f.StartsWith("cli/", StringComparison.Ordinal))
            && f.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(f => CodesIn(Repository.Read(f)).Select(code => (File: f, Code: code)))
        .Distinct();

    /// <summary>
    /// The codes a source text constructs an Error of: the first argument of <c>new Error("…"</c>, or of a target-typed <c>new("…"</c>
    /// (finding D6) when it is a code, a word and a dot at least; a composed code's literal start (element.) counts as the start alone.
    /// </summary>
    public static IEnumerable<string> CodesIn(string source) =>
        System.Text.RegularExpressions.Regex.Matches(source, @"new(?:\s+Error)?\s*\(\s*""([a-z0-9-]+\.[a-z0-9.-]*)""")
            .Select(m => m.Groups[1].Value).Where(code => !code.Contains('/', StringComparison.Ordinal));
}
