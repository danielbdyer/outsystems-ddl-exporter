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

public sealed class AnalyzeForeignKeyValuesStepTests
{
    [Fact]
    public async Task CollectsCountsAndIdentifiesOrphans()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);

        var catalog = new List<UserFkColumn>
        {
            new("dbo", "TableA", "CreatedBy", "FK_TableA"),
            new("dbo", "TableB", "UpdatedBy", "FK_TableB")
        };
        context.SetUserFkCatalog(catalog);
        context.SetAllowedUserIds(new[]
        {
            UserIdentifier.FromString("1"),
            UserIdentifier.FromString("2"),
            UserIdentifier.FromString("200")
        });

        var counts = new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>
        {
            [catalog[0]] = new Dictionary<UserIdentifier, long>
            {
                [UserIdentifier.FromString("1")] = 10L,
                [UserIdentifier.FromString("100")] = 3L
            },
            [catalog[1]] = new Dictionary<UserIdentifier, long>
            {
                [UserIdentifier.FromString("300")] = 5L,
                [UserIdentifier.FromString("2")] = 7L
            }
        };

        var provider = new StubValueProvider(counts);
        var snapshotStore = new StubSnapshotStore();
        var step = new AnalyzeForeignKeyValuesStep(provider, snapshotStore);

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, context.OrphanUserIds.Count);
        Assert.Contains(context.OrphanUserIds, id => id.NumericValue == 100L);
        Assert.Contains(context.OrphanUserIds, id => id.NumericValue == 300L);
        Assert.True(context.ForeignKeyValueCounts.TryGetValue(catalog[0], out var tableACounts));
        Assert.Equal(3L, tableACounts[UserIdentifier.FromString("100")]);
        Assert.NotNull(snapshotStore.LastSaved);
    }

    [Fact]
    public async Task LoadsSnapshotWhenAvailable()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        var catalog = new List<UserFkColumn>
        {
            new("dbo", "TableA", "CreatedBy", "FK_TableA")
        };
        context.SetUserFkCatalog(catalog);
        context.SetAllowedUserIds(new[] { UserIdentifier.FromString("1") });

        var snapshot = new UserForeignKeySnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            SourceFingerprint = context.SourceFingerprint,
            AllowedUserIds = new[] { UserIdentifier.FromString("1") },
            OrphanUserIds = new[] { UserIdentifier.FromString("100") },
            Columns = new[]
            {
                new UserForeignKeySnapshotColumn
                {
                    Schema = "dbo",
                    Table = "TableA",
                    Column = "CreatedBy",
                    Values = new[]
                    {
                        new UserForeignKeySnapshotValue { UserId = UserIdentifier.FromString("100"), RowCount = 4L }
                    }
                }
            }
        };

        var snapshotStore = new StubSnapshotStore { NextSnapshot = snapshot };
        var step = new AnalyzeForeignKeyValuesStep(new ThrowingValueProvider(), snapshotStore);

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.OrphanUserIds, id => id.NumericValue == 100L);
        Assert.True(context.ForeignKeyValueCounts[catalog[0]].ContainsKey(UserIdentifier.FromString("100")));
        Assert.Null(snapshotStore.LastSaved);
    }

    private static UatUsersContext CreateContext(string root)
    {
        var artifacts = new UatUsersArtifacts(root);
        var connectionFactory = new ThrowingConnectionFactory();
        var uatInventoryPath = Path.Combine(root, "uat_users.csv");
        File.WriteAllLines(uatInventoryPath, new[] { "Id,Username", "1,uat-user", "2,uat-admin" });
        var qaInventoryPath = Path.Combine(root, "qa.csv");
        File.WriteAllText(qaInventoryPath, "Id,Username\n1,qa-user\n2,qa-admin\n");

        return new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(root, "map.csv"),
            uatInventoryPath,
            qaInventoryPath,
            snapshotPath: Path.Combine(root, "snapshot.json"),
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");
    }

    private sealed class StubValueProvider : IUserForeignKeyValueProvider
    {
        private readonly IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> _counts;

        public StubValueProvider(IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts)
        {
            _counts = counts;
        }

        public Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
            IReadOnlyList<UserFkColumn> catalog,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_counts);
        }
    }

    private sealed class ThrowingValueProvider : IUserForeignKeyValueProvider
    {
        public Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
            IReadOnlyList<UserFkColumn> catalog,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Provider should not be invoked when loading snapshot.");
        }
    }

    private sealed class StubSnapshotStore : IUserForeignKeySnapshotStore
    {
        public UserForeignKeySnapshot? NextSnapshot { get; set; }

        public UserForeignKeySnapshot? LastSaved { get; private set; }

        public Task<UserForeignKeySnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextSnapshot);
        }

        public Task SaveAsync(string path, UserForeignKeySnapshot snapshot, CancellationToken cancellationToken)
        {
            LastSaved = snapshot;
            return Task.CompletedTask;
        }
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
                // Ignore cleanup issues in tests.
            }
        }
    }
}
