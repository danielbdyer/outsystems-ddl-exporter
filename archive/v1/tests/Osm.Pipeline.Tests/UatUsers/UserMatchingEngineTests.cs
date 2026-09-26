using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class UserMatchingEngineTests
{
    [Fact]
    public void Execute_ReturnsEmpty_WhenNoOrphans()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path);
        context.SetAllowedUserIds(Array.Empty<UserIdentifier>());
        context.SetOrphanUserIds(Array.Empty<UserIdentifier>());

        var engine = new UserMatchingEngine();
        var results = engine.Execute(context);

        Assert.Empty(results);
    }

    [Fact]
    public void Execute_MatchesCaseInsensitiveEmail()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(temp.Path, UserMatchingStrategy.CaseInsensitiveEmail);
        var qaUser = UserIdentifier.FromString("100");
        var target = UserIdentifier.FromString("200");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { qaUser });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [qaUser] = new(qaUser, "qa-admin", "QA@example.com", null, null, null, null, null)
        });
        context.SetUatUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [target] = new(target, "uat-admin", "qa@example.com", null, null, null, null, null)
        });

        var engine = new UserMatchingEngine();
        var result = Assert.Single(engine.Execute(context));
        Assert.Equal(target, result.TargetUserId);
        Assert.False(result.UsedFallback);
        Assert.Contains("Matched email", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_MatchesExactAttribute()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(
            temp.Path,
            UserMatchingStrategy.ExactAttribute,
            matchingAttribute: "External_Id");
        var qaUser = UserIdentifier.FromString("101");
        var target = UserIdentifier.FromString("201");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { qaUser });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [qaUser] = new(qaUser, null, null, null, "QA-EXT", null, null, null)
        });
        context.SetUatUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [target] = new(target, null, null, null, "QA-EXT", null, null, null)
        });

        var engine = new UserMatchingEngine();
        var result = Assert.Single(engine.Execute(context));
        Assert.Equal(target, result.TargetUserId);
        Assert.Equal("ExactAttribute", result.Strategy);
        Assert.Contains("Matched External_Id", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_MatchesRegexCapture()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(
            temp.Path,
            UserMatchingStrategy.Regex,
            matchingAttribute: "Username",
            matchingRegexPattern: "^qa_(?<target>.*)$");
        var qaUser = UserIdentifier.FromString("102");
        var target = UserIdentifier.FromString("202");
        context.SetAllowedUserIds(new[] { target });
        context.SetOrphanUserIds(new[] { qaUser });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [qaUser] = new(qaUser, "qa_target", null, null, null, null, null, null)
        });
        context.SetUatUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [target] = new(target, "target", null, null, null, null, null, null)
        });

        var engine = new UserMatchingEngine();
        var result = Assert.Single(engine.Execute(context));
        Assert.Equal(target, result.TargetUserId);
        Assert.False(result.UsedFallback);
        Assert.Contains("Regex", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AssignsFallbackRoundRobin()
    {
        using var temp = new TemporaryDirectory();
        var fallbackTargets = new[]
        {
            UserIdentifier.FromString("300"),
            UserIdentifier.FromString("301")
        };
        var context = CreateContext(
            temp.Path,
            UserMatchingStrategy.ExactAttribute,
            matchingAttribute: "External_Id",
            fallbackMode: UserFallbackAssignmentMode.RoundRobin,
            fallbackTargets: fallbackTargets);
        var orphanA = UserIdentifier.FromString("400");
        var orphanB = UserIdentifier.FromString("401");
        context.SetAllowedUserIds(fallbackTargets);
        context.SetOrphanUserIds(new[] { orphanA, orphanB });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [orphanA] = new(orphanA, null, null, null, null, null, null, null),
            [orphanB] = new(orphanB, null, null, null, null, null, null, null)
        });
        context.SetUatUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [fallbackTargets[0]] = new(fallbackTargets[0], null, null, null, null, null, null, null),
            [fallbackTargets[1]] = new(fallbackTargets[1], null, null, null, null, null, null, null)
        });

        var engine = new UserMatchingEngine();
        var results = engine.Execute(context).OrderBy(result => result.SourceUserId).ToArray();

        Assert.All(results, result => Assert.True(result.UsedFallback));
        Assert.Contains(results, result => result.TargetUserId == fallbackTargets[0]);
        Assert.Contains(results, result => result.TargetUserId == fallbackTargets[1]);
        Assert.All(results, result => Assert.Equal("RoundRobin", result.Strategy));
    }

    [Fact]
    public void Execute_DoesNotAssignFallbackWhenTargetOutsideRoster()
    {
        using var temp = new TemporaryDirectory();
        var context = CreateContext(
            temp.Path,
            UserMatchingStrategy.CaseInsensitiveEmail,
            fallbackMode: UserFallbackAssignmentMode.SingleTarget,
            fallbackTargets: new[] { UserIdentifier.FromString("999") });
        var orphan = UserIdentifier.FromString("777");
        context.SetAllowedUserIds(Array.Empty<UserIdentifier>());
        context.SetOrphanUserIds(new[] { orphan });
        context.SetQaUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>
        {
            [orphan] = new(orphan, "qa", null, null, null, null, null, null)
        });
        context.SetUatUserInventory(new Dictionary<UserIdentifier, UserInventoryRecord>());

        var engine = new UserMatchingEngine();
        var result = Assert.Single(engine.Execute(context));
        Assert.Null(result.TargetUserId);
        Assert.False(result.UsedFallback);
        Assert.Contains("not part of the allowed", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static UatUsersContext CreateContext(
        string root,
        UserMatchingStrategy strategy = UserMatchingStrategy.CaseInsensitiveEmail,
        string? matchingAttribute = null,
        string? matchingRegexPattern = null,
        UserFallbackAssignmentMode fallbackMode = UserFallbackAssignmentMode.Ignore,
        IEnumerable<UserIdentifier>? fallbackTargets = null)
    {
        var artifacts = new UatUsersArtifacts(root);
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
            matchingStrategy: strategy,
            matchingAttribute: matchingAttribute,
            matchingRegexPattern: matchingRegexPattern,
            fallbackAssignment: fallbackMode,
            fallbackTargets: fallbackTargets);
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForeignKeyDefinition>>(Array.Empty<ForeignKeyDefinition>());
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Connection factory should not be used in tests.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uat-users-matching", Guid.NewGuid().ToString("N"));
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
                // ignore cleanup issues
            }
        }
    }
}
