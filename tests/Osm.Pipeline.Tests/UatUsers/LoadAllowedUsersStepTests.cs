using System;
using System.Data.Common;
using System.IO;
using System.Linq;
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
        Assert.Contains(context.AllowedUserIds, id => id.NumericValue == 200L);
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

        Assert.Equal(new[] { "1", "2" }, context.AllowedUserIds.Select(id => id.Value));
    }

    [Fact]
    public async Task ReadsIdentifiersFromCsvProvidedViaSqlArgument()
    {
        using var temp = new TemporaryDirectory();
        var csvPath = Path.Combine(temp.Path, "dbo.User.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "Id,Name",
            "1,A",
            "2,B"
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
            allowedUsersSqlPath: csvPath,
            allowedUserIdsPath: null,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new LoadAllowedUsersStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(new[] { "1", "2" }, context.AllowedUserIds.Select(id => id.Value));
    }

    [Fact]
    public async Task ReadsIdentifiersFromCustomSchemaAndTable()
    {
        using var temp = new TemporaryDirectory();
        var sqlPath = Path.Combine(temp.Path, "auth.AllowedUsers.sql");
        File.WriteAllText(sqlPath, @"INSERT INTO [auth].[AllowedUsers] ([UserId], [Name]) VALUES (10, 'Admin');
INSERT INTO [Auth].[AllowedUsers] ([UserId], [Name]) VALUES (11, 'Operator');
INSERT INTO [dbo].[AllowedUsers] ([UserId], [Name]) VALUES (12, 'Should be ignored');", Encoding.UTF8);

        var context = new UatUsersContext(
            new StubSchemaGraph(),
            new UatUsersArtifacts(temp.Path),
            new ThrowingConnectionFactory(),
            "auth",
            "AllowedUsers",
            "UserId",
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

        Assert.Equal(new[] { "10", "11" }, context.AllowedUserIds.Select(id => id.Value));
    }

    [Fact]
    public async Task ThrowsWhenNoAllowedUsersDiscovered()
    {
        using var temp = new TemporaryDirectory();
        var emptyListPath = Path.Combine(temp.Path, "users.csv");
        File.WriteAllLines(emptyListPath, new[] { "Id,Name" });

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
            allowedUserIdsPath: emptyListPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new LoadAllowedUsersStep();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Contains("No allowed user identifiers", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupportsGuidIdentifiers()
    {
        using var temp = new TemporaryDirectory();
        var listPath = Path.Combine(temp.Path, "users.csv");
        var guidA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var guidB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        File.WriteAllLines(listPath, new[]
        {
            "UserId",
            guidA,
            guidB
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
            allowedUserIdsPath: listPath,
            snapshotPath: null,
            userEntityIdentifier: null,
            fromLiveMetadata: false,
            sourceFingerprint: "test/db");

        var step = new LoadAllowedUsersStep();
        await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, context.AllowedUserIds.Count);
        Assert.All(context.AllowedUserIds, id => Assert.True(id.IsGuid));
        Assert.Contains(context.AllowedUserIds, id => string.Equals(id.Value, guidA, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.AllowedUserIds, id => string.Equals(id.Value, guidB, StringComparison.OrdinalIgnoreCase));
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
