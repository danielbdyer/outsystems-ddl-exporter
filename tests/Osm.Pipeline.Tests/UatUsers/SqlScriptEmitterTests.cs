using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        var context = new UatUsersContext(
            new StubSchemaGraph(),
            artifacts,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(directory, "map.csv"),
            fromLiveMetadata: false);

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

        var script = SqlScriptEmitter.BuildScript(context);
        var updateBlocks = CountOccurrences(script, ";WITH delta AS");
        Assert.Equal(catalog.Count, updateBlocks);
        Assert.Contains("INSERT INTO #UserRemap (SourceUserId, TargetUserId) VALUES", script);
        Assert.Contains("(100, 200)", script);
        Assert.Contains("(300, 400)", script);
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
}
