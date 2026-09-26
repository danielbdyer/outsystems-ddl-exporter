using System;
using System.IO;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using Microsoft.Data.SqlClient;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// WP 1.8's read-only principal: VIEW DEFINITION at the database's scope and db_datareader, nothing server-wide beyond
/// CONNECT SQL, reached only through a file: reference under .dbchange/, and dropped with its database.
/// </summary>
public sealed class ReadOnlyPrincipalTests
{
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_read_only_principal_holds_VIEW_DEFINITION_and_db_datareader_and_nothing_server_wide_beyond_CONNECT_SQL()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, "CREATE TABLE dbo.Proof (Id INT NOT NULL PRIMARY KEY); INSERT dbo.Proof (Id) VALUES (1);");
        var reader = await ReadOnlyPrincipal.CreateAsync(database);

        Assert.StartsWith("file:.dbchange/", reader.Reference, StringComparison.Ordinal);
        Assert.Equal(1, await Held(database, reader, "SELECT COUNT(*) FROM sys.server_permissions WHERE grantee_principal_id = SUSER_ID(@name) AND permission_name = N'CONNECT SQL' AND state = 'G';"));
        Assert.Equal(0, await Held(database, reader, "SELECT COUNT(*) FROM sys.server_permissions WHERE grantee_principal_id = SUSER_ID(@name) AND NOT (permission_name = N'CONNECT SQL' AND state = 'G');"));
        Assert.Equal(0, await Held(database, reader, "SELECT COUNT(*) FROM sys.server_role_members WHERE member_principal_id = SUSER_ID(@name);"));
        Assert.Equal(1, await Held(database, reader, "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(@name) AND class = 0 AND state = 'G' AND permission_name = N'VIEW DEFINITION';"));
        Assert.Equal(0, await Held(database, reader, "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(@name) AND NOT (class = 0 AND state = 'G' AND permission_name IN (N'CONNECT', N'VIEW DEFINITION'));"));
        Assert.Equal(1, await Held(database, reader, "SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id = DATABASE_PRINCIPAL_ID(@name) AND role_principal_id = DATABASE_PRINCIPAL_ID(N'db_datareader');"));
        Assert.Equal(0, await Held(database, reader, "SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id = DATABASE_PRINCIPAL_ID(@name) AND role_principal_id <> DATABASE_PRINCIPAL_ID(N'db_datareader');"));

        Assert.Equal(1, await SqlServerFixture.ScalarAsync(reader.ConnectionString, "SELECT COUNT(*) FROM dbo.Proof;"));
        Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ExecuteAsync(reader.ConnectionString, "INSERT dbo.Proof (Id) VALUES (2);"))).Number);
        Assert.Equal(262, (await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ExecuteAsync(reader.ConnectionString, "CREATE TABLE dbo.Written (Id INT);"))).Number);
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_read_only_principal_its_login_and_its_file_are_dropped_with_its_database()
    {
        var database = await SqlServerFixture.RegisterAsync();
        var reader = await ReadOnlyPrincipal.CreateAsync(database);
        var file = Path.Combine(Repository.Root, reader.Reference["file:".Length..]);
        Assert.True(await SqlServerFixture.LoginExistsAsync(reader.Login));
        Assert.True(File.Exists(file));

        // A session of the login's in master, which dropping the database does not end: the drop ends it, then drops the login.
        await using var held = new SqlConnection(reader.ConnectionString);
        await held.OpenAsync();
        await using (var use = new SqlCommand("USE master;", held))
        {
            await use.ExecuteNonQueryAsync();
        }

        await database.DisposeAsync();

        Assert.False(await SqlServerFixture.LoginExistsAsync(reader.Login), reader.Login + " outlived its database");
        Assert.False(File.Exists(file), reader.Reference + " outlived its database");
    }

    private static Task<int> Held(RegisteredDatabase database, ReadOnlyPrincipal reader, string sql) => SqlServerFixture.ScalarAsync(database.ConnectionString, sql, reader.Login);
}
