using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Microsoft.Data.SqlClient;

namespace Estate.Io.Tests;

/// <summary>
/// The read-only principal (V3_MILESTONES.md WP 1.8, R14) for a registered database: a SQL login and its user holding only
/// VIEW DEFINITION at the database's scope and db_datareader, nothing server-wide beyond the CONNECT SQL a login carries.
/// Its connection string lives only in a file under .estate/principals/ (ignored), reached through a file: reference; the
/// password is generated here, per principal, and never printed. The login is named for its database, and dropping the
/// database drops the login and the file (<see cref="SqlServerFixture"/>), a killed run's at the next run's sweep. It prints
/// as its login and its reference only.
/// </summary>
public sealed class ReadOnlyPrincipal(string login, string reference)
{
    /// <summary>The login's name after its database's.</summary>
    public const string Suffix = "_reader";

    private const string Integrated = "The SQL Server under the fixture tests authenticates Windows identities only (SERVERPROPERTY('IsIntegratedSecurityOnly') = 1), "
        + "so the read-only principal cannot be a SQL login there. Remedy: start Docker (the fixture then runs ci/sql.sh up, or ci/sql.ps1 up on Windows), "
        + "or set ESTATE_SQL to a server that accepts SQL logins.";

    private const string Grant = "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') "
        + "+ N', DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME()) + N'; CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) "
        + "+ N'; ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@login) + N'; GRANT VIEW DEFINITION TO ' + QUOTENAME(@login) + N';'; EXEC (@sql);";

    public string Login => login;

    public string Reference => reference;

    /// <summary>The connection string the reference names: read when asked for, never kept or printed.</summary>
    public string ConnectionString => Resolve(reference);

    public static async Task<ReadOnlyPrincipal> CreateAsync(RegisteredDatabase database)
    {
        if (await SqlServerFixture.ScalarAsync(database.ConnectionString, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int);") == 1)
        {
            throw new InvalidOperationException(Integrated);
        }

        var login = database.Name + Suffix;
        var password = "Ro1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(Grant, connection);
            command.Parameters.Add(new SqlParameter("@login", System.Data.SqlDbType.NVarChar, 128) { Value = login });
            command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128) { Value = password });
            await command.ExecuteNonQueryAsync();
        }

        var file = FileOf(database.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new SqlConnectionStringBuilder(database.ConnectionString)
        {
            IntegratedSecurity = false,
            UserID = login,
            Password = password,
            ApplicationName = "estate-tests-reader",
            Pooling = false,
        }.ConnectionString);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return new ReadOnlyPrincipal(login, "file:" + Path.GetRelativePath(Repository.Root, file).Replace('\\', '/'));
    }

    /// <summary>
    /// The file: half of WP 1.4's reference grammar, for tests only: the named file's text, its path relative to the
    /// repository's root. Anything else, a literal connection string included, is refused.
    /// </summary>
    public static string Resolve(string reference) => reference.StartsWith("file:", StringComparison.Ordinal)
        ? File.ReadAllText(Path.Combine(Repository.Root, reference["file:".Length..])).Trim()
        : throw new ArgumentException("not a file: reference", nameof(reference));

    /// <summary>Deletes a database's principal file, if it has one; its login is dropped with the database.</summary>
    internal static void Forget(string database)
    {
        if (File.Exists(FileOf(database)))
        {
            File.Delete(FileOf(database));
        }
    }

    public override string ToString() => login + " through " + reference;

    private static string FileOf(string database) => Path.Combine(Repository.Root, ".estate", "principals", database + Suffix + ".connection");
}
