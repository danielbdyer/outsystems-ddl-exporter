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

public sealed class PrepareUserMapStepTests
{
    [Fact]
    public async Task GeneratesTemplateAndAugmentsMap()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        context.SetUserFkCatalog(new List<UserFkColumn>());
        context.SetAllowedUserIds(new[] { 1L, 2L, 3L });
        context.SetOrphanUserIds(new[] { 100L, 200L });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<long, long>>());

        var mapPath = context.UserMapPath;
        File.WriteAllLines(mapPath, new[]
        {
            "SourceUserId,TargetUserId,Rationale",
            "100,300,existing",
            "999,123,should be removed"
        });

        var step = new PrepareUserMapStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Collection(
            context.UserMap,
            entry =>
            {
                Assert.Equal(100L, entry.SourceUserId);
                Assert.Equal<long?>(300L, entry.TargetUserId);
                Assert.Equal("existing", entry.Rationale);
            },
            entry =>
            {
                Assert.Equal(200L, entry.SourceUserId);
                Assert.Null(entry.TargetUserId);
                Assert.Null(entry.Rationale);
            });

        var templatePath = Path.Combine(temp.Path, "uat-users", "00_user_map.template.csv");
        var template = File.ReadAllLines(templatePath);
        Assert.Equal(new[]
        {
            "SourceUserId,TargetUserId,Rationale",
            "100,,",
            "200,,"
        }, template);

        var mapLines = File.ReadAllLines(mapPath);
        Assert.Equal(new[]
        {
            "SourceUserId,TargetUserId,Rationale",
            "100,300,existing",
            "200,,"
        }, mapLines);
    }

    private static UatUsersContext CreateContext(string root)
    {
        var artifacts = new UatUsersArtifacts(root);
        var connectionFactory = new ThrowingConnectionFactory();
        var allowedPath = Path.Combine(root, "users.csv");
        File.WriteAllText(allowedPath, "Id\n1\n");

        return new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(root, "map.csv"),
            allowedUsersSqlPath: null,
            allowedUserIdsPath: allowedPath,
            snapshotPath: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");
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
