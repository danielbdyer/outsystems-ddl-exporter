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
            allowedPath,
            snapshotPath: null,
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
            new(100, 200, null),
            new(300, 400, null)
        };
        context.SetUserMap(mappings);

        context.SetAllowedUserIds(Array.Empty<long>());
        context.SetOrphanUserIds(new[] { 100L, 300L });
        context.SetForeignKeyValueCounts(new Dictionary<UserFkColumn, IReadOnlyDictionary<long, long>>
        {
            [catalog[0]] = new Dictionary<long, long> { { 100L, 5L } },
            [catalog[1]] = new Dictionary<long, long> { { 300L, 2L } }
        });

        var script = SqlScriptEmitter.BuildScript(context);
        var updateBlocks = CountOccurrences(script, ";WITH delta AS");
        Assert.Equal(catalog.Count, updateBlocks);
        Assert.Contains("INSERT INTO #UserRemap (SourceUserId, TargetUserId, Note) VALUES", script);
        Assert.Contains("(100, 200", script);
        Assert.Contains("(300, 400", script);
        Assert.Contains("WHERE t.[CreatedBy] IS NOT NULL", script);
        Assert.Contains("WHERE t.[UpdatedBy] IS NOT NULL", script);
        Assert.DoesNotContain("IDENTITY_INSERT", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Summary:", script);
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
