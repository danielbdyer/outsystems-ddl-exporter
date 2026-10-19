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
        context.SetAllowedUserIds(new[]
        {
            UserIdentifier.FromString("1"),
            UserIdentifier.FromString("2"),
            UserIdentifier.FromString("3")
        });
        context.SetOrphanUserIds(new[] { UserIdentifier.FromString("100"), UserIdentifier.FromString("200") });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>());
        context.SetAutomaticMappings(new[]
        {
            new UserMappingEntry(
                UserIdentifier.FromString("200"),
                UserIdentifier.FromString("400"),
                "auto-match")
        });

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
                Assert.Equal("100", entry.SourceUserId.Value);
                Assert.Equal("300", entry.TargetUserId?.Value);
                Assert.Equal("existing", entry.Rationale);
            },
            entry =>
            {
                Assert.Equal("200", entry.SourceUserId.Value);
                Assert.Equal("400", entry.TargetUserId?.Value);
                Assert.Equal("auto-match", entry.Rationale);
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
            "200,400,auto-match"
        }, mapLines);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRewriteUserMapWhenIdempotentEmissionEnabled()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path, idempotentEmission: true);
        context.SetUserFkCatalog(new List<UserFkColumn>());
        context.SetAllowedUserIds(new[]
        {
            UserIdentifier.FromString("1"),
            UserIdentifier.FromString("2")
        });
        context.SetOrphanUserIds(new[] { UserIdentifier.FromString("100") });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>());
        context.SetAutomaticMappings(Array.Empty<UserMappingEntry>());

        var step = new PrepareUserMapStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        var mapPath = context.UserMapPath!;
        var baseline = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(mapPath, baseline);

        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(baseline, File.GetLastWriteTimeUtc(mapPath));
    }

    private static UatUsersContext CreateContext(string root, bool idempotentEmission = false)
    {
        var artifacts = new UatUsersArtifacts(root, idempotentEmission);
        var connectionFactory = new ThrowingConnectionFactory();
        var uatInventoryPath = Path.Combine(root, "uat.csv");
        File.WriteAllText(uatInventoryPath, "Id,Username\n1,uat\n");
        var qaInventoryPath = Path.Combine(root, "qa.csv");
        File.WriteAllText(qaInventoryPath, "Id,Username\n1,qa\n");

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
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db",
            idempotentEmission: idempotentEmission);
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
