using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// io/LocalServer's choices that reach no server (WP 3.4): the first connect retried once and nothing else retried; the registry rows
/// the sweep drops, chosen by the copy's machine, its server and its age against a clock the caller gives; where a backup of a copy is
/// written, by the server's host; and a restore of a key the cache holds no backup of, refused before anything connects.
/// </summary>
public sealed class LocalServerTests : IDisposable
{
    private static readonly Error Unreachable = new("server.unreachable", "copy:x does not answer.", "Start the local server.");

    private readonly ScratchFolder scratch = ScratchFolder.Temporary("local-server");

    public void Dispose() => scratch.Dispose();

    /// <summary>
    /// The Facsimile's TLC-10: LocalDB's first connection can time out while the instance starts, so a connect that fails before the
    /// connection opens is tried once more until the server has first answered the process. A refusal, a timeout on an open connection,
    /// and a connection the server broke after it opened, which may have run the statement, are not sent again; once the server has
    /// answered, by a connection that opened or by a refused login, a later connect that fails is not retried.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_first_connect_to_the_local_server_is_retried_once_and_no_other_failure_is()
    {
        var denied = new Error("server.denied", "copy:x refused this identity.", "Check the login.");
        var timedOut = new Error("server.timed-out", "copy:x ran past its timeout.", "Run it again.");

        Assert.Equal((2, "server.unreachable"), Attempts(new LocalServer.FirstConnect(), (Unreachable, false), (Unreachable, false)));
        Assert.Equal((1, "server.denied"), Attempts(new LocalServer.FirstConnect(), (denied, false), (1, true)));
        Assert.Equal((1, "server.timed-out"), Attempts(new LocalServer.FirstConnect(), (timedOut, true), (1, true)));
        Assert.Equal((1, "server.unreachable"), Attempts(new LocalServer.FirstConnect(), (Unreachable, true), (1, true)));
        var connect = new LocalServer.FirstConnect();
        Assert.Equal((2, "1"), Attempts(connect, (Unreachable, false), (1, true)));
        Assert.Equal((1, "server.unreachable"), Attempts(connect, (Unreachable, false), (1, true)));
        var refused = new LocalServer.FirstConnect();
        Assert.Equal((1, "server.denied"), Attempts(refused, (denied, false), (1, true)));
        Assert.Equal((1, "server.unreachable"), Attempts(refused, (Unreachable, false), (1, true)));
    }

    /// <summary>
    /// The retry on the statement path itself (io/SqlServer.Query): a fresh dbchange process whose DBCHANGE_SQL names a TCP listener that
    /// accepts each connection and closes it at once makes a copy, so its first statement, the CREATE DATABASE, never reaches a server.
    /// The listener counts two connections, the first connect and its one retry, and the run fails as server.unreachable with no row
    /// left in .dbchange/copies.json.
    /// </summary>
    [Fact]
    [Trait("Category", "build")]
    public async Task A_copy_s_first_statement_to_a_server_that_never_answers_connects_twice_and_fails_as_server_unreachable()
    {
        var root = SqlServerFixture.RepositoryRoot(scratch.Folder("repository"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = 0;
        var accepting = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    listener.AcceptTcpClient().Dispose();
                    Interlocked.Increment(ref accepted);
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // the listener stopped
            }
        }) { IsBackground = true };
        accepting.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
            using var run = DbChangeProcess.Start(new Dictionary<string, string>
            {
                ["DBCHANGE_SQL"] = "Server=127.0.0.1," + port + ";User ID=sa;Password=unused;Connect Timeout=5;TrustServerCertificate=true",
            }, "copy", root);
            var errors = run.StandardError.ReadToEndAsync();
            var printed = run.StandardOutput.ReadToEnd();
            Assert.True(run.WaitForExit(TimeSpan.FromMinutes(1)), "the run did not end");

