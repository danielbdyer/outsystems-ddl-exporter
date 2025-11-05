using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class SqlScriptEmitterTests
{
    [Fact]
    public void EmitsOneUpdateBlockPerCatalogEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uat-users-tests", "emitter");
        Directory.CreateDirectory(directory);
        var artifacts = new UatUsersArtifacts(directory);
        var connectionFactory = new ThrowingConnectionFactory();
        var allowedPath = Path.Combine(directory, "allowed.csv");
        var context = new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(directory, "map.csv"),
            allowedUsersSqlPath: null,
            allowedUserIdsPath: allowedPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var catalog = new List<UserFkColumn>
        {
            new("dbo", "TableA", "CreatedBy", "FK_TableA"),
            new("dbo", "TableB", "UpdatedBy", "FK_TableB")
        };
        context.SetUserFkCatalog(catalog);

        var mappings = new List<UserMappingEntry>
        {
            new(UserIdentifier.FromString("100"), UserIdentifier.FromString("200"), null),
            new(UserIdentifier.FromString("300"), UserIdentifier.FromString("400"), null)
        };
        context.SetUserMap(mappings);

        context.SetAllowedUserIds(Array.Empty<UserIdentifier>());
        context.SetOrphanUserIds(new[] { UserIdentifier.FromString("100"), UserIdentifier.FromString("300") });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>
        {
            [catalog[0]] = new Dictionary<UserIdentifier, long> { { UserIdentifier.FromString("100"), 5L } },
            [catalog[1]] = new Dictionary<UserIdentifier, long> { { UserIdentifier.FromString("300"), 2L } }
        });

        var script = SqlScriptEmitter.BuildScript(context);
        var updateBlocks = CountOccurrences(script, ";WITH delta AS");
        Assert.Equal(catalog.Count, updateBlocks);
        Assert.Contains("INSERT INTO #UserRemap VALUES", script);
        Assert.Contains("(100, 200", script);
        Assert.Contains("(300, 400", script);
        Assert.Contains("WHERE t.[CreatedBy] IS NOT NULL", script);
        Assert.Contains("WHERE t.[UpdatedBy] IS NOT NULL", script);
        Assert.DoesNotContain("IDENTITY_INSERT", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-- Inputs hash:", script);

        var sanityIndex = script.IndexOf("IF EXISTS (SELECT 1 FROM #UserRemap", StringComparison.Ordinal);
        var changesIndex = script.IndexOf("CREATE TABLE #Changes", StringComparison.Ordinal);
        Assert.True(sanityIndex >= 0 && changesIndex >= 0 && sanityIndex < changesIndex, "Sanity check should precede #Changes creation.");
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitsUniqueIdentifierScriptWhenGuidsPresent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uat-users-tests", "emitter-guids");
        Directory.CreateDirectory(directory);
        var artifacts = new UatUsersArtifacts(directory);
        var connectionFactory = new ThrowingConnectionFactory();
        var sourceId = UserIdentifier.FromString("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var allowedPath = Path.Combine(directory, "allowed.csv");
        File.WriteAllText(allowedPath, $"Id\n{sourceId}\n");
        var context = new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(directory, "map.csv"),
            allowedUsersSqlPath: null,
            allowedUserIdsPath: allowedPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var catalog = new List<UserFkColumn>
        {
            new("dbo", "TableA", "CreatedBy", "FK_TableA")
        };
        context.SetUserFkCatalog(catalog);

        var targetId = UserIdentifier.FromString("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        context.SetUserMap(new List<UserMappingEntry>
        {
            new(sourceId, targetId, "reviewed")
        });

        context.SetAllowedUserIds(new[] { sourceId });
        context.SetOrphanUserIds(new[] { sourceId });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>
        {
            [catalog[0]] = new Dictionary<UserIdentifier, long>
            {
                [sourceId] = 3L
            }
        });

        var script = SqlScriptEmitter.BuildScript(context);
        Assert.Contains("SourceUserId UNIQUEIDENTIFIER", script);
        Assert.Contains("TargetUserId UNIQUEIDENTIFIER", script);
        Assert.Contains("OldUserId UNIQUEIDENTIFIER", script);
        Assert.Contains("('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',", script);
        Assert.Contains("'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',", script);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += value.Length;
        }
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ForeignKeyDefinition>>(ImmutableArray<ForeignKeyDefinition>.Empty);
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection factory should not be used in this test.");
        }
    }
}
