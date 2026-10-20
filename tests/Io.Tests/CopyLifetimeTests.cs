using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// A copy across dbchange processes (WP 3.4's done-when, VALUES.md O5 and O6): each process is this test assembly run as a program
/// (DbChangeProcess), making its copy through io/LocalServer on the server DBCHANGE_SQL names in its environment, never on its command
/// line. A run killed while it holds a copy leaves the database and its row, and the next run on the machine, a day later by the clock it
/// is given, drops both and nothing another machine, another server or a younger run registered. Two runs at once on one repository
/// root share no database, no registry row and no run folder, and reaching one base, one publishes and backs it up while the other waits
/// and restores it. Two drops and two sweeps of one registered copy at once all succeed, and a copy a RESTORE left RESTORING is dropped
/// by Drop and by the sweep.
/// </summary>
public sealed class CopyLifetimeTests : IDisposable
{
    private readonly string root = SqlServerFixture.RepositoryRoot(Path.Combine(Repository.Root, ".dbchange", "copies-under-test",
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O5")]
    public async Task A_killed_run_is_swept_by_the_next()
    {
        var server = await SqlServerFixture.ServerAsync();
        string killed;
        using (var run = Child(server, "copy", root))
        {
            killed = Answered(run).Name;
            run.Kill(entireProcessTree: true);
            Assert.True(run.WaitForExit(TimeSpan.FromMinutes(1)), "the killed run did not end");
        }

        Assert.True(await SqlServerFixture.ExistsAsync(killed), killed + " went with the run that made it");
        var row = Assert.Single(Registry(), r => (string)r["name"]! == killed);
        var created = DateTimeOffset.Parse((string)row["created"]!, CultureInfo.InvariantCulture);
        var here = Value(LocalServer.ServerName(server)).ToString();
        JsonObject[] planted =
        [
            Planted("another-machine-" + Environment.MachineName, here, created),
            Planted(Environment.MachineName, "localhost,1", created),
            Planted(Environment.MachineName, here, created.AddDays(1)),
        ];
        Write([row, .. planted]);

        var next = Value(LocalServer.Create(root, server, SqlServer.QueryLog.Start(root), now: created.AddDays(1).AddMinutes(1)));
        try
        {
            Assert.False(await SqlServerFixture.ExistsAsync(killed), killed + " outlived the next run's sweep");
            Assert.Equal(planted.Select(r => (string)r["name"]!).Append(next.Name.ToString()).Order(StringComparer.Ordinal),
                Registry().Select(r => (string)r["name"]!).Order(StringComparer.Ordinal));
        }
        finally
        {
            Value(LocalServer.Drop(next, SqlServer.QueryLog.Start(root)));
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O6")]
    public async Task Two_concurrent_runs_share_nothing()
    {
        var server = await SqlServerFixture.ServerAsync();
        var dacpac = await GoldenProject.Built();
        var key = Fingerprint.Of("two concurrent runs " + Guid.NewGuid().ToString("N"));
        using var one = Child(server, "base", root, dacpac, GoldenProject.Profile, key.ToString());
        using var two = Child(server, "base", root, dacpac, GoldenProject.Profile, key.ToString());
        try
        {
            var (first, second) = (Answered(one), Answered(two));

            Assert.NotEqual(first.Name, second.Name);
            Assert.True(await SqlServerFixture.ExistsAsync(first.Name) && await SqlServerFixture.ExistsAsync(second.Name),
                "a run's copy is missing while both runs hold theirs");
            Assert.Equal(new[] { first.Name, second.Name }.Order(StringComparer.Ordinal),
                Registry().Select(r => (string)r["name"]!).Order(StringComparer.Ordinal));
            Assert.NotEqual(Path.GetDirectoryName(first.Log), Path.GetDirectoryName(second.Log));
            Assert.Equal(["built", "restored"], new[] { first.How, second.How }.Order(StringComparer.Ordinal));
            Assert.Equal(await SqlServerFixture.ScalarAsync(Copied(server, first.Name), "SELECT COUNT(*) FROM dbo.Customer;"),
                await SqlServerFixture.ScalarAsync(Copied(server, second.Name), "SELECT COUNT(*) FROM dbo.Customer;"));

            foreach (var run in new[] { one, two })
            {
                run.StandardInput.WriteLine();
                Assert.Equal("dropped", run.StandardOutput.ReadLine());
                Assert.True(run.WaitForExit(TimeSpan.FromMinutes(1)) && run.ExitCode == 0, "a run did not drop its copy and end");
            }

            Assert.False(await SqlServerFixture.ExistsAsync(first.Name) || await SqlServerFixture.ExistsAsync(second.Name),
                "a run's copy outlived its run");
            Assert.Empty(Registry());
        }
        finally
        {
            foreach (var run in new[] { one, two }.Where(r => !r.HasExited))
            {
                run.Kill(entireProcessTree: true);
            }

            await SqlServerFixture.DeleteBackupsAsync(root);
        }
    }

    /// <summary>
    /// Two runs on one checkout that start together each sweep the copy a killed run left a day earlier, and a Drop can meet a sweep of
    /// the same copy. In each of 12 rounds, two Drop calls and two sweeps of one registered copy run at once: every call succeeds, and
    /// neither the database nor its row is left. Without Unmake's application lock, one drop's ALTER DATABASE ... SET SINGLE_USER WITH
    /// ROLLBACK IMMEDIATE killed the other's session (Msg 596, severity 21), or the other's ALTER met a database half dropped (Msg 5011),
    /// in the first round or soon after.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O6")]
    public async Task Two_drops_and_two_sweeps_of_one_copy_at_once_all_succeed_and_leave_neither_its_database_nor_its_row()
    {
        var server = await SqlServerFixture.ServerAsync();
        var here = Value(LocalServer.ServerName(server));
        var made = DateTimeOffset.UtcNow.AddDays(-2);
        for (var round = 1; round <= 12; round++)
        {
            var log = SqlServer.QueryLog.Start(root);
            var copy = Value(LocalServer.Create(root, server, log, now: made));
            Func<string>[] calls =
            [
                () => Failure(LocalServer.Drop(copy, log)), () => Failure(LocalServer.Drop(copy, log)),
                () => Failure(LocalServer.Swept(root, server, here, made.AddDays(2), log)),
                () => Failure(LocalServer.Swept(root, server, here, made.AddDays(2), log)),
            ];
            var failures = new string[calls.Length];
            using var start = new Barrier(calls.Length);
            var threads = calls.Select((call, i) => new Thread(() =>
            {
                start.SignalAndWait();
                failures[i] = call();
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            Assert.True(failures.All(f => f.Length == 0), "round " + round + ": " + string.Join(" | ", failures));
            Assert.False(await SqlServerFixture.ExistsAsync(copy.Name.ToString()), "round " + round + ": " + copy.Name + " outlived Drop");
            Assert.DoesNotContain(Registry(), r => (string)r["name"]! == copy.Name.ToString());
        }
    }

    /// <summary>
    /// A RESTORE that fails partway, or whose run is killed, leaves the copy's database RESTORING, and SQL Server refuses ALTER DATABASE
    /// on a database in that state (Msg 5052). A registered copy restored WITH NORECOVERY stands in for it: Drop removes its database and
    /// its row, and so does the sweep of the next copy made a day later.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O5")]
    public async Task A_copy_left_restoring_is_dropped_by_Drop_and_by_the_sweep()
    {
        var server = await SqlServerFixture.ServerAsync();
        var log = SqlServer.QueryLog.Start(root);
        var made = DateTimeOffset.UtcNow.AddDays(-2);
        using var source = DisposableCopy.Of(Value(LocalServer.Create(root, server, log)));
        try
        {
            var backup = Value(LocalServer.BackUp(source.Copy, Fingerprint.Of("left restoring " + Guid.NewGuid().ToString("N")), log));
            async Task<SqlServer.Copy> Restoring()
            {
                var copy = Value(LocalServer.Create(root, server, log, now: made));
                await SqlServerFixture.ExecuteAsync(server,
                    "DECLARE @from sysname = (SELECT name FROM sys.master_files WHERE database_id = DB_ID(@source) AND type = 0), "
                    + "@fromLog sysname = (SELECT name FROM sys.master_files WHERE database_id = DB_ID(@source) AND type = 1), "
                    + "@to nvarchar(4000) = (SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(@name) AND type = 0), "
                    + "@toLog nvarchar(4000) = (SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(@name) AND type = 1); "
                    + "RESTORE DATABASE @name FROM DISK = @path WITH REPLACE, NORECOVERY, MOVE @from TO @to, MOVE @fromLog TO @toLog;",
                    copy.Name.ToString(), ("@source", source.Copy.Name.ToString()), ("@path", backup.Path));
                Assert.Equal(1, await SqlServerFixture.ScalarAsync(server,
                    "SELECT COUNT(*) FROM sys.databases WHERE name = @name AND state_desc = N'RESTORING';", copy.Name.ToString()));
                return copy;
            }

            var dropped = await Restoring();
            Value(LocalServer.Drop(dropped, log));
            Assert.False(await SqlServerFixture.ExistsAsync(dropped.Name.ToString()), dropped.Name + " outlived Drop");
            Assert.DoesNotContain(Registry(), r => (string)r["name"]! == dropped.Name.ToString());

            var swept = await Restoring();
            using var next = DisposableCopy.Of(Value(LocalServer.Create(root, server, log, now: made.AddDays(2))));
            Assert.False(await SqlServerFixture.ExistsAsync(swept.Name.ToString()), swept.Name + " outlived the next copy's sweep");
            Assert.Equal(new[] { source.Copy.Name.ToString(), next.Copy.Name.ToString() }.Order(StringComparer.Ordinal),
                Registry().Select(r => (string)r["name"]!).Order(StringComparer.Ordinal));
        }
        finally
        {
            await SqlServerFixture.DeleteBackupsAsync(root);
        }
    }

    /// <summary>A call's failure as its code and message, or "" when it succeeded.</summary>
    private static string Failure<T>(Result<T> outcome) => outcome.Match(_ => "", error => error.Code + ": " + error.Message);

    /// <summary>This assembly run as a dbchange process in the mode given, the fixture's server as its DBCHANGE_SQL.</summary>
    private static Process Child(string server, params string[] arguments) =>
        DbChangeProcess.Start(new Dictionary<string, string> { ["DBCHANGE_SQL"] = server }, arguments);

    /// <summary>The copy a run holds, its run's log and how it was made, once the run prints them; fails with what the run printed
    /// instead.</summary>
    private static (string Name, string Log, string How) Answered(Process run)
    {
        var name = run.StandardOutput.ReadLine();
        if (name is null || CopyName.Of("the run's copy", name) is not Result<CopyName>.Ok)
        {
            run.Kill(entireProcessTree: true);   // its errors are read to their end, which a run still holding its copy never reaches
            Assert.Fail("the run made no copy: " + name + " " + run.StandardError.ReadToEnd());
        }

        return (name, run.StandardOutput.ReadLine()!, run.StandardOutput.ReadLine()!);
    }

    private static string Copied(string server, string name) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;

    /// <summary>A row as io/LocalServer writes one, for a copy no process holds and no database stands for.</summary>
    private static JsonObject Planted(string machine, string server, DateTimeOffset created) => new()
    {
        ["name"] = CopyName.Make(machine, 1, BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0)).ToString(), ["server"] = server,
        ["machine"] = CopyName.Make(machine, 0, 0).Machine, ["pid"] = 1,
        ["created"] = created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    private List<JsonObject> Registry()
    {
        var path = Path.Combine(root, ".dbchange", "copies.json");
        return File.Exists(path) ? [.. JsonNode.Parse(File.ReadAllText(path))!["copies"]!.AsArray().Select(r => r!.AsObject())] : [];
    }

    private void Write(IEnumerable<JsonObject> rows) => File.WriteAllText(Path.Combine(root, ".dbchange", "copies.json"),
        new JsonObject { ["copies"] = new JsonArray([.. rows.Select(r => r.DeepClone())]) }.ToJsonString());
}
