using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// check cdc (WP 2.5) with no SQL Server: SQL Server Express runs no Change Data Capture, so the rows of a tracked table are pinned here,
/// over the printer and the ledger dbchange/ledgers/cdc-tracked.md; the asked's text is held to the aggregate-query allowlist, and each form
/// that would return a name or read past the catalog is refused; and --commit pushes the ledger to a local bare repository, never to the
/// repository's own origin.
/// </summary>
public sealed class CdcCheckTests : IDisposable
{
    private static readonly Name Customer = Expect.Value(Name.Of("dbo", "Customer"));

    private static readonly Name Order = Expect.Value(Name.Of("dbo", "Order"));

    private readonly Scratch scratch = new();

    public void Dispose() => scratch.Dispose();

    /// <summary>
    /// The printer, for an environment whose catalog marks two of the project's three tables is_tracked_by_cdc: the message counts them, the
    /// lines are the ledger's header and one row per table, and the JSON names each table under cdc, at outcome listed and exit 0.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S8")]
    public void The_printer_writes_one_ledger_row_per_table_the_environment_tracks_and_its_answer_validates_against_dbchange_check_1()
    {
        var dev = Environment("dev");
        var answer = new CdcCheck.Answer(dev, new string('a', 40), 3, [Customer, Order],
            CdcLedger.Written([], dev, [Order, Customer], "2026-10-21"), "2026-10-21", null, []);

        var envelope = Verbs.Cdc(answer, new Stamp(Expect.Value(DacFx.Version)));
        var json = Render.Json(envelope);

        Assert.Equal(
        [
            "env:dev tracks 2 of the 3 tables the project declares at commit aaaaaaaa by Change Data Capture.", "",
            "| Environment | Table | Measured |", "|---|---|---|", "| dev | [dbo].[Customer] | 2026-10-21 |", "| dev | [dbo].[Order] | 2026-10-21 |",
        ], Render.Markdown(envelope).Split('\n').Take(6));
        VerbAnswer.Valid("dbchange.check.1.schema.json", json);
        Assert.Equal(("listed", 0), ((string?)json["outcome"], (int)json["exit"]!));
        Assert.Equal(["[dbo].[Customer]", "[dbo].[Order]"], json["cdc"]!["tracked"]!.AsArray().Select(t => (string)t!));
        Assert.Null(json["check"]);
        Assert.Null(json["cdc"]!["branch"]);
    }

    /// <summary>An environment that tracks none of the project's tables prints one sentence saying so, and its ledger keeps no row of
    /// it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S8")]
    public void An_environment_that_tracks_no_table_prints_one_sentence_and_no_row()
    {
        var dev = Environment("dev");
        var answer = new CdcCheck.Answer(dev, new string('b', 40), 3, [], CdcLedger.Written([], dev, [], "2026-10-21"), "2026-10-21",
            null, []);

        var envelope = Verbs.Cdc(answer, new Stamp(Expect.Value(DacFx.Version)));

        Assert.Equal("env:dev tracks none of the 3 tables the project declares at commit bbbbbbbb by Change Data Capture.\n",
            Render.Markdown(envelope));
        Assert.Empty(Expect.Value(CdcLedger.Parse(answer.Ledger)));
    }

