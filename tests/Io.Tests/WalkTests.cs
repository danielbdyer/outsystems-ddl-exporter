using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Xunit.Abstractions;

namespace Estate.Io.Tests;

/// <summary>
/// io/Ssdt's walk (WP 1.2) against real builds: law 3′'s io half (M1 exit 4) and WP 1.3's archetype properties, each
/// archetype an edited copy of the proving ground built against dist/estate/ and walked. Package walks are compared with
/// package walks only. Make-mandatory edits Customer.Email, the proving ground's own populated nullable column (the seed
/// plants rows with and without an Email).
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class WalkTests(ProvingGroundWalks walks, ITestOutputHelper output) : IClassFixture<ProvingGroundWalks>
{
    /// <summary>What each archetype's change names, one line per element added, removed, renamed or changed.</summary>
    private static readonly Dictionary<string, string[]> Named = new()
    {
        ["make-mandatory"] = ["Column [dbo].[Customer].[Email]: Nullable true → false"],
        ["add a nullable column"] = ["added Column [dbo].[Customer].[Nickname]", "Table [dbo].[Customer]: Columns"],
        ["drop a column"] = ["removed Column [dbo].[Product].[LegacyCode]", "removed DefaultConstraint [dbo].[DF_Product_LegacyCode]", "Table [dbo].[Product]: Columns"],
        ["widen a column"] = ["Column [dbo].[Product].[Code]: Length 50 → 100"],
        ["add a check constraint"] = ["added CheckConstraint [dbo].[CK_Product_Code]"],
        ["add a foreign key"] = ["added ForeignKeyConstraint [dbo].[FK_Order_Customer_CustomerId]"],
        ["a seed edit"] = ["PostDeploymentScript [PostDeploy]: Text"],
        ["a pre-deploy edit"] = ["PreDeploymentScript [PreDeploy]: Text"],
        ["rename a column"] = [
            "added RefactorLogOperation [" + ProvingGroundWalks.RenameKey + "]",
            "renamed Column [dbo].[Customer].[ContactPhone] to Column [dbo].[Customer].[MobileNumber]"],
    };

    public static TheoryData<string> Archetypes => new(Named.Keys);

    [Fact]
    [Trait("Category", "fast")]
    public void Two_builds_of_the_proving_ground_walk_to_equal_reads_and_one_fingerprint()
    {
        var (first, second) = (walks.Reads["base"], walks.Reads["again"]);

        Assert.Equal(first, second);
        Assert.Equal(Fingerprint.Of(first.Elements), Fingerprint.Of(second.Elements));
        Assert.Contains(first.Elements, e => e.Key.ToString() == "Column [dbo].[Customer].[Email]" && e["Nullable"] == new Value.Boolean(true));
        Assert.Contains("MERGE dbo.Customer AS target", Text(first, Element.PostDeploymentScript), StringComparison.Ordinal);
        Assert.Contains("Pre-deploy: no backfill active.", Text(first, Element.PreDeploymentScript), StringComparison.Ordinal);
        Assert.DoesNotContain(first.Elements.SelectMany(e => e.Properties), p => p.Value is Value.Text { Content: var t } && t.Contains('\r', StringComparison.Ordinal));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"walk of the proving ground: {first.Elements.Count} elements, {first.Elements.Sum(e => e.Properties.Count)} properties, "
            + $"{first.Elements.Sum(e => e.Relationships.Sum(r => r.Targets.Count))} relationship targets, {walks.WalkTime.TotalMilliseconds:0} ms"));
    }

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Archetypes))]
    public void Each_archetype_edit_to_the_proving_ground_changes_the_fingerprint(string archetype) =>
        Assert.NotEqual(Fingerprint.Of(walks.Reads["base"].Elements), Fingerprint.Of(walks.Reads[archetype].Elements));

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Archetypes))]
    public void Each_archetype_s_change_between_real_walks_names_exactly_what_the_archetype_edits(string archetype) =>
        Assert.Equal(Named[archetype].Order(StringComparer.Ordinal), Lines(Between("base", archetype)).Order(StringComparer.Ordinal));

    /// <summary>M1 exit 2's shape: one line, and nothing else.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Make_mandatory_between_real_walks_is_Customer_Email_s_Nullable_true_to_false_and_nothing_else() =>
        Assert.Equal("Column [dbo].[Customer].[Email]: Nullable true → false", Assert.Single(Lines(Between("base", "make-mandatory"))));

    [Fact]
    [Trait("Category", "fast")]
    public void A_rename_with_its_refactorlog_entry_is_one_rename_and_without_the_entry_a_drop_and_an_add()
    {
        var (before, after) = (walks.Reads["base"], walks.Reads["rename a column"]);
        var phone = Key(before, "Column [dbo].[Customer].[ContactPhone]");
        var mobile = Key(after, "Column [dbo].[Customer].[MobileNumber]");

        Assert.Equal([new Rename(phone, mobile)], after.Renames);
        Assert.Equal([new Rename(phone, mobile)], Between("base", "rename a column").Renamed);
        var lost = Ok(Change.Between(before.Elements, Seq.Of(after.Elements.Where(e => e.Key.Type != Element.RefactorLogOperation)), []));
        Assert.Equal([mobile], lost.Added.Select(e => e.Key));
        Assert.Equal([phone], lost.Removed.Select(e => e.Key));
    }

    /// <summary>
    /// A model built in memory: two unnamed defaults DacFx meets in one order, then in the other. Each unnamed child is keyed
    /// by its table, its relationship and its position among its kind in the order of what it references, never by DacFx's
    /// order or a generated name; an index key column's direction is a property of the index.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unnamed_constraint_is_keyed_by_its_table_its_relationship_and_its_position_whatever_order_DacFx_meets_it()
    {
        using var inOrder = Model(reverse: false);
        using var reversed = Model(reverse: true);
        var (forward, backward) = (Ok(Ssdt.Walk(inOrder)), Ok(Ssdt.Walk(reversed)));

        Assert.Equal(forward, backward);
        var keys = forward.Select(e => e.Key.ToString()).ToList();
        Assert.Superset(
            new HashSet<string>(["PrimaryKeyConstraint [dbo].[T].[Host]", "CheckConstraint [dbo].[T].[Host]", "DefaultConstraint [dbo].[T].[Host 1]", "DefaultConstraint [dbo].[T].[Host 2]", "DatabaseOptions [DatabaseOptions]"]),
            keys.ToHashSet());
        var first = forward.Single(e => e.Key.ToString() == "DefaultConstraint [dbo].[T].[Host 1]");
        Assert.Equal("Column [dbo].[T].[A]", first.Relationships.Single(r => r.Name == "TargetColumn").Targets.Single().Key.ToString());
        var index = forward.Single(e => e.Key.ToString() == "Index [dbo].[T].[IX_T_A]");
        Assert.Equal((new Value.Boolean(true), new Value.Boolean(false)), (index["Columns[0].Ascending"], index["Columns[1].Ascending"]));
    }

    /// <summary>
    /// The proving ground with a table of unnamed inline constraints, published to a registered copy and read back with
    /// LoadFromDatabase: the database walk keys every object as the package walk does, though SQL Server named each constraint.
    /// Their values differ (SQL Server stores a check's text as it normalized it), which is why walk fingerprints are compared
    /// only between like sources.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_and_the_database_it_was_published_to_key_every_object_alike_unnamed_constraints_included()
    {
        await using var copy = await SqlServerFixture.RegisterAsync();
        ProvingGround.Publish(walks.Dacpacs["unnamed constraints"], copy, new Microsoft.SqlServer.Dac.DacDeployOptions());
        using var model = TSqlModel.LoadFromDatabase(copy.ConnectionString, new ModelExtractOptions());

        var package = walks.Reads["unnamed constraints"].Elements
            .Where(e => e.Key.Type is not (Element.PreDeploymentScript or Element.PostDeploymentScript or Element.RefactorLogOperation)).ToList();
        var database = Ok(Ssdt.Walk(model));
        Assert.Contains(package, e => e.Key.ToString() == "DefaultConstraint [dbo].[Note].[Host 2]");
        Assert.Equal(package.Select(e => e.Key), database.Select(e => e.Key));
        var check = package.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[Note].[Host]");
        Assert.NotEqual(check["Expression"], database.Single(e => e.Key == check.Key)["Expression"]);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_value_reads_as_the_kernel_s_closed_cases_a_double_as_its_invariant_string_and_a_property_DacFx_cannot_read_is_skipped()
    {
        using var model = Model(reverse: false);
        var table = model.GetObject(Table.TypeClass, new ObjectIdentifier("dbo", "T"), DacQueryScopes.UserDefined);
        var c = table.GetReferenced(Table.Columns).Single(column => column.Name.Parts[^1] == "C");
        var spatial = model.GetObjects(DacQueryScopes.UserDefined, SpatialIndex.TypeClass).Single();

        Assert.Equal(new Value.Boolean(true), Ssdt.ValueOf(c, Column.Nullable));
        Assert.Equal(new Value.Integer(10), Ssdt.ValueOf(c, Column.Length));
        Assert.Equal(new Value.Null(), Ssdt.ValueOf(c, Column.Collation));
        Assert.Equal(new Value.Enumeration("Durability", "SchemaAndData"), Ssdt.ValueOf(table, Table.Durability));
        Assert.Equal(new Value.Text("10.5"), Ssdt.ValueOf(spatial, SpatialIndex.XMax));
        Assert.Null(Ssdt.ValueOf(table, Column.Nullable));
    }

    private static TSqlModel Model(bool reverse)
    {
        var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        string[] defaults = ["ALTER TABLE dbo.T ADD DEFAULT (0) FOR A;", "ALTER TABLE dbo.T ADD DEFAULT (5) FOR B;"];
        foreach (var script in ((string[])["CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NULL CHECK (B > 0), C NVARCHAR(10) NULL);"])
            .Concat(reverse ? defaults.Reverse() : defaults)
            .Append("CREATE INDEX IX_T_A ON dbo.T (A, B DESC);")
            .Append("CREATE TABLE dbo.S (Id INT NOT NULL CONSTRAINT PK_S PRIMARY KEY, G GEOMETRY NULL);")
            .Append("CREATE SPATIAL INDEX SX ON dbo.S (G) WITH (BOUNDING_BOX = (0, 0, 10.5, 10));"))
        {
            model.AddObjects(script);
        }

        return model;
    }

    private Change Between(string before, string after) =>
        Ok(Change.Between(walks.Reads[before].Elements, walks.Reads[after].Elements, walks.Reads[after].Renames));

    /// <summary>A change as lines: each element added, removed or renamed, and each property (with its values, a script's text left out) or relationship that differs.</summary>
    private static IEnumerable<string> Lines(Change change) =>
        change.Added.Select(e => "added " + e.Key)
            .Concat(change.Removed.Select(e => "removed " + e.Key))
            .Concat(change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Changed.SelectMany(a => a.Properties
                .Select(p => a.Key + ": " + p.Name + (p.Before is Value.Text || p.After is Value.Text ? "" : " " + p.Before + " → " + p.After))
                .Concat(a.Relationships.Select(r => a.Key + ": " + r.Name))));

    private static ElementKey Key(Ssdt.Read read, string key) => read.Elements.Single(e => e.Key.ToString() == key).Key;

    private static string Text(Ssdt.Read read, string type) => ((Value.Text)read.Elements.Single(e => e.Key.Type == type)["Text"]!).Content;

    private static T Ok<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));
}

