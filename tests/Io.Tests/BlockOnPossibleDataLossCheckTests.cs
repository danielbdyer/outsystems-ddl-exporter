using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// Spike S2 (V3_MILESTONES.md §6) on every sample change: the BlockOnPossibleDataLoss checks DacFx 162.5.57, the release the pipeline is
/// presumed to run, and 170.5.96, the committed one, write for it, held to the extracted checks committed under
/// tests/Golden/data-loss-checks/; and the statement shapes the Facsimile's vocabulary matches (tests/Cases/
/// ARCHITECTURE.md §4, its §11 question 3) found in both releases' plans. Statements are compared within one way of planning, a live
/// Script against the copy, because a live plan adds WAITFOR DELAY batches a plan between two packages lacks. The releases' plans differ
/// in one block, Query Store's, which each release plans on a copy the other published; the tests pin that block and compare the rest.
/// </summary>
public sealed class BlockOnPossibleDataLossCheckTests(PlannedOnBothDacFxReleases plans) : IClassFixture<PlannedOnBothDacFxReleases>
{
    public static TheoryData<string> SampleChanges => new(GoldenProject.SampleChanges.Select(c => c.Name));

    /// <summary>The sample changes whose plans write a BlockOnPossibleDataLoss check on the committed release.</summary>
    public static TheoryData<string> Checked => new(GoldenProject.SampleChanges.Select(c => c.Name)
        .Where(c => CommittedBlockOnPossibleDataLossChecks.For(c, PlannedOnBothDacFxReleases.Committed).Count > 0));

