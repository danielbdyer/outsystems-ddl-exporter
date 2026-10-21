using System;
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
/// What io/LocalServer checks on every path that makes a copy or resolves one, beside synthetic-copy up's own: R15 over the local
/// server's linked servers (DECISIONS.md, 2026-10-21), and a CREATE DATABASE that never takes over a database of the copy's name that
/// the registry did not make.
/// </summary>
public sealed class CopyMakeTests : IDisposable
{
    private readonly string root = SqlServerFixture.RepositoryRoot(Path.Combine(Repository.Root, ".dbchange", "copies-under-test",
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>
    /// dbchange/environments.json names an environment on a host of its own, and the local server holds a linked server whose data
    /// source is that host. A copy made through LocalServer.Create, the path every verb but synthetic-copy up takes, is copy.named-host,
    /// naming the linked server, its log holding the one read of sys.servers and its registry no new row; and copy: naming a copy made
    /// before the linked server was added no longer resolves, as copy.named-host too. With the linked server dropped, the copy resolves.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P1")]
    public async Task A_copy_made_or_named_on_a_local_server_linked_to_a_named_environment_s_host_is_refused()
    {
        var server = await SqlServerFixture.ServerAsync();
        var host = "dbchange-linked-" + Guid.NewGuid().ToString("N")[..8] + ".invalid";
        async Task Linked(bool linked) => await SqlServerFixture.ExecuteAsync(server, linked
            ? "EXEC sp_addlinkedserver @server = @name, @srvproduct = N'SQL Server';"
            : "IF EXISTS (SELECT 1 FROM sys.servers WHERE name = @name) EXEC sp_dropserver @server = @name, @droplogins = 'droplogins';",
            host);
        var made = Value(LocalServer.Create(root, server, SqlServer.QueryLog.Start(root)));
        var named = Value(SqlServer.Target("copy:" + made.Name, "--target"));
        try
        {
            EnvironmentsJson.Dev("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), host).WriteTo(root);
            await Linked(true);
            var log = SqlServer.QueryLog.Start(root);

            var refused = Failed(LocalServer.Create(root, server, log), "copy.named-host");
            var unresolved = Failed(SqlServer.Resolve(named, root, SqlServer.QueryLog.Start(root)), "copy.named-host");

            Assert.StartsWith("The local server holds the linked server [" + host + "], which names " + host, refused.Message,
                StringComparison.Ordinal);
            Assert.Equal(refused.Message, unresolved.Message);
            var entries = File.ReadAllText(log.Path);
            Assert.Equal(1, entries.Split("\nGO\n").Length - 1);
            Assert.Contains(" linked servers: ", entries, StringComparison.Ordinal);
            Assert.Equal([made.Name.ToString()], Registry().Select(r => (string)r["name"]!));
            await Linked(false);
            Assert.Equal(made.Name, ((SqlServer.Copy)Value(SqlServer.Resolve(named, root, SqlServer.QueryLog.Start(root)))).Name);
        }
        finally
        {
            await Linked(false);
            File.Delete(Path.Combine(root, "dbchange", "environments.json"));
            Value(LocalServer.Drop(made, SqlServer.QueryLog.Start(root)));
        }
    }

    /// <summary>
    /// A run's copy takes a name no database holds, but one that does is not taken over: CREATE DATABASE runs with no guard, so SQL
    /// Server refuses the name with Msg 1801, the run fails as server.failed, its row is removed, and the database standing under the
    /// name, which this registry did not make, is neither registered nor dropped.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_whose_name_a_database_the_registry_did_not_make_already_holds_is_refused_and_leaves_that_database_standing()
    {
        var server = await SqlServerFixture.ServerAsync();
        var name = CopyName.Make(Environment.MachineName, Environment.ProcessId, BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0));
        await SqlServerFixture.ExecuteAsync(server,
            "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);", name.ToString());
        try
        {
            var refused = Failed(LocalServer.Create(root, server, SqlServer.QueryLog.Start(root), named: name), "server.failed");

            Assert.Contains("Msg 1801", refused.Message, StringComparison.Ordinal);
            Assert.True(await SqlServerFixture.ExistsAsync(name.ToString()), name + " was dropped by the copy that could not take its name");
            Assert.Empty(Registry());
        }
        finally
        {
            await SqlServerFixture.DropAsync(server, name.ToString());
        }
    }

    private JsonObject[] Registry()
    {
        var path = Path.Combine(root, ".dbchange", "copies.json");
        return File.Exists(path) ? [.. JsonNode.Parse(File.ReadAllText(path))!["copies"]!.AsArray().Select(r => r!.AsObject())] : [];
    }
}
