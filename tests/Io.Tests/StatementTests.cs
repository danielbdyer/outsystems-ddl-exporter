using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// The one path for the statements estate sends itself (R5 option A): every one of them, CREATE and DROP DATABASE included, is in the
/// run's queries.log with its site and its row count or failure; a command that runs past its timeout on an open connection is a
/// timed-out statement, not a server that does not answer; and any failure carrying a SqlException is read by that exception's number.
/// </summary>
public sealed class StatementTests : IDisposable
{
    private readonly string root = SqlServerFixture.EstateRoot(Path.Combine(Repository.Root, ".estate", "statements-under-test", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Every_statement_estate_sends_to_a_copy_is_in_the_run_s_log_with_its_site_and_row_count()
    {
        var log = SqlServer.QueryLog.Start(root);
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync(), log));
        try
        {
            Made(SqlServer.Reach(copy, log));
            Made(SqlServer.Measure(copy, Made(SqlServer.AggregateQuery.Of("SELECT COUNT_BIG(*) FROM sys.objects;", "sys.objects Rows")), log));
        }
        finally
        {
            Made(ScratchServer.Drop(copy, log));
        }

        Assert.Equal([("CREATE DATABASE", "0 rows"), ("VIEW DEFINITION", "1 row"), ("sys.objects Rows", "1 row"), ("DROP DATABASE", "0 rows")],
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
        var log = SqlServer.QueryLog.Start(root);
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            var slow = Made(SqlServer.AggregateQuery.Of("SELECT SUM(CASE WHEN a.object_id = b.object_id OR c.object_id = d.object_id THEN 1 ELSE 0 END) "
                + "FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b CROSS JOIN sys.all_objects AS c CROSS JOIN sys.all_objects AS d;", "billions of rows"));

            var measured = Made(SqlServer.Measure(copy, slow, log, TimeSpan.FromSeconds(1)));

            Assert.Equal(new SqlServer.Measurement.TimedOut("billions of rows", TimeSpan.FromSeconds(1)), measured);
            Assert.Equal("timed out after 1 s", Assert.Single(Entries(File.ReadAllText(log.Path))).Outcome);
            Assert.IsType<Result<SqlServer.Database>.Ok>(SqlServer.Reach(copy, log));
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
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
        var copy = Made(ScratchServer.Create(root, await SqlServerFixture.ServerAsync()));
        try
        {
            var divided = await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ScalarAsync(copy.Connection, "SELECT 1 / 0;"));

            var error = Assert.IsType<Result<int>.Failed>(SqlServer.Query<int>(copy, new SqlServer.Statement("a wrapped failure", "SELECT 1;"), null,
                _ => throw new InvalidOperationException("An outer failure.", divided))).Error;

            Assert.Equal("server.failed", error.Code);
            Assert.StartsWith("copy:" + copy.Name + " failed the statement: Msg 8134", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Made(ScratchServer.Drop(copy));
        }
    }

    /// <summary>queries.log as entries: a header line naming the target, the site and the outcome, the statement, then GO.</summary>
    private static (string Target, string Site, string Outcome)[] Entries(string log) =>
        [.. Regex.Matches(log, @"^-- \S+ (?<target>\S+) (?<site>.+): (?<outcome>[^\n]+)\n(?:(?!GO\n).*\n)+?GO\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(m => (m.Groups["target"].Value, m.Groups["site"].Value, m.Groups["outcome"].Value))];

    private static T Made<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}