/// <summary>
/// The proving ground built twice from two copies, and once per head from a copy with the head's edits, all against
/// dist/estate/ and in parallel, each loaded and walked; then the base walked again, alone, for the walk's time. The tree
/// under .estate/walk/ is dropped after.
/// </summary>
public sealed class ProvingGroundWalks : IAsyncLifetime
{
    public const string RenameKey = "6d1c1b5e-3f0a-4c2e-9b7d-2a4f8e6c0d13";

    /// <summary>Each head's edits: a file, text that occurs in it exactly once, and its replacement.</summary>
    private static readonly Dictionary<string, (string File, string From, string To)[]> Edits = new()
    {
        ["base"] = [],
        ["again"] = [],
        ["make-mandatory"] = [("Modules/Customer.sql", "Email           NVARCHAR(256)   NULL,", "Email           NVARCHAR(256)   NOT NULL,")],
        ["add a nullable column"] = [("Modules/Customer.sql", "AccountId       INT             NULL,", "AccountId       INT             NULL,\n    Nickname        NVARCHAR(40)    NULL,")],
        ["drop a column"] = [("Modules/Product.sql", "LegacyCode NVARCHAR(40)  NOT NULL CONSTRAINT DF_Product_LegacyCode DEFAULT (N'LEGACY'),", "")],
        ["widen a column"] = [("Modules/Product.sql", "Code    NVARCHAR(50)    NOT NULL,", "Code    NVARCHAR(100)   NOT NULL,")],
        ["add a check constraint"] = [("Modules/Product.sql", "CONSTRAINT PK_Product_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Product_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT CK_Product_Code CHECK (LEN(Code) > 0)")],
        ["add a foreign key"] = [("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT FK_Order_Customer_CustomerId FOREIGN KEY (CustomerId) REFERENCES dbo.Customer (Id)")],
        ["a seed edit"] = [("Data/Seed.sql", "(3, N'Initech',", "(3, N'Initech Ltd',")],
        ["a pre-deploy edit"] = [("Script.PreDeployment.sql", "PRINT 'Pre-deploy: no backfill active.", "PRINT 'Pre-deploy: still no backfill active.")],
        ["rename a column"] =
        [
            ("Modules/Customer.sql", "ContactPhone    NVARCHAR(40)    NULL,", "MobileNumber    NVARCHAR(40)    NULL,"),
            ("SampleCatalog.refactorlog", "</Operations>",
                "  <Operation Name=\"Rename Refactor\" Key=\"" + RenameKey + "\" ChangeDateTime=\"09/24/2026 10:00:00\">\n"
                + "    <Property Name=\"ElementName\" Value=\"[dbo].[Customer].[ContactPhone]\" />\n"
                + "    <Property Name=\"ElementType\" Value=\"SqlSimpleColumn\" />\n"
                + "    <Property Name=\"ParentElementName\" Value=\"[dbo].[Customer]\" />\n"
                + "    <Property Name=\"ParentElementType\" Value=\"SqlTable\" />\n"
                + "    <Property Name=\"NewName\" Value=\"[MobileNumber]\" />\n"
                + "  </Operation>\n</Operations>"),
        ],
        ["unnamed constraints"] = [("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.",
            "CREATE TABLE dbo.Note (Id INT NOT NULL PRIMARY KEY, CustomerId INT NULL REFERENCES dbo.Customer (Id), Body NVARCHAR(200) NOT NULL DEFAULT (N''),"
            + " Pinned BIT NOT NULL DEFAULT (0) CHECK (Pinned IN (0, 1)), Code NVARCHAR(10) NULL UNIQUE);")],
    };

