using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.UatUsers.Steps;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class DiscoverUserFkCatalogStepTests
{
    [Fact]
    public async Task DiscoversAndDeduplicatesCatalog()
    {
        using var temp = new TemporaryDirectory();
        var connectionFactory = new ThrowingConnectionFactory();
        var schemaGraph = new StubSchemaGraph(new[]
        {
            new ForeignKeyDefinition(
                "FK_TableA",
                new ForeignKeyTable("dbo", "TableA"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("CreatedBy", "Id"))),
            new ForeignKeyDefinition(
                "FK_TableA_Duplicate",
                new ForeignKeyTable("dbo", "TableA"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("CreatedBy", "Id"))),
            new ForeignKeyDefinition(
                "FK_TableB",
                new ForeignKeyTable("dbo", "TableB"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("UpdatedBy", "Id"))),
            new ForeignKeyDefinition(
                "FK_TableC",
                new ForeignKeyTable("dbo", "TableC"),
                new ForeignKeyTable("dbo", "SomethingElse"),
                ImmutableArray.Create(new ForeignKeyColumn("OwnerId", "Id"))),
            new ForeignKeyDefinition(
                "FK_TableD",
                new ForeignKeyTable("dbo", "TableD"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("Legacy", "UserGuid")))
        });

        var artifacts = new UatUsersArtifacts(temp.Path);
        var allowedPath = Path.Combine(temp.Path, "allowed.csv");
        var context = new UatUsersContext(
            schemaGraph,
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(temp.Path, "map.csv"),
            allowedPath,
            snapshotPath: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new DiscoverUserFkCatalogStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Collection(
            context.UserFkCatalog,
            column =>
            {
                Assert.Equal("dbo", column.SchemaName);
                Assert.Equal("TableA", column.TableName);
                Assert.Equal("CreatedBy", column.ColumnName);
                Assert.Equal("FK_TableA", column.ForeignKeyName);
            },
            column =>
            {
                Assert.Equal("dbo", column.SchemaName);
                Assert.Equal("TableB", column.TableName);
                Assert.Equal("UpdatedBy", column.ColumnName);
                Assert.Equal("FK_TableB", column.ForeignKeyName);
            });

        var catalogPath = Path.Combine(temp.Path, "uat-users", "03_catalog.txt");
        Assert.True(File.Exists(catalogPath));
        var lines = File.ReadAllLines(catalogPath);
        Assert.Equal(new[]
        {
            "dbo.TableA.CreatedBy  -- FK_TableA",
            "dbo.TableB.UpdatedBy  -- FK_TableB"
        }, lines);
    }

    [Fact]
    public async Task IncludeColumnsFiltersCatalog()
    {
        using var temp = new TemporaryDirectory();
        var connectionFactory = new ThrowingConnectionFactory();
        var schemaGraph = new StubSchemaGraph(new[]
        {
            new ForeignKeyDefinition(
                "FK_TableA",
                new ForeignKeyTable("dbo", "TableA"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("CreatedBy", "Id"))),
            new ForeignKeyDefinition(
                "FK_TableB",
                new ForeignKeyTable("dbo", "TableB"),
                new ForeignKeyTable("dbo", "User"),
                ImmutableArray.Create(new ForeignKeyColumn("UpdatedBy", "Id")))
        });

        var artifacts = new UatUsersArtifacts(temp.Path);
        var allowedPath = Path.Combine(temp.Path, "allowed.csv");
        var context = new UatUsersContext(
            schemaGraph,
            artifacts,
            connectionFactory,
            "dbo",
            "User",
            "Id",
            includeColumns: new[] { "UpdatedBy" },
            Path.Combine(temp.Path, "map.csv"),
            allowedPath,
            snapshotPath: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new DiscoverUserFkCatalogStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.UserFkCatalog);
        Assert.Equal("UpdatedBy", context.UserFkCatalog[0].ColumnName);
    }

    private sealed class StubSchemaGraph : IUserSchemaGraph
    {
        private readonly IReadOnlyList<ForeignKeyDefinition> _definitions;

        public StubSchemaGraph(IReadOnlyList<ForeignKeyDefinition> definitions)
        {
            _definitions = definitions;
        }

        public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_definitions);
        }
    }

    private sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection factory should not be used in this test.");
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
                // Ignore cleanup failures in tests.
            }
        }
    }
}
