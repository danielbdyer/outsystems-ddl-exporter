using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.Data.SqlClient;

namespace DbChange.Io.Tests;

/// <summary>
/// The read-only principal (V3_MILESTONES.md WP 1.8, R14) for a registered database: a SQL login and its user holding only
/// VIEW DEFINITION at the database's scope and db_datareader, nothing server-wide beyond the CONNECT SQL a login carries.
/// Its connection string lives only in a file under .dbchange/principals/ (ignored), reached through a file: reference, which
/// io/SqlServer's own reading of a reference resolves; the password is generated here, per principal, and never printed. The login
/// is named for its database, and dropping the database drops the login and the file (<see cref="SqlServerFixture"/>), a killed run's
/// at the next run's sweep. It prints as its login and its reference only.
/// </summary>
public sealed class ReadOnlyPrincipal(string login, string reference)
{
    /// <summary>The login's name after its database's.</summary>
    public const string Suffix = "_reader";

    private const string Integrated = "The SQL Server under the fixture tests authenticates Windows identities only (SERVERPROPERTY('IsIntegratedSecurityOnly') = 1), "
        + "so the read-only principal cannot be a SQL login there. Remedy: start Docker (the fixture then runs ci/sql.sh up, or ci/sql.ps1 up on Windows), "
        + "or set DBCHANGE_SQL to a server that accepts SQL logins.";

    private const string Grant = "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') "
        + "+ N', DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME()) + N'; CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) "
        + "+ N'; ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@login) + N'; GRANT VIEW DEFINITION TO ' + QUOTENAME(@login) + N';'; EXEC (@sql);";

    public string Login => login;

    public string Reference => reference;

    /// <summary>The connection string the reference names, read as io/SqlServer reads a file: reference (git keeps the file out of every commit, and its owner alone reads it): never kept or printed.</summary>
    public string ConnectionString => SqlServer.Read("the read-only principal's connection, " + reference + ",", Expect.Value(SecretReference.Of("the read-only principal", reference)), Repository.Root)
        .Match(text => text ?? throw new InvalidOperationException(reference + " resolves to nothing"), error => throw new InvalidOperationException(error.Code + ": " + error.Message));

    public static async Task<ReadOnlyPrincipal> CreateAsync(RegisteredDatabase database)
    {
        if (await SqlServerFixture.ScalarAsync(database.ConnectionString, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int);") == 1)
        {
            throw new InvalidOperationException(Integrated);
        }

        var login = database.Name + Suffix;
        var password = "Ro1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, Grant, null, ("@login", login), ("@password", password));
        var file = FileOf(database.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new SqlConnectionStringBuilder(database.ConnectionString)
        {
            IntegratedSecurity = false,
            UserID = login,
            Password = password,
            ApplicationName = "dbchange-tests-reader",
            Pooling = false,
        }.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return new ReadOnlyPrincipal(login, "file:" + Path.GetRelativePath(Repository.Root, file).Replace('\\', '/'));
    }

    /// <summary>Deletes a database's principal file, if it has one; its login is dropped with the database.</summary>
    internal static void Forget(string database)
    {
        if (File.Exists(FileOf(database)))
        {
            File.Delete(FileOf(database));
        }
    }

    public override string ToString() => login + " through " + reference;

    private static string FileOf(string database) => Path.Combine(Repository.Root, ".dbchange", "principals", database + Suffix + ".connection");
}
