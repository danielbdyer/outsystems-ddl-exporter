using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// Spike S2 (V3_MILESTONES.md §6) on every sample change: the BlockOnPossibleDataLoss checks DacFx 162.5.57, the release the pipeline is
/// presumed to run, and 170.5.96, the committed one, write for it, held to the extracted checks committed under
/// tests/Golden/data-loss-checks/; and the statement shapes the Facsimile's vocabulary matches (archive/v2/ssdt-agent/facsimile/
/// ARCHITECTURE.md §4, its §11 question 3) found in both releases' plans. Statements are compared within one way of planning, a live
/// Script against the copy, because a live plan adds WAITFOR DELAY batches a plan between two packages lacks. The releases' plans differ
/// in one block, Query Store's, which each release plans on a copy the other published; the tests pin that block and compare the rest.
/// </summary>
public sealed class BlockOnPossibleDataLossCheckTests(PlannedOnBothDacFxReleases plans) : IClassFixture<PlannedOnBothDacFxReleases>
{
    public static TheoryData<string> SampleChanges => new(GoldenProject.SampleChanges.Select(c => c.Name));

    [Theory]
    [Trait("Category", "fixture")]
    [MemberData(nameof(SampleChanges))]
    public void Each_sample_change_s_plan_writes_the_data_loss_checks_committed_for_it_on_both_DacFx_releases(string change)
    {
        var (on170, on162) = (CommittedBlockOnPossibleDataLossChecks.For(change, PlannedOnBothDacFxReleases.Committed),
            CommittedBlockOnPossibleDataLossChecks.For(change, DacFx162Child.Release));

        Assert.Equal((Lines(on170), Lines(on170), Lines(on162)), (Lines(BlockOnPossibleDataLossChecks.In(plans.Live170[change])),
            Lines(BlockOnPossibleDataLossChecks.In(plans.Package170[change])), Lines(BlockOnPossibleDataLossChecks.In(plans.Live162[change]))));
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

    /// <summary>The guard's opening, which the pre-deploy script's block comment also quotes, so only a parse tells a check from it.</summary>
    private const string Guard = "RAISERROR (N'Rows were detected";

    private static string Lines(IEnumerable<string> checks) => string.Concat(checks.Select(c => c + "\n"));

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
    }

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
