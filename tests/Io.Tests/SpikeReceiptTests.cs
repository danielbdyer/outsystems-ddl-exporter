using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// Spikes S9, S10 and S11 of V3_MILESTONES.md §6, each answered where its fact can be measured, on DacFx 170.5.96 in this process and on
/// 162.5.57 through tests/DacFx162/ (the partial class beside this file): the receipts the Facsimile's §11 questions 1, 9 and 10 wait for
/// (tests/Cases/ARCHITECTURE.md), and the receipts of the verify cases of tests/Cases/ they settle. S9: a
/// schema read keeps a disabled check or foreign key as the public property Disabled and an untrusted one only as the internal annotation
/// IsNotTrustedPropertyAnnotation, a project cannot declare a key untrusted, no plan of the repository touches an untrusted key, and every
/// plan drops a disabled key the project does not declare and adds one it declares again WITH CHECK, so WP 3.5's trust step reads trust
/// from the catalog. S10: constraint validation runs after the commit and after the whole post-deploy script. S11: SQL Server names an unnamed
/// constraint after its object id, so the names differ between a copy and an environment with history, and the head plans against the two
/// agree once each name is replaced by its role. Every test makes its own databases on the fixture's SQL Server and drops them.
/// </summary>
public sealed partial class SpikeReceiptTests
{
    /// <summary>The key of the sample change add-a-foreign-key, over the seed's orphan (Order 4 to Customer 999), added as a failed deploy of
    /// that change leaves it: untrusted.</summary>
    private const string Untrusted =
        "ALTER TABLE dbo.[Order] WITH NOCHECK ADD CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id);";

    /// <summary>The check of the sample change add-a-check-constraint, added and then disabled.</summary>
    private const string Disabled =
        "ALTER TABLE dbo.Product ADD CONSTRAINT CK_Product_Code CHECK (LEN(Code) > 0); ALTER TABLE dbo.Product NOCHECK CONSTRAINT CK_Product_Code;";

    /// <summary>The key declare-what-the-planter-reads puts on dbo.Product.CategoryId, added and then disabled.</summary>
    private const string DisabledCategoryKey = "ALTER TABLE dbo.Product WITH NOCHECK ADD CONSTRAINT FK_Product_Category_CategoryId "
        + "FOREIGN KEY (CategoryId) REFERENCES dbo.Category (Id); ALTER TABLE dbo.Product NOCHECK CONSTRAINT FK_Product_Category_CategoryId;";

    private const string Key = "FK_Order_Customer_CustomerId";

    private const string CategoryKey = "FK_Product_Category_CategoryId";

    private const string Check = "CK_Product_Code";

    /// <summary>The name SQL Server gave the unnamed default of dbo.Note.Body.</summary>
    private const string BodyDefault = "SELECT name FROM sys.default_constraints "
        + "WHERE parent_object_id = OBJECT_ID(N'dbo.Note') AND COL_NAME(parent_object_id, parent_column_id) = N'Body';";

    private const string BodyDefaultIsSystemNamed = "SELECT CAST(is_system_named AS int) FROM sys.default_constraints "
        + "WHERE parent_object_id = OBJECT_ID(N'dbo.Note') AND COL_NAME(parent_object_id, parent_column_id) = N'Body';";

    private const string Orphans =
        "SELECT COUNT(*) FROM dbo.[Order] o WHERE NOT EXISTS (SELECT 1 FROM dbo.Customer c WHERE c.Id = o.CustomerId);";

    /// <summary>An orphan the seed does not fix: Order 5 to Customer 998, which no seed row adds.</summary>
    private const string SecondOrphan = "SET IDENTITY_INSERT dbo.[Order] ON; INSERT dbo.[Order] (Id, CustomerId, StatusId, Total, StatusText) "
        + "VALUES (5, 998, 1, 1.00, N'Pending'); SET IDENTITY_INSERT dbo.[Order] OFF;";

    /// <summary>A Product row whose category no seed row adds, written while <see cref="CategoryKey"/> is disabled.</summary>
    private const string CategoryOrphan = "INSERT dbo.Product (Code, CategoryId) VALUES (N'ORPHAN', 99);";

