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

public sealed class EmitArtifactsStepTests
{
    [Fact]
    public async Task WritesPreviewWithRowCounts()
    {
        using var temp = new TemporaryDirectory();
        var artifacts = new UatUsersArtifacts(temp.Path);
        var uatInventoryPath = Path.Combine(temp.Path, "uat.csv");
        File.WriteAllText(uatInventoryPath, "Id,Username\n1,uat\n2,uat2\n");
        var qaInventoryPath = Path.Combine(temp.Path, "qa.csv");
        File.WriteAllText(qaInventoryPath, "Id,Username\n1,qa\n2,qa2\n");
        var context = new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            new ThrowingConnectionFactory(),
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(temp.Path, "map.csv"),
            uatInventoryPath,
            qaInventoryPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        context.SetAllowedUserIds(new[] { UserIdentifier.FromString("1"), UserIdentifier.FromString("2") });
        context.SetOrphanUserIds(new[] { UserIdentifier.FromString("100") });

        var catalog = new List<UserFkColumn>
        {
            new("dbo", "Orders", "CreatedBy", "FK_Orders_Users_CreatedBy")
        };
        context.SetUserFkCatalog(catalog);
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>
        {
            [catalog[0]] = new Dictionary<UserIdentifier, long> { { UserIdentifier.FromString("100"), 42L } }
        });
        context.SetUserMap(new List<UserMappingEntry> { new(UserIdentifier.FromString("100"), UserIdentifier.FromString("200"), "reviewed") });

        var step = new EmitArtifactsStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        var previewPath = Path.Combine(temp.Path, "uat-users", "01_preview.csv");
        var lines = File.ReadAllLines(previewPath);
        Assert.Equal(new[]
        {
            "TableName,ColumnName,OldUserId,NewUserId,RowCount",
            "dbo.Orders,CreatedBy,100,200,42"
        }, lines);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uat-users-tests", Guid.NewGuid().ToString("N"));
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
                // ignore cleanup issues in tests.
            }
        }
    }
}
