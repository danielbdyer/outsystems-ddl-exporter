using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
/// and restores it.
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
