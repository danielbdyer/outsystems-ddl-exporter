using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Kernel;
using Estate.Tests;
using Microsoft.Data.SqlClient;
using Xunit;
using static Estate.Tests.Expect;

namespace Estate.Io.Tests;

/// <summary>
/// The one path for the statements estate sends itself (R5 option A): every one of them, CREATE and DROP DATABASE and the catalog read
/// for synonyms included, is in the run's queries.log with its site and its row count or failure; a command that runs past its timeout
/// on an open connection is a timed-out statement, not a server that does not answer; an aggregate query over a synonym is refused;
/// any failure carrying a SqlException is read by that exception's number; and a copy the server will not make, or a scratch server
/// login the server refuses, leaves no row in the registry.
/// </summary>
public sealed class StatementTests : IDisposable
{
    private readonly ScratchFolder folder = ScratchFolder.UnderRepository("statements-under-test");

    /// <summary>An estate's root whose posture names no environment, so R15 clears the scratch server.</summary>
    private string Root => SqlServerFixture.EstateRoot(folder.Path);

    public void Dispose() => folder.Dispose();

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Every_statement_estate_sends_to_a_copy_is_in_the_run_s_log_with_its_site_and_row_count()
    {
        var log = SqlServer.QueryLog.Start(Root);
        var copy = Value(ScratchServer.Create(Root, await SqlServerFixture.ServerAsync(), log));
        try
        {
            Value(SqlServer.Reach(copy, log));
            Value(SqlServer.Measure(copy, Value(SqlServer.AggregateQuery.Of("SELECT COUNT_BIG(*) FROM sys.objects;", "sys.objects Rows")), log));
        }
        finally
        {
            Value(ScratchServer.Drop(copy, log));
        }

        Assert.Equal([("CREATE DATABASE", "0 rows"), ("VIEW DEFINITION", "1 row"), ("Synonyms: sys.objects Rows", "0 rows"), ("sys.objects Rows", "1 row"), ("DROP DATABASE", "0 rows")],
            Entries(File.ReadAllText(log.Path)).Select(e => (e.Site, e.Outcome)));
        Assert.All(Entries(File.ReadAllText(log.Path)), e => Assert.Equal("copy:" + copy.Name, e.Target));
    }