            Assert.True(printed.StartsWith("server.unreachable: ", StringComparison.Ordinal), printed + await errors);
            Assert.Equal((1, 2), (run.ExitCode, Volatile.Read(ref accepted)));
            Assert.DoesNotContain("dbchange_", File.ReadAllText(Path.Combine(root, ".dbchange", "copies.json")), StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
            accepting.Join(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// VALUES.md O5: a registry row is swept when its name is a copy's name for this machine, its server is the local server this machine
    /// names now (a copy on another server cannot be dropped from here), and it was registered more than a day before the clock given;
    /// a row whose time is missing or unreadable counts as old. A row of another machine, of another server, of a name dbchange never gives
    /// a copy, or of 23 hours ago stays.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O5")]
    public void The_sweep_chooses_only_this_host_s_copies_on_this_server_registered_more_than_a_day_ago()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var here = ServerName.Of("127.0.0.1,11434", Environment.MachineName);
        var suffix = 0u;
        JsonObject Row(string machine, string server, string? created) => new()
        {
            ["name"] = CopyName.Make(machine, 4242, ++suffix).ToString(), ["server"] = server, ["created"] = created,
        };
        string Ago(int hours) => now.AddHours(-hours).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var old = Row(Environment.MachineName, here.ToString(), Ago(25));
        var undated = Row(Environment.MachineName, here.ToString(), null);
        var garbled = Row(Environment.MachineName, here.ToString(), "yesterday");
        JsonObject[] kept =
        [
            Row(Environment.MachineName, here.ToString(), Ago(23)),
            Row("another-machine-" + Environment.MachineName, here.ToString(), Ago(25)),
            Row(Environment.MachineName, "localhost,1433", Ago(25)),
            new() { ["name"] = "master", ["server"] = here.ToString(), ["created"] = Ago(25) },
        ];

        Assert.Equal([(string)old["name"]!, (string)undated["name"]!, (string)garbled["name"]!],
            LocalServer.Stale([old, .. kept, undated, garbled], here, now).Select(n => n.ToString()));
    }

    /// <summary>
    /// A backup is written where the server can write it and a restore on this machine can read it again: LocalDB runs as the caller, so it
    /// writes into .dbchange/cache/; a SQL Server on this machine runs under its own account (the mssql user, or a Windows service), so it
    /// writes into its own default backup folder; a server on another machine writes to that machine's disk, so no backup is made, and the
    /// base is published from scratch, copy.uncached naming the server.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_backup_is_written_by_LocalDB_into_the_cache_by_a_server_on_this_machine_into_its_own_folder_and_by_no_other()
    {
        static IPAddress[] Resolve(string host) => [host == "sql-alias" ? IPAddress.Loopback : IPAddress.Parse("192.0.2.10")];
        LocalServer.BackupFolder Folder(string dataSource) =>
            Value(LocalServer.BackupFolderOf(scratch.Path, ServerName.Of(dataSource, Environment.MachineName), Resolve));

        Assert.Equal(Path.Combine(scratch.Path, ".dbchange", "cache"),
            Assert.IsType<LocalServer.BackupFolder.Cache>(Folder(@"(localdb)\MSSQLLocalDB")).Path);
        Assert.All(new[] { "127.0.0.1,11434", "localhost", Environment.MachineName + @"\SQLEXPRESS", "sql-alias,1433" },
            local => Assert.IsType<LocalServer.BackupFolder.Server>(Folder(local)));
        var remote = Failed(LocalServer.BackupFolderOf(scratch.Path, ServerName.Of("build-sql.corp.example,1433", Environment.MachineName),
            Resolve), "copy.uncached");
        Assert.Contains("build-sql.corp.example,1433", remote.Message, StringComparison.Ordinal);
    }

    /// <summary>A restore of a key .dbchange/cache/backups.json holds no backup of on this server is copy.uncached, and registers
    /// nothing.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_restore_of_a_key_the_cache_holds_no_backup_of_is_copy_uncached_and_registers_nothing()
    {
        var root = SqlServerFixture.RepositoryRoot(scratch.Folder("repository"));

        var error = Failed(LocalServer.Restore(root, "Server=127.0.0.1,1;User ID=sa;Password=unused;TrustServerCertificate=true",
            Fingerprint.Of("no backup"), SqlServer.QueryLog.Start(root)), "copy.uncached");

        Assert.Contains(Fingerprint.Of("no backup").ToString()[..16], error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, ".dbchange", "copies.json")), "a refused restore wrote the registry");
    }

    /// <summary>How many times <see cref="LocalServer.FirstConnect.Attempt{T}"/> sent the statement, answered in turn by
    /// <paramref name="answers"/>, each with whether its connection opened, and its outcome: the value, or the error's code.</summary>
    private static (int, string) Attempts(LocalServer.FirstConnect connect, params (Result<int> Answer, bool Opened)[] answers)
    {
        var sent = 0;
        var outcome = connect.Attempt(() => answers[Math.Min(sent++, answers.Length - 1)], TimeSpan.Zero);
        return (sent, outcome.Match(value => value.ToString(CultureInfo.InvariantCulture), error => error.Code));
    }
}