    /// <summary>
    /// The ledger as check cdc writes it: one environment's rows replaced, every other environment's kept, the rows in the order of their
    /// environment and table, and a name holding a quote, a closing bracket and a bar read back as written.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S8")]
    public void The_ledger_replaces_the_rows_of_the_environment_read_keeps_the_others_and_reads_back_as_written()
    {
        var (dev, qa) = (Environment("dev"), Environment("qa"));
        var odd = Expect.Value(Name.Of("sales", "O'Brien]|x"));
        var before = Expect.Value(CdcLedger.Parse(CdcLedger.Written([], qa, [Customer], "2026-10-01")));
        var written = CdcLedger.Written(before.Where(r => r.Environment != dev), dev, [Order, odd, Customer], "2026-10-21");

        var read = Expect.Value(CdcLedger.Parse(written));
        var again = Expect.Value(CdcLedger.Parse(CdcLedger.Written(read.Where(r => r.Environment != dev), dev, [], "2026-10-22")));

        Assert.Equal(["dev [dbo].[Customer] 2026-10-21", "dev [dbo].[Order] 2026-10-21", "dev [sales].[O'Brien]]|x] 2026-10-21",
            "qa [dbo].[Customer] 2026-10-01"], read.Select(r => r.Environment + " " + r.Table + " " + r.Measured));
        Assert.Equal(["qa [dbo].[Customer] 2026-10-01"], again.Select(r => r.Environment + " " + r.Table + " " + r.Measured));
        Assert.Empty(Expect.Value(CdcLedger.Read(scratch.Root)));   // a repository without the ledger lists no table
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("| dev | Customer | 2026-10-21 |")]
    [InlineData("| Dev | [dbo].[Customer] | 2026-10-21 |")]
    [InlineData("| dev | [dbo].[Customer] | 21/10/2026 |")]
    [InlineData("| dev | [dbo].[Customer] |")]
    [InlineData("| dev | [x].[dbo].[Customer] | 2026-10-21 |")]
    public void A_ledger_row_that_is_not_an_environment_a_two_part_table_and_a_date_is_refused_naming_its_line(string row)
    {
        var text = CdcLedger.Written([], Environment("qa"), [Customer], "2026-10-01") + row + "\n";

        var error = Expect.Failed(CdcLedger.Parse(text), "file.ledger-row");

        var line = text.Split('\n').ToList().IndexOf(row) + 1;
        Assert.Contains("line " + line + " ", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// check cdc's aggregate queries (VALUES.md P2): one CASE WHEN EXISTS item per table the project declares, reading sys.tables.is_tracked_by_cdc
    /// joined to sys.schemas, the table's schema and name as literals; at most 500 items a query; each query admitted as the text it runs
    /// as, and admitted again when read back; a name holding a quote and a bracket written as a literal SQL Server reads back as the name.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [Trait("Value", "S8")]
    public void Check_cdc_s_aggregate_query_passes_the_allowlist_one_item_per_table_at_most_500_items_a_query()
    {
        List<Name> tables = [Expect.Value(Name.Of("sales", "O'Brien]")), .. Enumerable.Range(0, 1000)
            .Select(i => Expect.Value(Name.Of("dbo", "T" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))))];

        var queries = Expect.Value(CdcCheck.Queries(tables));

        Assert.Equal([500, 500, 1], queries.Select(p => p.Tables.Count));
        Assert.Equal(tables.Order(), queries.SelectMany(p => p.Tables).Order());
        Assert.All(queries, p => Expect.Value(SqlServer.AggregateQuery.Of(p.Query.Statement, "read back")));
        Assert.Contains("CASE WHEN EXISTS (SELECT 1 FROM [sys].[tables] AS t INNER JOIN [sys].[schemas] AS s ON s.schema_id = t.schema_id"
            + " WHERE t.is_tracked_by_cdc = 1 AND s.name = N'sales' AND t.name = N'O''Brien]') THEN 1 ELSE 0 END",
            System.Text.RegularExpressions.Regex.Replace(queries[^1].Query.Statement, @"\s+", " "), StringComparison.Ordinal);
        Assert.Empty(Expect.Value(CdcCheck.Queries([])));
    }

    /// <summary>The negative cases of check cdc's aggregate queries: each form a catalog read of CDC could take that returns a name, an id, a list, or a value read
    /// past the catalog, refused by the allowlist.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [InlineData("SELECT s.name, t.name FROM sys.tables AS t INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id"
        + " WHERE t.is_tracked_by_cdc = 1;")]
    [InlineData("SELECT t.object_id FROM sys.tables AS t WHERE t.is_tracked_by_cdc = 1;")]
    [InlineData("SELECT STRING_AGG(t.name, N',') FROM sys.tables AS t WHERE t.is_tracked_by_cdc = 1;")]
    [InlineData("SELECT CASE WHEN EXISTS (SELECT 1 FROM master.sys.tables AS t WHERE t.is_tracked_by_cdc = 1) THEN 1 ELSE 0 END;")]
    [InlineData("SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables AS t WHERE t.is_tracked_by_cdc = 1) THEN t.name ELSE 0 END"
        + " FROM sys.tables AS t;")]
    [InlineData("SELECT COUNT(*) FROM sys.tables AS t WHERE t.is_tracked_by_cdc = 1 GROUP BY t.name;")]
    [InlineData("SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables AS t WHERE t.is_tracked_by_cdc = 1"
        + " AND t.name = (SELECT TOP 1 c.capture_instance FROM cdc.change_tables AS c)) THEN 1 ELSE 0 END;")]
    [InlineData("SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables AS t WHERE t.object_id = OBJECT_ID(N'[dbo].[Customer]')"
        + " AND t.is_tracked_by_cdc = 1) THEN 1 ELSE 0 END;")]
    [InlineData("EXEC sys.sp_cdc_help_change_data_capture;")]
    public void A_catalog_read_of_CDC_that_returns_a_name_an_id_or_a_value_read_past_the_catalog_is_refused(string text)
    {
        var error = Expect.Failed(SqlServer.AggregateQuery.Of(text, "check cdc"), "aggregate-query.refused");

        Assert.DoesNotContain("Customer", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What an aggregate query measured: each table it asks of whose item answered 1, in the order of their names; an error SQL Server raised, or a
    /// timeout, is cdc.unread naming the error's number and never its message.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_aggregate_query_s_answer_names_each_table_whose_item_answered_1_and_a_raised_error_is_cdc_unread_by_its_number()
    {
        var asked = Expect.Value(CdcCheck.Queries([Order, Customer])).Single();
        var site = asked.Query.Site;

        var tracked = Expect.Value(CdcCheck.Tracked(asked,
            new SqlServer.Measurement.Answered(site, [SqlServer.Row.Of([.. asked.Tables.Select(t => t == Order ? 1L : 0L)])])));
        var raised = Expect.Failed(CdcCheck.Tracked(asked, new SqlServer.Measurement.Raised(site, 229, "planted-message")), "cdc.unread");
        var late = Expect.Failed(CdcCheck.Tracked(asked, new SqlServer.Measurement.TimedOut(site, TimeSpan.FromSeconds(30))), "cdc.unread");

        Assert.Equal([Order], tracked);
        Assert.Contains("Msg 229", raised.Message, StringComparison.Ordinal);
        new PlantedValue("planted-message").AbsentFrom(raised);
        Assert.Contains("30 s", late.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// --commit: the ledger with dev's rows committed on dbchange/cdc-dev-&lt;date&gt; and pushed to a local bare origin, the one path
    /// changed; the caller's branch, index and working tree as they were; the same rows a second time commit nothing; and a branch that
    /// exists refused before anything is written.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "S8")]
    public void Check_cdc_commit_pushes_the_ledger_on_a_new_branch_to_a_local_bare_origin_and_leaves_the_working_tree_as_it_was()
    {
        var origin = scratch.Origin();
        var (dev, qa) = (Environment("dev"), Environment("qa"));
        var kept = CdcLedger.Written([], qa, [Customer], "2026-10-01");
        var head = scratch.Commit("the estate", (CdcLedger.File, kept), ("README.md", "estate\n"));
        scratch.Git("push", "-q", "origin", "main");
        var proposed = CdcLedger.Written(Expect.Value(CdcLedger.Parse(kept)), dev, [Order], "2026-10-21");

        var pushed = Expect.Value(CdcCheck.Proposed(scratch.Root, dev, proposed, "2026-10-21"));
        var unchanged = Expect.Value(CdcCheck.Proposed(scratch.Root, qa, kept, "2026-10-21"));
        var exists = Expect.Failed(CdcCheck.Proposed(scratch.Root, dev, proposed, "2026-10-21"), "git-branch.exists");

        Assert.Equal("dbchange/cdc-dev-2026-10-21", pushed!.Branch);
        Assert.Equal(proposed.TrimEnd('\n'), scratch.GitAt(origin, "show", pushed.Commit + ":" + CdcLedger.File).TrimEnd('\n'));
        Assert.Equal(CdcLedger.File, scratch.GitAt(origin, "diff-tree", "-r", "--name-only", "--no-commit-id", head, pushed.Commit));
        Assert.Equal(("refs/heads/main", head, ""),
            (scratch.Git("symbolic-ref", "HEAD"), scratch.Git("rev-parse", "HEAD"), scratch.Git("status", "--porcelain")));
        Assert.Equal(kept, File.ReadAllText(Path.Combine(scratch.Root, CdcLedger.File)));
        Assert.Null(unchanged);
        Assert.Contains("dbchange/cdc-dev-2026-10-21", exists.Message, StringComparison.Ordinal);
    }

    private static EnvironmentName Environment(string name) => Expect.Value(EnvironmentName.Of("--env", name));
}
