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
/// The copy cache (WP 3.4): a copy backed up under a key, recorded in .dbchange/cache/backups.json with the file the server wrote, in
/// .dbchange/cache/ on LocalDB and in the server's own default backup folder on a SQL Server on this machine; restored into a new
/// registered copy equal to the copy it was backed up from, so the plan of its package against the restored copy is empty; and a fresh
/// copy of a base, published once and restored from its backup after.
/// </summary>
public sealed class CopyCacheTests : IDisposable
{
    private readonly string root = SqlServerFixture.RepositoryRoot(Path.Combine(Repository.Root, ".dbchange", "copies-under-test",
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_backup_restores_into_a_new_registered_copy_whose_plan_against_the_base_package_is_empty()
    {
        var server = await SqlServerFixture.ServerAsync();
        var dacpac = await GoldenProject.Built();
        var strict = Value(PublishProfiles.Load(GoldenProject.Profile));
        var key = Fingerprint.Of("a backup restores " + Guid.NewGuid().ToString("N"));
        using var published = DisposableCopy.Create(root, server, dacpac, strict);
        try
        {
            var backup = Value(LocalServer.BackUp(published.Copy, key, SqlServer.QueryLog.Start(root)));
            using var restored = DisposableCopy.Of(Value(LocalServer.Restore(root, server, key, SqlServer.QueryLog.Start(root))));

            var entry = Assert.Single(Index())!;
            Assert.Equal((key.ToString(), Value(LocalServer.ServerName(server)).ToString(), backup.Path),
                ((string)entry["key"]!, (string)entry["server"]!, (string)entry["path"]!));
            Assert.Equal(server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase),
                backup.Path.StartsWith(Path.Combine(root, ".dbchange", "cache"), StringComparison.Ordinal));
            Assert.NotEqual(published.Copy.Name, restored.Copy.Name);
            Assert.Equal((Environment.ProcessId, published.Copy.Name.Machine), (restored.Copy.Name.Pid, restored.Copy.Name.Machine));
            Assert.Contains(Registry(), row => (string)row!["name"]! == restored.Copy.Name.ToString());
            using var extracted = Value(DacFx.Extract(restored.Copy));
            using var package = Value(Ssdt.Open(dacpac));
            var plan = Value(DacFx.Plan(package, extracted, restored.Copy.Catalog, strict, []));
            Assert.True(plan.Report.IsEmpty, "the base package plans operations against its restored copy:\n"
                + string.Join('\n', plan.Report.Operations));
            Assert.Equal(await SqlServerFixture.ScalarAsync(published.Copy.Connection, "SELECT COUNT(*) FROM dbo.Customer;"),
                await SqlServerFixture.ScalarAsync(restored.Copy.Connection, "SELECT COUNT(*) FROM dbo.Customer;"));
        }
        finally
        {
            await SqlServerFixture.DeleteBackupsAsync(root);
        }
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_fresh_copy_of_a_base_is_published_once_and_restored_from_its_backup_after()
    {
        var server = await SqlServerFixture.ServerAsync();
        var dacpac = await GoldenProject.Built();
        var strict = Value(PublishProfiles.Load(GoldenProject.Profile));
        var key = Fingerprint.Of("a fresh copy " + Guid.NewGuid().ToString("N"));
        var publishes = 0;
        Result<SqlServer.Copy> Publish(SqlServer.Copy copy)
        {
            publishes++;
            return copy.Publish(dacpac, strict, SqlServer.QueryLog.Start(root));
        }

        try
        {
            using var first = DisposableCopy.Of(Fresh(LocalServer.FromBase(root, server, key, Publish, SqlServer.QueryLog.Start(root)),
                restored: false));
            using var second = DisposableCopy.Of(Fresh(LocalServer.FromBase(root, server, key, Publish, SqlServer.QueryLog.Start(root)),
                restored: true));

            Assert.Equal(1, publishes);
            Assert.NotEqual(first.Copy.Name, second.Copy.Name);
            Assert.Single(Index());
            Assert.Equal(await SqlServerFixture.ScalarAsync(first.Copy.Connection, "SELECT COUNT(*) FROM sys.tables;"),
                await SqlServerFixture.ScalarAsync(second.Copy.Connection, "SELECT COUNT(*) FROM sys.tables;"));
        }
        finally
        {
            await SqlServerFixture.DeleteBackupsAsync(root);
        }
    }

    /// <summary>The fresh copy, made as <paramref name="restored"/> says, with no note: the cache serves this server.</summary>
    private static SqlServer.Copy Fresh(Result<LocalServer.Fresh> made, bool restored)
    {
        var fresh = Value(made);
        Assert.Equal((restored, ""), (fresh.Restored, string.Join(" ", fresh.Notes.Select(n => n.Code + ": " + n.Message))));
        return fresh.Copy;
    }

    private JsonArray Index() => JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".dbchange", "cache", "backups.json")))!["backups"]!
        .AsArray();

    private JsonArray Registry() => JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".dbchange", "copies.json")))!["copies"]!
        .AsArray();
}