    /// <summary>
    /// ARCH-13: an aggregate query that SQL Server is still running when its timeout of one second passes is measured as timed out,
    /// where it was a failed measurement reading Msg -2 as though the query had failed; the server answers the next statement.
    /// The query tests a condition on each of the trillions of combinations of four sys.all_objects rows, a condition on all four, so
    /// no server can count it from each table's rows alone, as it counts a cross join, and none finishes within a second.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task An_aggregate_query_past_its_timeout_is_measured_as_timed_out_and_the_server_still_answers()
    {
        var log = SqlServer.QueryLog.Start(Root);
        var copy = Value(ScratchServer.Create(Root, await SqlServerFixture.ServerAsync()));
        try
        {
            var slow = Value(SqlServer.AggregateQuery.Of("SELECT SUM(CASE WHEN a.object_id = b.object_id OR c.object_id = d.object_id THEN 1 ELSE 0 END) "
                + "FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b CROSS JOIN sys.all_objects AS c CROSS JOIN sys.all_objects AS d;", "billions of rows"));

            var measured = Value(SqlServer.Measure(copy, slow, log, TimeSpan.FromSeconds(1)));

            Assert.Equal(new SqlServer.Measurement.TimedOut("billions of rows", TimeSpan.FromSeconds(1)), measured);
            Assert.Equal("timed out after 1 s", Assert.Single(Entries(File.ReadAllText(log.Path)), e => e.Site == "billions of rows").Outcome);
            Value(SqlServer.Reach(copy, log));
        }
        finally
        {
            Value(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// DECISIONS.md, 2026-09-25: a synonym can stand for a table in another database or on a linked server, which the query's text
    /// cannot show, so an aggregate query that reads one is refused before it runs; the target's catalog is asked once, and the same
    /// query over the table itself is measured.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task An_aggregate_query_that_reads_a_synonym_is_refused_before_it_runs()
    {
        var log = SqlServer.QueryLog.Start(Root);
        var copy = Value(ScratchServer.Create(Root, await SqlServerFixture.ServerAsync()));
        try
        {
            await SqlServerFixture.ExecuteAsync(copy.Connection, "CREATE TABLE dbo.Person (Id INT NOT NULL PRIMARY KEY); INSERT dbo.Person (Id) VALUES (1), (2); CREATE SYNONYM dbo.People FOR dbo.Person;");

            var refused = Failed(SqlServer.Measure(copy, Value(SqlServer.AggregateQuery.Of("SELECT COUNT_BIG(*) FROM dbo.People;", "dbo.People Rows")), log), "aggregate-query.refused");
            var measured = Value(SqlServer.Measure(copy, Value(SqlServer.AggregateQuery.Of("SELECT COUNT_BIG(*) FROM dbo.Person AS p WHERE p.Id > 0;", "dbo.Person Rows")), log));

            Assert.StartsWith("The query dbo.People Rows reads [dbo].[People], a synonym", refused.Message, StringComparison.Ordinal);
            Assert.Equal(new SqlServer.Measurement.Answered("dbo.Person Rows", SortedArray.Of(SqlServer.Row.Of(2))), measured);
            Assert.DoesNotContain(Entries(File.ReadAllText(log.Path)), e => e.Site == "dbo.People Rows");
        }
        finally
        {
            Value(ScratchServer.Drop(copy));
        }
    }

    /// <summary>
    /// Finding R-5: a failure that carries a SqlException inside another exception is read by the SqlException's number, as a failure
    /// DacFx wraps is; before the one statement path, the scratch server's CREATE and DROP caught a SqlException alone, and anything
    /// wrapping one escaped unread.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_SqlException_inside_another_exception_is_read_by_its_number()
    {
        var copy = Value(ScratchServer.Create(Root, await SqlServerFixture.ServerAsync()));
        try
        {
            var divided = await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ScalarAsync(copy.Connection, "SELECT 1 / 0;"));

            var error = Failed(SqlServer.Query<int>(copy, new SqlServer.Statement("a wrapped failure", "SELECT 1;"), null, _ => throw new InvalidOperationException("An outer failure.", divided)), "server.failed");

            Assert.StartsWith("copy:" + copy.Name + " failed the statement: Msg 8134", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Value(ScratchServer.Drop(copy));
        }
    }

    /// <summary>A copy dropped twice: the second DROP DATABASE finds no database and no row, and is no error.</summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Dropping_a_copy_twice_is_no_error_and_leaves_no_row()
    {
        var copy = Value(ScratchServer.Create(Root, await SqlServerFixture.ServerAsync()));

        Value(ScratchServer.Drop(copy));
        Value(ScratchServer.Drop(copy));

        Assert.False(await SqlServerFixture.ExistsAsync(copy.Name.ToString()), copy.Name + " outlived Drop");
        Assert.Empty(Registry());
    }

    /// <summary>
    /// A login the server admits and that may not create a database, the read-only principal's: CREATE DATABASE is denied (Msg 262),
    /// and the row Create wrote before the statement is taken out of the registry again, so no row names a database that was never made.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_the_server_refuses_to_create_leaves_no_registry_row()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var reader = await ReadOnlyPrincipal.CreateAsync(database);

        var error = Failed(ScratchServer.Create(Root, reader.ConnectionString), "server.denied");

        Assert.Contains("Msg 262", error.Message, StringComparison.Ordinal);
        Assert.Empty(Registry());
    }

    /// <summary>
    /// The scratch server's own login refused, as after ~/.estate/sql.env names a password the container no longer has: a denial whose
    /// remedy names sql.env rather than a lead, and no row in the registry. On LocalDB, which authenticates Windows identities alone, a
    /// SQL login is refused as untrusted (Msg 18452), a denial too.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_scratch_server_login_the_server_refuses_names_sql_env_and_leaves_no_registry_row()
    {
        var wrong = new PlantedValue("Wr0ng!planted#7f3a");
        var server = new SqlConnectionStringBuilder(await SqlServerFixture.ServerAsync()) { IntegratedSecurity = false, UserID = "estate_nobody", Password = wrong.Text }.ConnectionString;

        var error = Failed(ScratchServer.Create(Root, server), "server.denied");

        Assert.Contains("sql.env", error.Remedy, StringComparison.Ordinal);
        wrong.AbsentFrom(error);
        Assert.Empty(Registry());
    }

    /// <summary>
    /// Finding ARCH-08: each entry is appended to the log, where the whole log was written again and replaced for each statement, so a
    /// run of N statements wrote bytes in proportion to N². A reader that holds the log open, as a person following a run does, reads
    /// each entry as it is added; a log replaced whole leaves that reader on the old file on Linux and cannot be replaced under it on
    /// Windows.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_entry_is_appended_to_the_log_a_reader_holds_open()
    {
        var log = SqlServer.QueryLog.Start(Root);
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 1), "Server=127.0.0.1,11433;User ID=sa", Root);
        log.Add(copy, "first", "SELECT 1;", "1 row");
        using var reader = new StreamReader(new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        var first = reader.ReadToEnd();

        log.Add(copy, "second", "SELECT 2;", "1 row");
        var second = reader.ReadToEnd();

        Assert.Equal(["first"], Entries(first).Select(e => e.Site));
        Assert.Equal(["second"], Entries(second).Select(e => e.Site));
        Assert.Equal(first + second, File.ReadAllText(log.Path));
    }

    /// <summary>The registry's rows, none when the file is absent.</summary>
    private JsonArray Registry() => File.Exists(Path.Combine(Root, ".estate", "copies.json"))
        ? JsonNode.Parse(File.ReadAllText(Path.Combine(Root, ".estate", "copies.json")))!["copies"]!.AsArray()
        : [];

    /// <summary>queries.log as entries: a header line naming the target, the site and the outcome, the statement, then GO.</summary>
    private static (string Target, string Site, string Outcome)[] Entries(string log) =>
        [.. Regex.Matches(log, @"^-- \S+ (?<target>\S+) (?<site>.+): (?<outcome>[^\n]+)\n(?:(?!GO\n).*\n)+?GO\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(m => (m.Groups["target"].Value, m.Groups["site"].Value, m.Groups["outcome"].Value))];
}
