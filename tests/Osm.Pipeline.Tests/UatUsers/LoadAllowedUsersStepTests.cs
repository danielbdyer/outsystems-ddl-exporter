using System;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.UatUsers.Steps;
using Xunit;

namespace Osm.Pipeline.Tests.UatUsers;

public sealed class LoadAllowedUsersStepTests
{
    [Fact]
    public async Task ReadsIdentifiersFromCsv()
    {
        using var temp = new TemporaryDirectory();
        var allowedPath = Path.Combine(temp.Path, "users.csv");
        File.WriteAllLines(allowedPath, new[]
        {
            "Id,Name",
            "1,A",
            "2,B",
            "200,C"
        });

        var context = new UatUsersContext(
            new StubSchemaGraph(),
            new UatUsersArtifacts(temp.Path),
            new ThrowingConnectionFactory(),
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(temp.Path, "map.csv"),
            allowedUsersSqlPath: null,
            allowedUserIdsPath: allowedPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new LoadAllowedUsersStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(3, context.AllowedUserIds.Count);
        Assert.Contains(200L, context.AllowedUserIds);
    }

    [Fact]
    public async Task ReadsIdentifiersFromSqlSeedScript()
    {
        using var temp = new TemporaryDirectory();
        var sqlPath = Path.Combine(temp.Path, "dbo.User.sql");
        File.WriteAllText(sqlPath, @"SET IDENTITY_INSERT [dbo].[User] ON;
INSERT INTO [dbo].[User] ([Id], [Name]) VALUES (1, 'Admin'), (2, 'Operator');
SET IDENTITY_INSERT [dbo].[User] OFF;", Encoding.UTF8);

        var context = new UatUsersContext(
            new StubSchemaGraph(),
            new UatUsersArtifacts(temp.Path),
            new ThrowingConnectionFactory(),
            "dbo",
            "User",
            "Id",
            includeColumns: null,
            Path.Combine(temp.Path, "map.csv"),
            allowedUsersSqlPath: sqlPath,
            allowedUserIdsPath: null,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new LoadAllowedUsersStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(new[] { 1L, 2L }, context.AllowedUserIds);
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
                // ignore
            }
        }
    }
}
