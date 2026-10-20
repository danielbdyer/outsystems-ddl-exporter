using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
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
/// (archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md), held here until WP 2.6 moves the Facsimile's case catalog to tests/Cases/. S9: a
/// schema read keeps a disabled constraint as the public property Disabled and an untrusted one only as the internal annotation
/// IsNotTrustedPropertyAnnotation, and no plan of the repository touches an untrusted key, so WP 3.5's trust step reads trust from the
/// catalog. S10: constraint validation runs after the commit and after the whole post-deploy script. S11: SQL Server names an unnamed
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

    private const string Key = "FK_Order_Customer_CustomerId";

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

    private static readonly XNamespace Report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly XNamespace Serialization = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>The markers of the S10 head's plan, in the order the script must hold them: the key added WITH NOCHECK, the commit, the
    /// post-deploy script's header, the seed row that adds the parent, the post-deploy script's last line, and the validation.</summary>
    private static readonly string[] ValidationAfterTheSeed =
    [
        "WITH NOCHECK", "COMMIT TRANSACTION", "the POST-DEPLOY slot", "(999, N'Wayne", "Post-deploy complete",
        "Checking existing data against newly created constraints", "WITH CHECK CHECK CONSTRAINT [FK_Order_Customer_CustomerId]",
    ];

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Spike_S9_a_schema_read_keeps_a_disabled_check_and_an_untrusted_key_and_its_publish_reproduces_both()
    {
        await using var environment = await HoldingAnUntrustedKeyAndADisabledCheck();
        var reader = await ReadOnlyPrincipal.CreateAsync(environment);
        Assert.Equal((3, 1), await Trusts(environment));

        using (var model = TSqlModel.LoadFromDatabase(reader.ConnectionString, new ModelExtractOptions()))
        {
            Assert.Equal((true, false), (
                model.GetObject(CheckConstraint.TypeClass, new ObjectIdentifier("dbo", Check), DacQueryScopes.UserDefined)
                    .GetProperty<bool>(CheckConstraint.Disabled),
                model.GetObject(ForeignKeyConstraint.TypeClass, new ObjectIdentifier("dbo", Key), DacQueryScopes.UserDefined)
                    .GetProperty<bool>(ForeignKeyConstraint.Disabled)));
        }

        using var scratch = ScratchFolder.Temporary("spike-s9");
        var extracted = scratch.Under("environment.dacpac");
        new DacServices(reader.ConnectionString).Extract(extracted, environment.Name, "spike", new Version(1, 0), "", null, DacFx.Extraction);
        KeepsADisabledCheckAndAnUntrustedKey(extracted);
        await using var fresh = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(extracted, fresh, GoldenProject.Pipeline());
        Assert.Equal((3, 1), await Trusts(fresh));

        // io's one element reader keeps Disabled and has nothing to keep of trust: the untrusted key reads as the trusted key the sample
        // change add-a-foreign-key builds.
        var read = await Read(environment);
        var head = (await GoldenProject.Model(GoldenProject.Change("add-a-foreign-key"))).Elements;
        Assert.Equal(new Value.Boolean(true), Single(read, "CheckConstraint [dbo].[" + Check + "]")["Disabled"]);
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

        var (script, report) = Plan(head, reader.ConnectionString, environment.Name);
        Assert.Equal([("Create", "[dbo].[CK_Product_Code]"), ("Drop", "[dbo].[CK_Product_Code]")], Operations(report).Order());
        Assert.Contains("WITH CHECK CHECK CONSTRAINT [CK_Product_Code]", script, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, script, StringComparison.Ordinal);

        using (var extracted = Value(DacFx.Extract(Copy(environment, await SqlServerFixture.ServerAsync()))))
        using (var source = Value(Ssdt.Open(head)))
        {
            var planned = Value(DacFx.Plan(source, extracted, environment.Name, Value(PublishProfiles.Load(GoldenProject.Profile)), []));
            Assert.Equal([("Create", "CheckConstraint [dbo].[CK_Product_Code]"), ("Drop", "CheckConstraint [dbo].[CK_Product_Code]")],
                planned.Report.Operations.Select(o => (o.Kind.Name, o.Key.ToString())));
        }

        GoldenProject.Publish(head, environment, GoldenProject.Pipeline());
        Assert.Equal((0, 1), await Trusts(environment));
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

    /// <summary>The operations of the plan of add-unnamed-constraints against a database it was published to: its Pinned check, written
    /// with IN, dropped and created again.</summary>
    private static (string Operation, string Item)[] CheckWrittenWithInOnNote =>
        [("Create", "Check Constraint: unnamed constraint on [dbo].[Note]"), ("Drop", "unnamed constraint on [dbo].[Note]")];

    /// <summary>A registered database holding the golden base, published and seeded, with <see cref="Untrusted"/> and
    /// <see cref="Disabled"/> run on it as the fixture's administrator.</summary>
    private static async Task<RegisteredDatabase> HoldingAnUntrustedKeyAndADisabledCheck()
    {
        var environment = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(await GoldenProject.Built(), environment, GoldenProject.Pipeline());
        await SqlServerFixture.ExecuteAsync(environment.ConnectionString, Untrusted + " " + Disabled);
        return environment;
    }

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
    /// untrusted too).</summary>
    private static Task<int> Trust(string connectionString, string constraint) => SqlServerFixture.ScalarAsync(connectionString,
        "SELECT CAST(is_disabled AS int) * 2 + CAST(is_not_trusted AS int) FROM (SELECT name, is_disabled, is_not_trusted FROM sys.foreign_keys "
        + "UNION ALL SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints) c WHERE name = @name;", constraint);

    /// <summary>The states of S9's check and key.</summary>
    private static async Task<(int Check, int Key)> Trusts(RegisteredDatabase database) =>
        (await Trust(database.ConnectionString, Check), await Trust(database.ConnectionString, Key));

    /// <summary>After S10's failed validation: Customer 999, which the post-deploy seed adds; the key, which the main script added and
    /// committed; and the key's state, untrusted.</summary>
    private static async Task<(int Seeded, int Added, int State)> AfterTheFailedValidation(RegisteredDatabase copy) => (
        await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT COUNT(*) FROM dbo.Customer WHERE Id = 999;"),
        await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = @name;", Key),
        await Trust(copy.ConnectionString, Key));

    /// <summary>The S10 head's script holds each marker of <see cref="ValidationAfterTheSeed"/>, in that order.</summary>
    private static void ValidatesAfterTheSeed(string script)
    {
        var lines = script.Split('\n').ToList();
        int At(string marker) => lines.FindIndex(l => l.Contains(marker, StringComparison.Ordinal));
        Assert.All(ValidationAfterTheSeed, marker => Assert.True(At(marker) >= 0, marker + " is not in the script"));
        Assert.Equal(ValidationAfterTheSeed, ValidationAfterTheSeed.OrderBy(At));
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
    /// What a package extracted from S9's database keeps in model.xml: the disabled check with IsDisabled True and the annotation
    /// IsNotTrustedPropertyAnnotation, and the untrusted key with the annotation and no IsDisabled. The annotation is DacFx's internal name,
    /// so a DacFx release that renames it fails this receipt, which is re-run on every bump of the pin.
    /// </summary>
    private static void KeepsADisabledCheckAndAnUntrustedKey(string dacpac)
    {
        var (check, key) = (ModelElement(dacpac, "[dbo].[" + Check + "]"), ModelElement(dacpac, "[dbo].[" + Key + "]"));
        static bool Untrusted(XElement element) =>
            element.Elements(Serialization + "Annotation").Any(a => (string?)a.Attribute("Type") == "IsNotTrustedPropertyAnnotation");
        static string? IsDisabled(XElement element) => (string?)element.Elements(Serialization + "Property")
            .SingleOrDefault(p => (string?)p.Attribute("Name") == "IsDisabled")?.Attribute("Value");

        Assert.Equal(("True", true), (IsDisabled(check), Untrusted(check)));
        Assert.Equal((null, true), (IsDisabled(key), Untrusted(key)));
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