    /// <summary>The state <see cref="Trust"/> gives a constraint the database does not hold.</summary>
    private const int Absent = -1;

    /// <summary>The project with <see cref="CategoryKey"/> declared on dbo.Product.CategoryId, as declare-what-the-planter-reads declares
    /// it, and nothing else of that change.</summary>
    private static readonly SampleChange DeclaresTheCategoryKey = new("declare-the-category-key",
        [new GoldenEdit("Modules/Product.sql", "CategoryId INT           NULL,",
            "CategoryId INT           NULL CONSTRAINT " + CategoryKey + " REFERENCES dbo.Category (Id),")], []);

    private static readonly XNamespace Report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly XNamespace Serialization = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>The markers of the S10 head's plan, in the order the script must hold them: the key added WITH NOCHECK, the commit, the
    /// post-deploy script's header, the seed row that adds the parent, the post-deploy script's last line, and the validation.</summary>
    private static readonly string[] ValidationAfterTheSeed =
    [
        "WITH NOCHECK", "COMMIT TRANSACTION", "the POST-DEPLOY slot", "(999, N'Wayne", "Post-deploy complete",
        "Checking existing data against newly created constraints", "WITH CHECK CHECK CONSTRAINT [FK_Order_Customer_CustomerId]",
    ];

    /// <summary>The order the plan of a release that leaves a declared key unchanged holds against a database where the key is disabled:
    /// the key dropped, added again before the commit, the post-deploy script's last line, and the validation that trusts it.</summary>
    private static readonly string[] ReEnabledAfterTheSeed =
    [
        "DROP CONSTRAINT [" + CategoryKey + "]", "ADD CONSTRAINT [" + CategoryKey + "]", "COMMIT TRANSACTION", "Post-deploy complete",
        "WITH CHECK CHECK CONSTRAINT [" + CategoryKey + "]",
    ];

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_a_schema_read_keeps_a_disabled_check_and_an_untrusted_key_and_its_publish_reproduces_both()
    {
        await using var environment = await HoldingAnUntrustedKeyAndADisabledCheck();
        var reader = await ReadOnlyPrincipal.CreateAsync(environment);
        Assert.Equal((3, 1, 3), await Trusts(environment));

        using (var model = TSqlModel.LoadFromDatabase(reader.ConnectionString, new ModelExtractOptions()))
        {
            bool KeyDisabled(string name) => model.GetObject(ForeignKeyConstraint.TypeClass, new ObjectIdentifier("dbo", name),
                DacQueryScopes.UserDefined).GetProperty<bool>(ForeignKeyConstraint.Disabled);
            Assert.Equal((true, false, true), (
                model.GetObject(CheckConstraint.TypeClass, new ObjectIdentifier("dbo", Check), DacQueryScopes.UserDefined)
                    .GetProperty<bool>(CheckConstraint.Disabled), KeyDisabled(Key), KeyDisabled(CategoryKey)));
        }

        using var scratch = ScratchFolder.Temporary("spike-s9");
        var extracted = scratch.Under("environment.dacpac");
        new DacServices(reader.ConnectionString).Extract(extracted, environment.Name, "spike", new Version(1, 0), "", null, DacFx.Extraction);
        KeepsADisabledCheckAndAnUntrustedKey(extracted);
        await using var fresh = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(extracted, fresh, GoldenProject.Pipeline());
        Assert.Equal((3, 1, 3), await Trusts(fresh));

        // io's one element reader keeps Disabled and has nothing to keep of trust: the untrusted key reads as the trusted key the sample
        // change add-a-foreign-key builds.
        var read = await Read(environment);
        var head = (await GoldenProject.Model(GoldenProject.Change("add-a-foreign-key"))).Elements;
        Assert.Equal(new Value.Boolean(true), Single(read, "CheckConstraint [dbo].[" + Check + "]")["Disabled"]);
        Assert.Equal(new Value.Boolean(true), Single(read, "ForeignKeyConstraint [dbo].[" + CategoryKey + "]")["Disabled"]);
        Assert.Equal(Single(head, "ForeignKeyConstraint [dbo].[" + Key + "]").Properties,
            Single(read, "ForeignKeyConstraint [dbo].[" + Key + "]").Properties);
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_a_plan_sees_a_disabled_check_but_never_an_untrusted_key_and_no_publish_trusts_it_again()
    {
        await using var environment = await HoldingAnUntrustedKeyAndADisabledCheck();
        var reader = await ReadOnlyPrincipal.CreateAsync(environment);
        var head = await Head();

        // The head does not declare the disabled category key, and the plan drops it though the profile's DropObjectsNotInSource is False.
        var (script, report) = Plan(head, reader.ConnectionString, environment.Name);
        Assert.Equal(DisabledCheckAndUndeclaredKey, Operations(report).Order());
        Assert.Contains("WITH CHECK CHECK CONSTRAINT [CK_Product_Code]", script, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, script, StringComparison.Ordinal);

        using (var extracted = Value(DacFx.Extract(Copy(environment, await SqlServerFixture.ServerAsync()))))
        using (var source = Value(Ssdt.Open(head)))
        {
            var planned = Value(DacFx.Plan(source, extracted, environment.Name, Value(PublishProfiles.Load(GoldenProject.Profile)), []));
            Assert.Equal([("Create", "CheckConstraint [dbo].[CK_Product_Code]"), ("Drop", "CheckConstraint [dbo].[CK_Product_Code]"),
                    ("Drop", "ForeignKeyConstraint [dbo].[" + CategoryKey + "]")],
                planned.Report.Operations.Select(o => (o.Kind.Name, o.Key.ToString())).Order());
        }

        GoldenProject.Publish(head, environment, GoldenProject.Pipeline());
        Assert.Equal((0, 1, Absent), await Trusts(environment));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_a_release_that_leaves_a_declared_key_unchanged_adds_it_again_where_it_is_disabled_and_fails_Msg_547_on_an_orphan()
    {
        var (last, head) = (await GoldenProject.Built(DeclaresTheCategoryKey),
            await GoldenProject.Built(DeclaresTheCategoryKey, GoldenProject.Change("add-a-nullable-column")));
        await using var clean = await WithTheCategoryKeyDisabled(last, orphaned: false);
        await using var orphaned = await WithTheCategoryKeyDisabled(last, orphaned: true);

        var (script, report) = Plan(head, clean.ConnectionString, clean.Name);
        GoldenProject.Publish(head, clean, GoldenProject.Pipeline());
        var failed = Assert.Throws<DacServicesException>(() => GoldenProject.Publish(head, orphaned, GoldenProject.Pipeline()));

        Assert.Equal(TheCategoryKeyAddedAgain, Operations(report).Order());
        ReEnablesAfterTheSeed(script);
        Assert.Equal((1, 0), (await Nicknamed(clean), await Trust(clean.ConnectionString, CategoryKey)));
        Assert.Contains("Msg 547", string.Join('\n', failed.Messages.Select(m => m.ToString()).Prepend(failed.Message)), StringComparison.Ordinal);
        Assert.Equal((1, 1), (await Nicknamed(orphaned), await Trust(orphaned.ConnectionString, CategoryKey)));
    }

    [Fact]
    [Trait("Category", "build")]
    public void Spike_S9_a_project_cannot_declare_a_key_untrusted_its_build_warns_SQL70588_and_its_package_holds_the_key_trusted()
    {
        using var scratch = ScratchFolder.Temporary("spike-s9-build");
        var project = GoldenProject.CopyTo(scratch.Path, new GoldenEdit("Modules/OrderStatusText.sql",
            "-- Intentionally no schema object. The column lives in Modules/Order.sql.", Untrusted));
        var output = new StringBuilder();
        Ran Recorded(Command command, CancellationToken cancel)
        {
            var ran = Command.Run(command, cancel);
            output.Append(ran is Ran.Exited exited ? exited.Output + exited.Errors : "");
            return ran;
        }

        var built = Value(Ssdt.Build(project, new PublishedTool().Folder, scratch.Under("build"), Recorded));

        Assert.Contains("OrderStatusText.sql(30,1): Build warning SQL70588: WITH CHECK | NOCHECK option for existing data check enforcement "
            + "is ignored.", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ModelElement(built.Path, "[dbo].[" + Key + "]").Elements(Serialization + "Annotation"),
            a => (string?)a.Attribute("Type") == "IsNotTrustedPropertyAnnotation");
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S10_constraint_validation_runs_after_the_post_deploy_seed_so_a_parent_it_adds_satisfies_a_new_key()
    {
        await using var copy = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), copy, GoldenProject.Pipeline());
        Assert.Equal(1, await SqlServerFixture.ScalarAsync(copy.ConnectionString, Orphans));
        var head = await GoldenProject.Built(GoldenProject.Change("add-a-foreign-key-whose-parent-the-seed-adds"));

        ValidatesAfterTheSeed(Plan(head, copy.ConnectionString, copy.Name).Script);
        GoldenProject.Publish(head, copy, GoldenProject.Pipeline());
        Assert.Equal(0, await Trust(copy.ConnectionString, Key));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S10_an_orphan_the_seed_does_not_fix_fails_Msg_547_after_the_main_script_and_the_seed_have_landed()
    {
        await using var copy = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), copy, GoldenProject.Pipeline());
        await SqlServerFixture.ExecuteAsync(copy.ConnectionString, SecondOrphan);
        var head = await GoldenProject.Built(GoldenProject.Change("add-a-foreign-key-whose-parent-the-seed-adds"));

        var failed = Assert.Throws<DacServicesException>(() => GoldenProject.Publish(head, copy, GoldenProject.Pipeline()));

        Assert.Contains("Msg 547", string.Join('\n', failed.Messages.Select(m => m.ToString()).Prepend(failed.Message)), StringComparison.Ordinal);
        Assert.Equal((1, 1, 1), await AfterTheFailedValidation(copy));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S11_system_names_differ_between_the_copy_and_the_environment_and_the_head_plans_agree_but_for_them()
    {
        await using var environment = await WithHistory();
        await using var copy = await SqlServerFixture.RegisterAsync();
        var unnamed = await GoldenProject.Built(GoldenProject.Change("add-unnamed-constraints"));
        GoldenProject.Publish(unnamed, environment, GoldenProject.Pipeline());
        GoldenProject.Publish(unnamed, copy, GoldenProject.Pipeline());
        var head = await GoldenProject.Built(GoldenProject.Change("change-an-unnamed-default"));

        var named = await SystemNamesDiffer(environment, copy);
        var (onEnvironment, onCopy) = (Plan(head, environment.ConnectionString, environment.Name),
            Plan(head, copy.ConnectionString, copy.Name));

        Assert.Equal(Operations(onCopy.Report), Operations(onEnvironment.Report));
        Assert.Contains("DROP CONSTRAINT [" + named + "]", onEnvironment.Script, StringComparison.Ordinal);
        Assert.Equal(Normalized(onCopy.Script, copy.Name), Normalized(onEnvironment.Script, environment.Name));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S11_an_extract_of_the_environment_published_to_a_copy_keeps_none_of_its_system_names()
    {
        await using var environment = await WithHistory();
        GoldenProject.Publish(await GoldenProject.Built(GoldenProject.Change("add-unnamed-constraints")), environment, GoldenProject.Pipeline());
        var named = await SqlServerFixture.TextAsync(environment.ConnectionString, BodyDefault);
        using var scratch = ScratchFolder.Temporary("spike-s11");
        var extracted = scratch.Under("environment.dacpac");
        new DacServices(environment.ConnectionString)
            .Extract(extracted, environment.Name, "spike", new Version(1, 0), "", null, DacFx.Extraction);

        await using var fresh = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(extracted, fresh, GoldenProject.Pipeline());

        Assert.StartsWith("DF__Note__Body__", named, StringComparison.Ordinal);
        // The extract keeps the environment's name only as the annotation SqlInlineConstraintAnnotation of an element with no name.
        var body = XDocument.Parse(ModelXml(extracted)).Descendants(Serialization + "Element")
            .Single(e => (string?)e.Attribute("Type") == "SqlDefaultConstraint" && e.Elements(Serialization + "Relationship")
                .Any(r => (string?)r.Attribute("Name") == "ForColumn" && r.Descendants(Serialization + "References")
                    .Any(n => (string?)n.Attribute("Name") == "[dbo].[Note].[Body]")));
        Assert.Equal((null, "[dbo].[" + named + "]"), ((string?)body.Attribute("Name"), (string?)body.Elements(Serialization + "Annotation")
            .Single(a => (string?)a.Attribute("Type") == "SqlInlineConstraintAnnotation").Attribute("Name")));
        Assert.NotEqual(named, await SqlServerFixture.TextAsync(fresh.ConnectionString, BodyDefault));
        Assert.Equal(1, await SqlServerFixture.ScalarAsync(fresh.ConnectionString, BodyDefaultIsSystemNamed));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S11_a_DF_name_given_explicitly_is_dropped_by_every_plan_of_a_project_that_leaves_it_unnamed()
    {
        await using var environment = await SqlServerFixture.RegisterAsync();
        var unnamed = await GoldenProject.Built(GoldenProject.Change("add-unnamed-constraints"));
        GoldenProject.Publish(unnamed, environment, GoldenProject.Pipeline());
        await SqlServerFixture.ExecuteAsync(environment.ConnectionString, "DECLARE @q nvarchar(300) = N'dbo.' + (" + BodyDefault.TrimEnd(';')
            + "); EXEC sp_rename @q, N'DF__Note__Body__0BADF00D', N'OBJECT';");

        var operations = Operations(Plan(unnamed, environment.ConnectionString, environment.Name).Report);

        Assert.Equal(0, await SqlServerFixture.ScalarAsync(environment.ConnectionString, BodyDefaultIsSystemNamed));
        Assert.Contains(("Drop", "[dbo].[DF__Note__Body__0BADF00D]"), operations);
        Assert.Contains(operations, o => o.Operation == "Create" && o.Item.EndsWith("unnamed constraint on [dbo].[Note]", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S11_a_check_written_with_IN_is_dropped_and_created_again_by_every_plan_against_its_own_database()
    {
        await using var copy = await SqlServerFixture.RegisterAsync();
        var unnamed = await GoldenProject.Built(GoldenProject.Change("add-unnamed-constraints"));
        GoldenProject.Publish(unnamed, copy, GoldenProject.Pipeline());
        await using var named = await SqlServerFixture.RegisterAsync();
        using var scratch = ScratchFolder.Temporary("spike-s11-in");
        var inMemory = scratch.Under("named.dacpac");
        using (var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions()))
        {
            model.AddObjects("CREATE TABLE dbo.T (Id INT NOT NULL CONSTRAINT PK_T PRIMARY KEY, A INT NULL, CONSTRAINT CK_T_A_In CHECK (A IN (0, 1)));");
            DacPackageExtensions.BuildPackage(inMemory, model, new PackageMetadata());
        }

        GoldenProject.Publish(inMemory, named, GoldenProject.Pipeline());
        var (script, report) = Plan(unnamed, copy.ConnectionString, copy.Name);

        Assert.Equal(CheckWrittenWithInOnNote, Operations(report).Order());
        Assert.Contains("ADD CHECK (Pinned IN (0, 1))", script, StringComparison.Ordinal);
        Assert.Equal([("Create", "[dbo].[CK_T_A_In]"), ("Drop", "[dbo].[CK_T_A_In]")],
            Operations(Plan(inMemory, named.ConnectionString, named.Name).Report).Order());
    }

    /// <summary>The operations of S9's head against the database <see cref="HoldingAnUntrustedKeyAndADisabledCheck"/> makes: the disabled
    /// check dropped and created again, the disabled key the head does not declare dropped, and nothing of the untrusted key.</summary>
    private static (string Operation, string Item)[] DisabledCheckAndUndeclaredKey =>
        [("Create", "[dbo].[" + Check + "]"), ("Drop", "[dbo].[" + Check + "]"), ("Drop", "[dbo].[" + CategoryKey + "]")];

    /// <summary>The operations of a release that adds dbo.Customer.Nickname and leaves <see cref="CategoryKey"/> as the project declared
    /// it, against a database where the key is disabled: the column added and the key dropped and created again.</summary>
    private static (string Operation, string Item)[] TheCategoryKeyAddedAgain =>
        [("Alter", "[dbo].[Customer]"), ("Create", "[dbo].[" + CategoryKey + "]"), ("Drop", "[dbo].[" + CategoryKey + "]")];

    /// <summary>The operations of the plan of add-unnamed-constraints against a database it was published to: its Pinned check, written
    /// with IN, dropped and created again.</summary>
    private static (string Operation, string Item)[] CheckWrittenWithInOnNote =>
        [("Create", "Check Constraint: unnamed constraint on [dbo].[Note]"), ("Drop", "unnamed constraint on [dbo].[Note]")];

    /// <summary>A registered database holding the golden base, published and seeded, with <see cref="Untrusted"/>, <see cref="Disabled"/>
    /// and <see cref="DisabledCategoryKey"/> run on it as the fixture's administrator.</summary>
    private static async Task<RegisteredDatabase> HoldingAnUntrustedKeyAndADisabledCheck()
    {
        var environment = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), environment, GoldenProject.Pipeline());
        await SqlServerFixture.ExecuteAsync(environment.ConnectionString, Untrusted + " " + Disabled + " " + DisabledCategoryKey);
        return environment;
    }

    /// <summary>A registered database the package was published to, the package declaring <see cref="CategoryKey"/>; then the key
    /// disabled as the fixture's administrator and, when orphaned, <see cref="CategoryOrphan"/> written.</summary>
    private static async Task<RegisteredDatabase> WithTheCategoryKeyDisabled(string dacpac, bool orphaned)
    {
        var environment = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(dacpac, environment, GoldenProject.Pipeline());
        await SqlServerFixture.ExecuteAsync(environment.ConnectionString,
            "ALTER TABLE dbo.Product NOCHECK CONSTRAINT " + CategoryKey + ";" + (orphaned ? " " + CategoryOrphan : ""));
        Assert.Equal(3, await Trust(environment.ConnectionString, CategoryKey));
        return environment;
    }

    /// <summary>1 when the database holds dbo.Customer.Nickname, the column add-a-nullable-column adds, else 0.</summary>
    private static Task<int> Nicknamed(RegisteredDatabase database) => SqlServerFixture.ScalarAsync(database.ConnectionString,
        "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'Nickname';");

    /// <summary>
    /// A registered database given history: two tables with unnamed constraints created and dropped. SQL Server names an unnamed default,
    /// check or foreign key after its object id in hex, and two fresh databases allocate the same object ids, so a publish of one package
    /// to two fresh databases gives the same DF__, CK__ and FK__ names; an environment has history, and its names differ from a copy's.
    /// AUTO_CLOSE is set OFF first, as an environment's is: SQL Server Express and LocalDB create a database with AUTO_CLOSE ON, a publish
    /// sets it OFF, and that restarts the database, after which it allocates object ids as if the dropped tables had never existed
    /// (measured on SQL Server 2022 Express 16.0.4305.5).
    /// </summary>
    private static async Task<RegisteredDatabase> WithHistory()
    {
        var environment = await SqlServerFixture.RegisterAsync();
        await SqlServerFixture.ExecuteAsync(environment.ConnectionString, "ALTER DATABASE CURRENT SET AUTO_CLOSE OFF WITH NO_WAIT; "
            + "CREATE TABLE dbo.History (A INT NOT NULL DEFAULT (0), B INT NULL CHECK (B > 0)); CREATE TABLE dbo.History2 (A INT NOT NULL DEFAULT (0)); "
            + "DROP TABLE dbo.History; DROP TABLE dbo.History2;");
        return environment;
    }

    /// <summary>The head of S9's plan: the sample changes add-a-foreign-key and add-a-check-constraint, which declare the untrusted key
    /// and the disabled check as trusted and enabled.</summary>
    private static Task<string> Head() =>
        GoldenProject.Built(GoldenProject.Change("add-a-foreign-key"), GoldenProject.Change("add-a-check-constraint"));

    /// <summary>A constraint's state from the catalog: 0 trusted, 1 untrusted (is_not_trusted), 3 disabled (is_disabled, which leaves it
    /// untrusted too), <see cref="Absent"/> when the database holds no constraint of that name.</summary>
    private static Task<int> Trust(string connectionString, string constraint) => SqlServerFixture.ScalarAsync(connectionString,
        "SELECT ISNULL((SELECT CAST(is_disabled AS int) * 2 + CAST(is_not_trusted AS int) FROM (SELECT name, is_disabled, is_not_trusted "
        + "FROM sys.foreign_keys UNION ALL SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints) c WHERE name = @name), -1);",
        constraint);

    /// <summary>The states of S9's check, untrusted key and disabled key.</summary>
    private static async Task<(int Check, int Key, int CategoryKey)> Trusts(RegisteredDatabase database) =>
        (await Trust(database.ConnectionString, Check), await Trust(database.ConnectionString, Key),
            await Trust(database.ConnectionString, CategoryKey));

    /// <summary>After S10's failed validation: Customer 999, which the post-deploy seed adds; the key, which the main script added and
    /// committed; and the key's state, untrusted.</summary>
    private static async Task<(int Seeded, int Added, int State)> AfterTheFailedValidation(RegisteredDatabase copy) => (
        await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT COUNT(*) FROM dbo.Customer WHERE Id = 999;"),
        await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = @name;", Key),
        await Trust(copy.ConnectionString, Key));

    /// <summary>The S10 head's script holds each marker of <see cref="ValidationAfterTheSeed"/>, in that order.</summary>
    private static void ValidatesAfterTheSeed(string script) => InOrder(script, ValidationAfterTheSeed);

    /// <summary>The script holds each marker of <see cref="ReEnabledAfterTheSeed"/>, in that order, and adds the key WITH NOCHECK.</summary>
    private static void ReEnablesAfterTheSeed(string script)
    {
        InOrder(script, ReEnabledAfterTheSeed);
        Assert.Matches(@"WITH NOCHECK\s+ADD CONSTRAINT \[" + CategoryKey + @"\]", script);
    }

    /// <summary>The script holds each marker, and the first line holding each comes in the order given.</summary>
    private static void InOrder(string script, string[] markers)
    {
        var lines = script.Split('\n').ToList();
        int At(string marker) => lines.FindIndex(l => l.Contains(marker, StringComparison.Ordinal));
        Assert.All(markers, marker => Assert.True(At(marker) >= 0, marker + " is not in the script"));
        Assert.Equal(markers, markers.OrderBy(At));
    }

    /// <summary>
    /// S11's two databases, each holding add-unnamed-constraints: the Body default's names both SQL Server's (is_system_named 1), each
    /// DF__Note__Body__ and its object id, and different; the environment's name.
    /// </summary>
    private static async Task<string> SystemNamesDiffer(RegisteredDatabase environment, RegisteredDatabase copy)
    {
        var (named, copied) = (await SqlServerFixture.TextAsync(environment.ConnectionString, BodyDefault),
            await SqlServerFixture.TextAsync(copy.ConnectionString, BodyDefault));
        Assert.All([named, copied], name => Assert.StartsWith("DF__Note__Body__", name, StringComparison.Ordinal));
        Assert.NotEqual(named, copied);
        Assert.Equal((1, 1), (await SqlServerFixture.ScalarAsync(environment.ConnectionString, BodyDefaultIsSystemNamed),
            await SqlServerFixture.ScalarAsync(copy.ConnectionString, BodyDefaultIsSystemNamed)));
        return named!;
    }

    /// <summary>
    /// What a package extracted from S9's database keeps in model.xml: the disabled check and the disabled key with IsDisabled True and the
    /// annotation IsNotTrustedPropertyAnnotation, and the untrusted key with the annotation and no IsDisabled. The annotation is DacFx's
    /// internal name, so a DacFx release that renames it fails this receipt, which is re-run on every bump of the pin.
    /// </summary>
    private static void KeepsADisabledCheckAndAnUntrustedKey(string dacpac)
    {
        var (check, key, disabled) = (ModelElement(dacpac, "[dbo].[" + Check + "]"), ModelElement(dacpac, "[dbo].[" + Key + "]"),
            ModelElement(dacpac, "[dbo].[" + CategoryKey + "]"));
        static bool Untrusted(XElement element) =>
            element.Elements(Serialization + "Annotation").Any(a => (string?)a.Attribute("Type") == "IsNotTrustedPropertyAnnotation");
        static string? IsDisabled(XElement element) => (string?)element.Elements(Serialization + "Property")
            .SingleOrDefault(p => (string?)p.Attribute("Name") == "IsDisabled")?.Attribute("Value");

        Assert.Equal(("True", true), (IsDisabled(check), Untrusted(check)));
        Assert.Equal((null, true), (IsDisabled(key), Untrusted(key)));
        Assert.Equal(("True", true), (IsDisabled(disabled), Untrusted(disabled)));
    }

    private static string ModelXml(string dacpac)
    {
        using var package = ZipFile.OpenRead(dacpac);
        using var reader = new StreamReader(package.GetEntry("model.xml")!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>The Element of a package's model.xml whose Name is <paramref name="name"/>.</summary>
    private static XElement ModelElement(string dacpac, string name) =>
        XDocument.Parse(ModelXml(dacpac)).Descendants(Serialization + "Element").Single(e => (string?)e.Attribute("Name") == name);

    /// <summary>Each item of a deploy report: its operation's name and the item's value, in the report's order.</summary>
    private static List<(string Operation, string Item)> Operations(string report) =>
        [.. XDocument.Parse(report).Descendants(Report + "Operation").SelectMany(o => o.Elements(Report + "Item")
            .Select(i => ((string)o.Attribute("Name")!, (string)i.Attribute("Value")!)))];

    /// <summary>A script with the database's name and the object-id suffix of each system name replaced by placeholders, so a plan
    /// against the copy and one against the environment compare by role.</summary>
    private static string Normalized(string script, string database) =>
        Regex.Replace(script.Replace(database, "<database>", StringComparison.Ordinal), @"\[((?:DF|CK|FK|UQ|PK)__[^\]]*__)[0-9A-F]{8,16}\]",
            "[$1<id>]");

    /// <summary>DacServices.Script of a package, loaded from a stream, against the database, under the pipeline profile's options, both
    /// outputs generated, the script written to files of the plan's own (io/DacFx.Scripted).</summary>
    private static (string Script, string Report) Plan(string dacpac, string connectionString, string database)
    {
        using var stream = File.OpenRead(dacpac);
        using var package = DacPackage.Load(stream);
        var plan = DacFx.Scripted(new PublishOptions { GenerateDeploymentScript = true, GenerateDeploymentReport = true,
            DeployOptions = GoldenProject.Pipeline() }, given => new DacServices(connectionString).Script(package, database, given));
        return (plan.DatabaseScript, plan.DeploymentReport);
    }

    /// <summary>The registered database as io's copy, on the fixture's server.</summary>
    private static SqlServer.Copy Copy(RegisteredDatabase database, string server) =>
        new(Value(CopyName.Of("the registered database", database.Name)), server, Repository.Root);

    /// <summary>The registered database read by io's one read, DacFx.Extract, into its elements.</summary>
    private static async Task<SortedArray<Element>> Read(RegisteredDatabase database)
    {
        using var package = Value(DacFx.Extract(Copy(database, await SqlServerFixture.ServerAsync())));
        return Value(package.Elements).Elements;
    }

    private static Element Single(IEnumerable<Element> elements, string key) => elements.Single(e => e.Key.ToString() == key);
}
