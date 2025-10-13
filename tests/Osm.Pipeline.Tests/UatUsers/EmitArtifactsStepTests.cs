using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
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
        var context = new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            new ThrowingConnectionFactory(),
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(temp.Path, "map.csv"),
            allowedUsersSqlPath: null,
            allowedUserIdsPath: Path.Combine(temp.Path, "users.csv"),
            snapshotPath: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        context.SetAllowedUserIds(new[] { 1L, 2L });
        context.SetOrphanUserIds(new[] { 100L });

        var catalog = new List<UserFkColumn>
        {
            new("dbo", "Orders", "CreatedBy", "FK_Orders_Users")
        };
        context.SetUserFkCatalog(catalog);
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<long, long>>
        {
            [catalog[0]] = new Dictionary<long, long> { { 100L, 42L } }
        });
        context.SetUserMap(new List<UserMappingEntry> { new(100L, 200L, "reviewed") });

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