    /// <summary>WP 2.2's done-when: io/BlockOnPossibleDataLoss, the reader, finds in each plan exactly the checks spike S2 extracted and
    /// committed, from a live plan by each release, the 162.5.57 half run through tests/DacFx162/, and from io's own DacFx.Plan.</summary>
    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(SampleChanges))]
    public void Each_sample_change_s_plan_writes_the_data_loss_checks_committed_for_it_on_both_DacFx_releases(string change)
    {
        var (on170, on162) = (CommittedBlockOnPossibleDataLossChecks.For(change, PlannedOnBothDacFxReleases.Committed),
            CommittedBlockOnPossibleDataLossChecks.For(change, DacFx162Child.Release));

        Assert.Equal((Lines(on170), Lines(on170), Lines(on162)), (Lines(Read(plans.Live170[change])),
            Lines(Read(plans.Package170[change])), Lines(Read(plans.Live162[change]))));
    }

    /// <summary>
    /// The other half of WP 2.2's done-when: each check's predicate, run through SqlServer.Measure as SELECT CASE WHEN EXISTS (…) THEN 1
    /// ELSE 0 END, answers 1 on the copy the golden project's seed populated and 0 on a copy of the same schema whose rows were deleted,
    /// for the checks of either release's live plan.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(Checked))]
    public void Each_data_loss_check_s_predicate_answers_1_on_a_populated_copy_and_0_on_an_empty_one(string change)
    {
        using var root = ScratchFolder.UnderRepository("data-loss-predicates");
        var (populated, empty) = (Resolved(root, "populated", plans.Populated), Resolved(root, "empty", plans.Emptied));
        var log = SqlServer.QueryLog.Start(root.Path);
        var checks = plans.Live.SelectMany(r => Expect.Value(BlockOnPossibleDataLoss.Read(r.Scripts[change], []))).ToList();

        Assert.NotEmpty(checks);
        Assert.All(checks, check => Assert.Equal((check.ToString(), (long?)1, (long?)0), (check.ToString(), Answer(populated, check, log),
            Answer(empty, check, log))));
    }

    /// <summary>
    /// Spike S2's decision line (DECISIONS.md, 2026-10-19): DacFx writes one check per table under a block comment naming each site on
    /// it. two-sites-on-one-table makes [Email] NOT NULL and narrows [ContactPhone] on [dbo].[Customer], and each release's live plan
    /// writes one check on [dbo].[Customer] whose comment names both columns, which the reader gives as the check's two sites.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void The_one_check_of_a_table_changed_at_two_sites_names_both_in_its_block_comment_on_both_DacFx_releases() =>
        Assert.All(plans.Live, r =>
        {
            var check = Assert.Single(Expect.Value(BlockOnPossibleDataLoss.Read(r.Scripts["two-sites-on-one-table"], [])),
                c => c.Table.ToString() == "[dbo].[Customer]");
            Assert.Equal(2, check.Sites.Count);
            Assert.Contains(check.Sites, s => s.Statement.Contains("Email", StringComparison.Ordinal));
            Assert.Contains(check.Sites, s => s.Statement.Contains("ContactPhone", StringComparison.Ordinal));
        });

    /// <summary>
    /// WP 1.4's rule on the reader: a check's query that fails against an environment classified real reports its error number and its
    /// site alone. The check reads a planted string as a number, which SQL Server refuses with Msg 245 quoting the value.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "X2")]
    public async Task A_data_loss_check_whose_query_fails_against_a_real_environment_reports_only_its_number_and_its_site()
    {
        var planted = PlantedValue.Unique();
        await SqlServerFixture.ExecuteAsync(plans.Emptied.ConnectionString,
            "CREATE TABLE dbo.Planted (Id INT NOT NULL PRIMARY KEY, Value NVARCHAR(100) NOT NULL); INSERT dbo.Planted VALUES (1, @name);",
            planted.Text);
        using var root = ScratchFolder.UnderRepository("data-loss-failure");
        var uat = Resolved(root, "uat", plans.Emptied);
        var log = SqlServer.QueryLog.Start(root.Path);
        var check = Assert.Single(Expect.Value(BlockOnPossibleDataLoss.Read("IF EXISTS (select top 1 1 from [dbo].[Planted] where [Value] > 0)\n"
            + "    RAISERROR (N'Rows were detected.', 16, 127) WITH NOWAIT\nGO\n", [])));

        var failed = Assert.IsType<SqlServer.Measurement.Raised>(Expect.Value(BlockOnPossibleDataLoss.Measure(uat, check, log)));

        Assert.Equal((245, "BlockOnPossibleDataLoss on [dbo].[Planted]", (string?)null), (failed.Number, failed.Site, failed.Message));
        planted.AbsentFrom(failed + "\n" + File.ReadAllText(log.Path));
    }

    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(SampleChanges))]
    public void Both_DacFx_releases_plan_each_sample_change_as_the_same_statements_but_the_Query_Store_block_162_5_57_adds(string change)
    {
        var (on170, on162) = (BlockOnPossibleDataLossChecks.Statements(plans.Live170[change]), BlockOnPossibleDataLossChecks.Statements(plans.Live162[change]));

        Assert.Equal((0, 3), (on170.Count(QueryStore), on162.Count(QueryStore)));
        Assert.Equal(on170, on162.Where(s => !QueryStore(s)));
    }

    /// <summary>
    /// The one statement the releases plan differently, on any change and on none: a model that sets no Query Store option is Query Store
    /// OFF to 162.5.57 and READ_WRITE to 170.5.96, and each release plans the database to its own reading. SQL Server 2022 creates a
    /// database with Query Store READ_WRITE, and a publish by either release leaves it as that release reads the model, so on a copy one
    /// release published the other plans IF EXISTS (… sysdatabases …) BEGIN ALTER DATABASE … SET QUERY_STORE … WITH ROLLBACK IMMEDIATE END
    /// for the unchanged base, and the publishing release plans nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void An_unset_Query_Store_is_OFF_to_DacFx_162_5_57_and_READ_WRITE_to_170_5_96_and_each_plans_it_on_the_other_s_copy()
    {
        var (on170Copy, on162Copy) = (plans.UnchangedOn170Copy, plans.UnchangedOn162Copy);

        Assert.Equal((2, 0), (on170Copy.QueryStoreState, on162Copy.QueryStoreState));
        Assert.Equal(
            ("", "ALTER DATABASE [$(DatabaseName)] SET QUERY_STORE = OFF WITH ROLLBACK IMMEDIATE",
                "ALTER DATABASE [$(DatabaseName)] SET QUERY_STORE (OPERATION_MODE = READ_WRITE) WITH ROLLBACK IMMEDIATE", ""),
            (QueryStoreSet(on170Copy.By170), QueryStoreSet(on170Copy.By162), QueryStoreSet(on162Copy.By170), QueryStoreSet(on162Copy.By162)));
        Assert.Equal([0, 3, 3, 0], new[] { on170Copy.By170, on170Copy.By162, on162Copy.By170, on162Copy.By162 }
            .Select(script => BlockOnPossibleDataLossChecks.Statements(script).Count(QueryStore)));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Neither_the_rebuild_s_IF_EXISTS_copy_nor_the_constraint_verification_s_RAISERROR_at_state_127_is_a_data_loss_check()
    {
        foreach (var (release, scripts) in plans.Live)
        {
            var rebuild = BlockOnPossibleDataLossChecks.Statements(scripts["turn-on-identity"]);
            var verification = BlockOnPossibleDataLossChecks.Statements(scripts["add-unnamed-constraints"]);

            Assert.Contains(rebuild, s => s.Kind == "IfStatement"
                && s.Text.StartsWith("IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Category]) BEGIN", StringComparison.Ordinal)
                && s.Text.Contains("INSERT INTO [dbo].[tmp_ms_xx_Category]", StringComparison.Ordinal));
            Assert.Contains(verification, s => s.Kind == "RaiseErrorStatement"
                && s.Text.Contains("RAISERROR (N'An error occurred while verifying constraints', 16, 127)", StringComparison.Ordinal));
            // the pre-deploy script's block comment quotes the guard in every plan, one that writes no check included
            Assert.Contains(Guard, BlockOnPossibleDataLossChecks.Body(scripts["add-a-nullable-column"]), StringComparison.Ordinal);
            Assert.Equal((release, 0, 0, 0), (release, BlockOnPossibleDataLossChecks.In(scripts["turn-on-identity"]).Count,
                BlockOnPossibleDataLossChecks.In(scripts["add-unnamed-constraints"]).Count, BlockOnPossibleDataLossChecks.In(scripts["add-a-nullable-column"]).Count));
        }
    }

    /// <summary>
    /// The receipts for the Facsimile's §11 question 3: each row a kind of its §4 vocabulary, the sample change that brings it out, the
    /// ScriptDom statement type that carries it and a fragment of that statement as io's Sql160ScriptGenerator writes it back.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("HasRows", "make-mandatory", "IfStatement", "SELECT TOP 1 1 FROM [dbo].[Customer]")]
    [InlineData("NotNull", "make-mandatory", "AlterTableAlterColumnStatement", "[Email] NVARCHAR (256) NOT NULL")]
    [InlineData("Fits", "narrow-a-column", "AlterTableAlterColumnStatement", "NVARCHAR (10) NOT NULL")]
    [InlineData("Fits", "cut-scale", "AlterTableAlterColumnStatement", "DECIMAL (18, 1)")]
    [InlineData("Fits", "change-collation", "AlterTableAlterColumnStatement", "COLLATE Latin1_General_CS_AS")]
    [InlineData("Unique", "add-unique-index", "CreateIndexStatement", "CREATE UNIQUE NONCLUSTERED INDEX [UIX_Product_Code]")]
    [InlineData("Unique", "add-unique-constraint", "AlterTableAddTableElementStatement", "UNIQUE NONCLUSTERED")]
    [InlineData("Unique", "redefine-the-primary-key", "CreateTableStatement", "[tmp_ms_xx_OrderLine]")]
    [InlineData("Unique", "redefine-the-primary-key", "CreateTableStatement", "PRIMARY KEY CLUSTERED")]
    [InlineData("Orphan", "add-a-foreign-key", "AlterTableConstraintModificationStatement",
        "WITH CHECK CHECK CONSTRAINT [FK_Order_Customer_CustomerId]")]
    [InlineData("Orphan", "add-an-unnamed-foreign-key-to-a-populated-table", "AlterTableAddTableElementStatement",
        "WITH NOCHECK ADD FOREIGN KEY")]
    [InlineData("CheckFalse", "add-a-check-constraint", "AlterTableConstraintModificationStatement", "[CK_Product_Code]")]
    [InlineData("CheckFalse", "add-an-unnamed-check-to-a-populated-table", "AlterTableAddTableElementStatement", "WITH NOCHECK ADD CHECK")]
    [InlineData("KeyLength", "add-an-index-over-the-key-limit", "CreateIndexStatement", "[IX_Product_Name]")]
    [InlineData("Rebuild", "turn-on-identity", "InsertStatement", "INSERT INTO [dbo].[tmp_ms_xx_Category]")]
    [InlineData("Computed", "add-a-persisted-computed-column", "AlterTableAddTableElementStatement", "AS LEN(Code) PERSISTED")]
    [InlineData("Temporal", "convert-to-temporal", "AlterTableSetStatement", "SYSTEM_VERSIONING = ON")]
    [InlineData("Scan", "add-an-index", "CreateIndexStatement", "CREATE NONCLUSTERED INDEX [IX_Customer_Email]")]
    [InlineData("Scan", "add-mandatory-with-default", "AlterTableAddTableElementStatement", "DEFAULT (0) NOT NULL")]
    public void Each_statement_shape_the_Facsimile_s_vocabulary_names_is_in_its_sample_change_s_plan_on_both_DacFx_releases(
        string kind, string change, string statement, string fragment) =>
        Assert.Empty(plans.Live
            .Where(r => !BlockOnPossibleDataLossChecks.Statements(r.Scripts[change]).Any(s => s.Kind == statement
                && s.Text.Contains(fragment, StringComparison.Ordinal)))
            .Select(r => kind + ": DacFx " + r.Release + "'s plan of " + change + " holds no " + statement + " with '" + fragment + "'"));

    /// <summary>
    /// WP 2.1's second slice, its done-when, and the Facsimile's QST-93: each sample change's plan, live on 170.5.96 and on 162.5.57 and
    /// package to package on 170.5.96, read by io/PlanStatements and by the vocabulary over the property read between the base's model and
    /// the change's, holds no statement of DacFx's own that no row matches: the Query Store block, #tmpErrors, @@ERROR and @@TRANCOUNT,
    /// PRINT, SET, WAITFOR DELAY, the constraint verification's cursor, a default dropped and added around an ALTER COLUMN, __RefactorLog
    /// and CREATE ROLE among them.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(SampleChanges))]
    public async Task QST_93_every_statement_of_DacFx_s_own_in_each_sample_change_s_plan_on_both_DacFx_releases_matches_a_row_of_the_vocabulary(
        string change) =>
        Assert.Empty((await Readings(change, null)).SelectMany(r => r.Reading.Unclassified
            .Select(s => r.Plan + ": " + Vocabulary.Where(s) + ", " + s.Type + ": " + s.Text)));

    /// <summary>The Facsimile's M1 (QST-95) for the rows whose case is a sample change: each row, removed, leaves a statement of that
    /// change's live plan on 170.5.96 unclassified or takes a precondition from what the plan asserts.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task QST_95_removing_any_row_of_the_vocabulary_whose_case_is_a_sample_change_changes_what_that_change_s_plan_asserts()
    {
        var kept = new List<string>();
        foreach (var row in Vocabulary.Rows.Where(r => r.Case.StartsWith("S2 ", StringComparison.Ordinal)))
        {
            var change = row.Case["S2 ".Length..];
            var (all, without) = ((await Readings(change, null))[0].Reading,
                (await Readings(change, [.. Vocabulary.Rows.Where(r => !ReferenceEquals(r, row))]))[0].Reading);
            if (all.Preconditions == without.Preconditions && all.Unclassified == without.Unclassified)
            {
                kept.Add(row.Type + " [" + string.Join("|", row.With) + "] for " + row.Case);
            }
        }

        Assert.Empty(kept);
    }

    /// <summary>The guard's opening, which the pre-deploy script's block comment also quotes, so only a parse tells a check from it.</summary>
    private const string Guard = "RAISERROR (N'Rows were detected";

    /// <summary>A sample change's three plans, each read by the vocabulary, the rows given or its own, over the property read between the
    /// base's model and the change's, with the deploy scripts of both packages.</summary>
    private async Task<(string Plan, Reading Reading)[]> Readings(string change, IReadOnlyList<VocabularyRow>? rows)
    {
        var sample = GoldenProject.SampleChanges.Single(c => c.Name == change);
        var (before, after) = (await GoldenProject.Model(), await GoldenProject.Model(sample));
        var made = Expect.Value(Change.Between(before.Elements, after.Elements, after.Renames));
        var (scriptsAfter, scriptsBefore) = (Deploy(await GoldenProject.Built(sample)), Deploy(await GoldenProject.Built()));
        Reading Read(string script)
        {
            var listing = Expect.Value(PlanStatements.Read(script, [], scriptsAfter, scriptsBefore));
            var sites = BlockOnPossibleDataLoss.Sites(Expect.Value(BlockOnPossibleDataLoss.Read(script, [])));
            return Vocabulary.Read(listing.Plan, listing.ScriptsBefore,
                Expect.Value(Preconditions.Of(before.Elements, after.Elements, made, sites)), before.Elements, after.Elements, made, rows);
        }

        return [("170.5.96, live", Read(plans.Live170[change])), ("170.5.96, package to package", Read(plans.Package170[change])),
            ("162.5.57, live", Read(plans.Live162[change]))];
    }

    /// <summary>A package's pre-deploy and post-deploy scripts.</summary>
    private static PlanStatements.Scripts Deploy(string package)
    {
        using var opened = Expect.Value(Ssdt.Open(package));
        return new(opened.PreDeploy, opened.PostDeploy);
    }

    private static string Lines(IEnumerable<string> checks) => string.Concat(checks.Select(c => c + "\n"));

    /// <summary>The checks the reader finds in a plan's script, each as DacFx wrote it.</summary>
    private static IEnumerable<string> Read(string script) => Expect.Value(BlockOnPossibleDataLoss.Read(script, [])).Select(c => c.Text);

    /// <summary>A check's predicate measured on a database: the one value it answers.</summary>
    private static long? Answer(SqlServer.Database database, BlockOnPossibleDataLoss.Check check, SqlServer.QueryLog log) =>
        Assert.Single(Assert.IsType<SqlServer.Measurement.Answered>(Expect.Value(BlockOnPossibleDataLoss.Measure(database, check, log)))
            .Rows).Values.Single();

    /// <summary>A copy reached as env:&lt;name&gt;, classified real, through a file: reference the owner alone reads.</summary>
    private static SqlServer.Database Resolved(ScratchFolder root, string name, RegisteredDatabase copy)
    {
        var file = root.File(name + ".connection", copy.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            // io/SqlServer refuses a connection file others can read
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        EnvironmentsJson.Of(name, new("file:" + file.Replace('\\', '/'), "localhost", Classification: "real")).WriteTo(root.Path);
        File.Copy(GoldenProject.Profile, Path.Combine(root.Path, "dbchange", "profiles", "pipeline.publish.xml"), overwrite: true);
        return Expect.Value(SqlServer.Resolve(Expect.Value(SqlServer.Target("env:" + name, "--target")), root.Path));
    }

    /// <summary>The ALTER DATABASE … SET QUERY_STORE of the script's Query Store block, or "" where it has none.</summary>
    private static string QueryStoreSet(string script) => BlockOnPossibleDataLossChecks.Statements(script)
        .SingleOrDefault(s => s.Kind == "AlterDatabaseSetStatement" && QueryStore(s)).Text ?? "";

    /// <summary>A statement of the Query Store block: the IF over sysdatabases, its BEGIN … END and its ALTER DATABASE.</summary>
    private static bool QueryStore((string Kind, string Text) statement) => statement.Text.Contains("QUERY_STORE", StringComparison.Ordinal);
}

/// <summary>
/// The extracted checks committed under tests/Golden/data-loss-checks/: &lt;sample change&gt;.sql, LF, UTF-8 without a BOM, holding only
/// the BlockOnPossibleDataLoss checks DacFx writes for that change, in script order, each as DacFx wrote it and followed by a line
/// break. &lt;sample change&gt;.&lt;release&gt;.sql, where present, holds what that release writes in place of the shared file. A sample change
/// with no file is the claim that DacFx writes no check for it.
/// </summary>
public sealed class CommittedBlockOnPossibleDataLossChecks
{
    private static readonly string Folder = Path.Combine(Repository.Root, "tests", "Golden", "data-loss-checks");

    private static readonly string[] Releases = [DacFx162Child.Release, PlannedOnBothDacFxReleases.Committed];

    /// <summary>The checks committed for the change on the release: its own file, else the shared one, else none.</summary>
    internal static IReadOnlyList<string> For(string change, string release) =>
        new[] { Path.Combine(Folder, change + "." + release + ".sql"), Path.Combine(Folder, change + ".sql") }.FirstOrDefault(File.Exists)
            is { } file ? BlockOnPossibleDataLossChecks.In(File.ReadAllText(file)) : [];

    [Fact]
    [Trait("Category", "fast")]
    public void Each_committed_data_loss_check_file_names_a_sample_change_and_holds_only_IF_EXISTS_RAISERROR_blocks_at_state_127()
    {
        var files = Directory.GetFiles(Folder).Order(StringComparer.Ordinal).ToList();
        var changes = GoldenProject.SampleChanges.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            var name = Path.GetFileName(file);
            var change = Releases.Select(r => "." + r + ".sql").Append(".sql").Where(e => name.EndsWith(e, StringComparison.Ordinal))
                .Select(e => name[..^e.Length]).FirstOrDefault() ?? name;
            var bytes = File.ReadAllBytes(file);
            var text = new UTF8Encoding(false, true).GetString(bytes);

            Assert.True(changes.Contains(change), name + " names no sample change under tests/Golden/changes/");
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), name + " begins with a byte order mark");
            Assert.NotEqual("", text);
            Assert.Equal(text, string.Concat(BlockOnPossibleDataLossChecks.In(text).Select(c => c + "\n")));
        });
    }
}

