using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.UatUsers.Steps;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class LoadUatUserInventoryStepTests
{
    [Fact]
    public async Task ExecuteAsync_LoadsAllowedUsers()
    {
        using var temp = new TemporaryDirectory();
        var uatInventoryPath = Path.Combine(temp.Path, "uat_users.csv");
        File.WriteAllLines(uatInventoryPath, new[]
        {
            "Id,Username",
            "100,uat-admin",
            "200,uat-owner"
        });

        var qaInventoryPath = Path.Combine(temp.Path, "qa_users.csv");
        File.WriteAllLines(qaInventoryPath, new[]
        {
            "Id,Username",
            "900,qa-admin"
        });

        var context = CreateContext(temp.Path, uatInventoryPath, qaInventoryPath);
        var step = new LoadUatUserInventoryStep();

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, context.AllowedUserIds.Count);
        Assert.Contains(UserIdentifier.FromString("100"), context.AllowedUserIds);
        Assert.Contains(UserIdentifier.FromString("200"), context.AllowedUserIds);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenInventoryEmpty()
    {
        using var temp = new TemporaryDirectory();
        var uatInventoryPath = Path.Combine(temp.Path, "uat_users.csv");
        File.WriteAllText(uatInventoryPath, "Id,Username\n");

        var qaInventoryPath = Path.Combine(temp.Path, "qa_users.csv");
        File.WriteAllText(qaInventoryPath, "Id,Username\n1,qa\n");

        var context = CreateContext(temp.Path, uatInventoryPath, qaInventoryPath);
        var step = new LoadUatUserInventoryStep();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Contains("did not contain any identifiers", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static UatUsersContext CreateContext(string root, string uatInventoryPath, string qaInventoryPath)
    {
        var artifacts = new UatUsersArtifacts(root);
        var mapPath = Path.Combine(root, "map.csv");
        File.WriteAllText(mapPath, "SourceUserId,TargetUserId\n");

        return new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            new ThrowingConnectionFactory(),
            userSchema: "dbo",
            userTable: "Users",
            userIdColumn: "Id",
            includeColumns: null,
            userMapPath: mapPath,
            uatUserInventoryPath: uatInventoryPath,
            qaUserInventoryPath: qaInventoryPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "uat/db");
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForeignKeyDefinition>>(Array.Empty<ForeignKeyDefinition>());
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection factory should not be used in these tests.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "load-uat-inventory-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }
}