    private readonly string root = Path.Combine(Repository.Root, ".estate", "walk", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

    public Dictionary<string, Ssdt.Read> Reads { get; } = [];

    public Dictionary<string, string> Dacpacs { get; } = [];

    public TimeSpan WalkTime { get; private set; }

    public async Task InitializeAsync()
    {
        var tool = new PublishedTool();
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        Directory.CreateDirectory(root);
        foreach (var stop in (string[])["Directory.Build.props", "Directory.Packages.props"])
        {
            File.Copy(Path.Combine(golden, stop), Path.Combine(root, stop));
        }

        var reads = await Task.WhenAll(Edits.Select(head => Task.Run(() =>
        {
            var directory = Path.Combine(root, "heads", head.Key.Replace(' ', '-'));
            ToolFolderTests.Copy(Path.Combine(golden, "proving-ground"), directory);
            foreach (var (file, from, to) in head.Value)
            {
                var path = Path.Combine(directory, file);
                var text = File.ReadAllText(path);
                Assert.True(text.Split(from).Length == 2, path + " does not hold exactly one '" + from + "'");
                File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
            }

            var dacpac = Ok(Ssdt.Build(Path.Combine(directory, "SampleCatalog.sqlproj"), tool.Folder, Path.Combine(root, "build", head.Key.Replace(' ', '-'))));
            using var package = Ok(Ssdt.Load(dacpac.Path));
            return (Head: head.Key, Dacpac: dacpac.Path, Read: Ok(Ssdt.Walk(package)));
        })));
        foreach (var (head, dacpac, read) in reads)
        {
            (Reads[head], Dacpacs[head]) = (read, dacpac);
        }

        // The base walked once more, alone and warm, for its time.
        using var again = Ok(Ssdt.Load(Dacpacs["base"]));
        var clock = Stopwatch.StartNew();
        Assert.Equal(Reads["base"], Ok(Ssdt.Walk(again)));
        WalkTime = clock.Elapsed;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static T Ok<T>(Result<T> result) => result.Match(value => value, refusal => throw new Xunit.Sdk.XunitException(refusal.Code + ": " + refusal.Message));
}