/// <summary>
/// Every sample change planned against one copy of the golden project's base, published and seeded by its post-deploy script under the
/// pipeline profile by DacFx 170.5.96: by 170.5.96 in this process, live (DacServices.Script against the copy) and package to package
/// (io's own DacFx.Plan, the plan predict reads); and by 162.5.57 live, in one run of the DacFx 162 child. Besides, the unchanged base
/// planned live by both releases against that copy and against a second copy, which the child published with 162.5.57. The three ways of
/// planning run at once against the one copy, so each gives DacFx files of its own for the script (io/DacFx.Scripted, and the child its
/// output folder): given none, each release names the file after the database alone, and one plan reads, or deletes, another's. The
/// scripts are kept as DacFx wrote them, CRLF line endings included, in memory, and the child's in a scratch folder deleted with the
/// copies; none is committed.
/// </summary>
public sealed class PlannedOnBothDacFxReleases : IAsyncLifetime
{
    /// <summary>The committed DacFx, which this process loads.</summary>
    public const string Committed = "170.5.96";

    private const string Unchanged = "base";

    private readonly ScratchFolder scripts = ScratchFolder.Temporary("s2-scripts");

    private readonly List<RegisteredDatabase> copies = [];

    public IReadOnlyDictionary<string, string> Live170 { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Package170 { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Live162 { get; private set; } = new Dictionary<string, string>();

    /// <summary>The unchanged base planned live by both releases against the copy 170.5.96 published.</summary>
    public UnchangedPlans UnchangedOn170Copy { get; private set; } = new(-1, "", "");

    /// <summary>The unchanged base planned live by both releases against the copy 162.5.57 published.</summary>
    public UnchangedPlans UnchangedOn162Copy { get; private set; } = new(-1, "", "");

    /// <summary>Each release's live plans, the committed release first.</summary>
    public IEnumerable<(string Release, IReadOnlyDictionary<string, string> Scripts)> Live =>
        [(Committed, Live170), (DacFx162Child.Release, Live162)];

    public async Task InitializeAsync()
    {
        Assert.Equal(Committed, Expect.Value(DacFx.Version).ToString());
        var basePackage = await GoldenProject.Built();
        var heads = await Task.WhenAll(GoldenProject.SampleChanges.Select(async c => (c.Name, Path: await GoldenProject.Built(c))));
        (string Name, string Path)[] unchanged = [(Unchanged, basePackage)];
        var (by170, by162) = (await Registered(), await Registered());
        var published162 = Task.Run(() =>
            DacFx162Child.Succeeded(["publish", GoldenProject.Profile, by162.Name, basePackage], by162.ConnectionString));
        GoldenProject.Publish(basePackage, by170, GoldenProject.Pipeline());
        var planned = await Task.WhenAll(Task.Run(() => Planned170Live(by170, [.. heads, .. unchanged])),
            Task.Run(() => Planned170Package(by170, basePackage, heads)),
            Task.Run(() => Planned162Live(by170, [.. heads, .. unchanged], "on-the-170-copy")));
        await published162;
        UnchangedOn170Copy = new(await QueryStoreState(by170), planned[0][Unchanged], planned[2][Unchanged]);
        UnchangedOn162Copy = new(await QueryStoreState(by162), Planned170Live(by162, unchanged)[Unchanged],
            Planned162Live(by162, unchanged, "on-the-162-copy")[Unchanged]);
        (Live170, Package170, Live162) = (Changes(planned[0]), planned[1], Changes(planned[2]));
        await SqlServerFixture.ExecuteAsync(by162.ConnectionString, Empty);
        (Populated, Emptied) = (by170, by162);
    }

    /// <summary>The copy 170.5.96 published, its rows the golden project's seed: what every plan above is planned against.</summary>
    public RegisteredDatabase Populated { get; private set; } = null!;

    /// <summary>The copy 162.5.57 published, every row of its tables deleted once the plans against it were made.</summary>
    public RegisteredDatabase Emptied { get; private set; } = null!;

    /// <summary>Every row of every user table deleted, its constraints left unchecked first so no foreign key orders the deletes; a
    /// history table of a system-versioned table refuses a DELETE, and the golden project's base holds none.</summary>
    private const string Empty = "DECLARE @sql nvarchar(max) = N'';\n"
        + "SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ALL; '\n"
        + "FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;\n"
        + "SELECT @sql += N'DELETE FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N'; '\n"
        + "FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;\n"
        + "EXEC sys.sp_executesql @sql;";

    public async Task DisposeAsync()
    {
        foreach (var copy in copies)
        {
            await copy.DisposeAsync();
        }

        scripts.Dispose();
    }

    private async Task<RegisteredDatabase> Registered()
    {
        var copy = await SqlServerFixture.RegisterAsync();
        copies.Add(copy);
        return copy;
    }

    /// <summary>sys.database_query_store_options' actual_state: 0 OFF, 1 READ_ONLY, 2 READ_WRITE.</summary>
    private static Task<int> QueryStoreState(RegisteredDatabase copy) =>
        SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT CAST(actual_state AS int) FROM sys.database_query_store_options;");

    private static Dictionary<string, string> Changes(Dictionary<string, string> planned) =>
        planned.Where(p => p.Key != Unchanged).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> Planned170Live(RegisteredDatabase copy, (string Name, string Path)[] heads)
    {
        var services = new DacServices(copy.ConnectionString);
        var options = GoldenProject.Pipeline();
        return heads.ToDictionary(h => h.Name, h =>
        {
            using var stream = File.OpenRead(h.Path);
            using var package = DacPackage.Load(stream);
            return DacFx.Scripted(new PublishOptions { GenerateDeploymentScript = true, GenerateDeploymentReport = false, DeployOptions = options },
                given => services.Script(package, copy.Name, given)).DatabaseScript;
        }, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> Planned170Package(RegisteredDatabase copy, string basePackage,
        (string Name, string Path)[] heads)
    {
        var profile = Expect.Value(PublishProfiles.Load(GoldenProject.Profile));
        using var target = Expect.Value(Ssdt.Open(basePackage));
        return heads.ToDictionary(h => h.Name, h =>
        {
            using var source = Expect.Value(Ssdt.Open(h.Path));
            return Expect.Value(DacFx.Plan(source, target, copy.Name, profile, [])).Script;
        }, StringComparer.Ordinal);
    }

    private Dictionary<string, string> Planned162Live(RegisteredDatabase copy, (string Name, string Path)[] heads, string folder)
    {
        var output = scripts.Folder(folder);
        DacFx162Child.Succeeded(["script", GoldenProject.Profile, copy.Name, output, .. heads.Select(h => h.Name + "=" + h.Path)],
            copy.ConnectionString);
        return heads.ToDictionary(h => h.Name, h => File.ReadAllText(Path.Combine(output, h.Name + ".sql")), StringComparer.Ordinal);
    }
}

/// <summary>The unchanged base planned live against one copy: the copy's Query Store state, and each release's script.</summary>
public sealed record UnchangedPlans(int QueryStoreState, string By170, string By162);

/// <summary>The DacFx 162 child as every spike starts it, here with no verb, which needs no SQL Server.</summary>
public sealed class DacFx162ChildTests
{
    [Fact]
    [Trait("Category", "build")]
    public void The_DacFx_release_beside_the_committed_one_reports_162_5_57_and_this_process_runs_170_5_96()
    {
        var ran = DacFx162Child.Run([]);

        Assert.Equal(("DacFx " + DacFx162Child.Release, 2, PlannedOnBothDacFxReleases.Committed),
            (DacFx162Child.Loaded(ran), ran.Code, Expect.Value(DacFx.Version).ToString()));
    }
}
