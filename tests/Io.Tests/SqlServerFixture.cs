using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io.Tests;

/// <summary>
/// One SQL Server per test run, and a registered database per test, dbchange_&lt;host&gt;_&lt;pid&gt;_&lt;rand&gt;, dropped after it, so
/// concurrent runs and agents sharing a server never collide. The server is io/LocalServer's (WP 1.4): DBCHANGE_SQL when set; else the
/// SQL Server ~/.dbchange/sql.env names, which ci/sql.sh up installs and starts; else LocalDB's MSSQLLocalDB, which ci/sql.ps1 up
/// starts. The fixture starts none of them, as io does not (DECISIONS.md, 2026-10-19). With none, every fixture test fails with the
/// remedy; the fixture lane never skips. Every login made for a database is named after it, &lt;database&gt;_&lt;role&gt;, and is
/// dropped with it.
/// </summary>
public static class SqlServerFixture
{
    private const string NoServer =
        "No SQL Server for the fixture tests: DBCHANGE_SQL is unset, ~/.dbchange/sql.env names no server, and LocalDB is not installed. "
        + "Remedy: run ci/sql.sh up, or ci/sql.ps1 up on Windows; or set DBCHANGE_SQL to a connection string.";

    private const string Create = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    /// <summary>The database, then each login named after it, once no session holds it (a session that ended first is no error).</summary>
    private const string Drop =
        // A database a killed RESTORE left RESTORING takes DROP DATABASE alone: SQL Server refuses ALTER DATABASE on it (Msg 5052).
        "DECLARE @sql nvarchar(max) = CASE WHEN DATABASEPROPERTYEX(@name, 'Status') = N'RESTORING' THEN N'' "
        + "ELSE N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' END + N'DROP DATABASE ' + QUOTENAME(@name) + N';'; "
        + "IF DB_ID(@name) IS NOT NULL EXEC (@sql); "
        + "DECLARE @login sysname, @wait int; DECLARE logins CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.server_principals WHERE name LIKE @name + N'[_]%' AND type = 'S'; "
        + "OPEN logins; FETCH NEXT FROM logins INTO @login; WHILE @@FETCH_STATUS = 0 BEGIN "
        + "SET @sql = N''; SELECT @sql += N'BEGIN TRY KILL ' + CAST(session_id AS nvarchar(10)) + N'; END TRY BEGIN CATCH END CATCH; ' FROM sys.dm_exec_sessions WHERE login_name = @login; EXEC (@sql); "
        // KILL returns before the session is gone, so the drop waits (up to ten seconds) until no session of the login remains.
        + "SET @wait = 0; WHILE @wait < 50 AND EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE login_name = @login) BEGIN WAITFOR DELAY '00:00:00.200'; SET @wait += 1; END; "
        + "SET @sql = N'DROP LOGIN ' + QUOTENAME(@login) + N';'; EXEC (@sql); FETCH NEXT FROM logins INTO @login; END; CLOSE logins; DEALLOCATE logins;";

    /// <summary>This machine's name as the names of the databases made here carry it.</summary>
    private static readonly string Machine = CopyName.Make(Environment.MachineName, 0, 0).Machine;

    private static readonly Lazy<Task<string>> Master = new(ChooseAsync);

    /// <summary>The run's SQL Server, master as its catalog: where io/LocalServer makes the fixture tests' copies.</summary>
    public static Task<string> ServerAsync() => Master.Value;

    public static async Task<RegisteredDatabase> RegisterAsync()
    {
        var master = await Master.Value;
        var name = CopyName.Make(Environment.MachineName, Environment.ProcessId, BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)))
            .ToString();
        await ExecuteAsync(master, Create, name);
        return new RegisteredDatabase(name,
            new SqlConnectionStringBuilder(master) { InitialCatalog = name, Pooling = false }.ConnectionString, master);
    }

    /// <summary>A repository root for the copies a test makes: the folder given, its dbchange/environments.json naming no environment, so
    /// R15 reads it and clears the local server.</summary>
    public static string RepositoryRoot(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "dbchange"));
        File.WriteAllText(Path.Combine(folder, "dbchange", "environments.json"), "{ \"environments\": {} }");
        return Path.GetFullPath(folder);
    }

    public static async Task<bool> ExistsAsync(string name) =>
        await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.databases WHERE name = @name;", name) == 1;

    public static async Task<bool> LoginExistsAsync(string name) =>
        await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.server_principals WHERE name = @name;", name) == 1;

    /// <summary>The statement run with @name and any further parameter given, each nvarchar(4000), which holds a backup's path.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, string? name = null,
        params (string Name, string Value)[] parameters) =>
        await RunAsync(connectionString, sql, name, parameters, c => c.ExecuteNonQueryAsync());

    public static async Task<int> ScalarAsync(string connectionString, string sql, string? name = null) =>
        Convert.ToInt32(await RunAsync(connectionString, sql, name, [], c => c.ExecuteScalarAsync()), CultureInfo.InvariantCulture);

    /// <summary>
    /// Each backup .dbchange/cache/backups.json under the repository root records, deleted: one in the root's own cache goes with the
    /// root; one in the server's default backup folder, which only the server's account can write (the mssql user), through the server's
    /// own xp_delete_file, so a test leaves no file on the server.
    /// </summary>
    public static async Task DeleteBackupsAsync(string repositoryRoot)
    {
        var index = Path.Combine(repositoryRoot, LocalState.Name, "cache", "backups.json");
        if (!File.Exists(index))
        {
            return;
        }

        foreach (var path in System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(index))!["backups"]!.AsArray()
            .Select(b => (string)b!["path"]!).Where(p => !p.StartsWith(repositoryRoot, StringComparison.Ordinal)))
        {
            await ExecuteAsync(await Master.Value, "EXEC master.sys.xp_delete_file 0, @name;", path);
        }
    }

    internal static async Task DropAsync(string master, string name)
    {
        await ExecuteAsync(master, Drop, name);
        ReadOnlyPrincipal.Forget(name);
    }

    /// <summary>io/LocalServer's choice: DBCHANGE_SQL, else the SQL Server sql.env names, else LocalDB's instance where sqllocaldb
    /// knows it.</summary>
    private static async Task<string> ChooseAsync()
    {
        var given = Environment.GetEnvironmentVariable("DBCHANGE_SQL");
        var sqlEnv = LocalState.UserSqlEnv is { } path && File.Exists(path) ? Private(path) : null;
        var localDb = string.IsNullOrEmpty(given) && sqlEnv is null
            && new Command("sqllocaldb", ["info", "MSSQLLocalDB"], TimeSpan.FromMinutes(2)).Run() is Ran.Exited { Code: 0 };
        var chosen = LocalServer.Server(given, sqlEnv, localDb)
            .Match(server => server, _ => throw new InvalidOperationException(NoServer));
        var master = new SqlConnectionStringBuilder(chosen) { InitialCatalog = "master", ApplicationName = "dbchange-tests",
            TrustServerCertificate = true, ConnectTimeout = 60 }.ConnectionString;
        try
        {
            await SweepAsync(master);
        }
        catch (SqlException e)
        {
            throw new InvalidOperationException(
                "SQL Server at " + new SqlConnectionStringBuilder(master).DataSource + " does not answer: " + e.Message + " " + NoServer,
                e);
        }

        return master;
    }

    /// <summary>Finding NFR-13: ci/sql.sh leaves the SA password in sql.env readable by its owner alone, mode 0600; Windows keeps no
    /// such mode. The file's path, once its mode is checked.</summary>
    private static string Private(string sqlEnv)
    {
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(sqlEnv) is var mode
            && mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        {
            throw new InvalidOperationException(sqlEnv + " is mode " + Convert.ToString((int)mode, 8)
                + ", and the SA password it holds is read by its owner alone (0600): run ci/sql.sh up, which sets the mode again.");
        }

        return sqlEnv;
    }

    /// <summary>Drops what this host registered for a process no longer running: a killed run's databases, and the logins named after
    /// them.</summary>
    private static async Task SweepAsync(string master)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        // A login named after a database is <database>_<role>, so the database's name is the login's up to its last underscore.
        await using var list = new SqlCommand(
            "SELECT name FROM sys.databases WHERE name LIKE N'dbchange[_]%' UNION SELECT LEFT(name, LEN(name) - CHARINDEX(N'_', REVERSE(name))) "
            + "FROM sys.server_principals WHERE name LIKE N'dbchange[_]%';", connection);
        await using var reader = await list.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // A registered database is named as io/LocalServer names a copy (CopyName.Make), so one sweep serves both.
            if (CopyName.Of("a database on the local server", reader.GetString(0)) is Result<CopyName>.Ok(var named)
                && named.Machine == Machine && !Running(named.Pid))
            {
                await DropAsync(master, reader.GetString(0));
            }
        }
    }

    private static bool Running(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<T> RunAsync<T>(string connectionString, string sql, string? name, (string Name, string Value)[] parameters,
        Func<SqlCommand, Task<T>> run)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // The fixture creates and drops databases while other test classes do the same; it waits as long as io/LocalServer does.
        await using var command = new SqlCommand(sql,
            connection) { CommandTimeout = (int)LocalServer.DatabaseStatementTimeout.TotalSeconds };
        foreach (var (parameter, value) in parameters.Concat(name is null ? [] : [("@name", name)]))
        {
            command.Parameters.Add(new SqlParameter(parameter, System.Data.SqlDbType.NVarChar, 4000) { Value = value });
        }

        return await run(command);
    }
}

/// <summary>A test's own database on the run's SQL Server; disposing it drops it and every login named after it, and a second disposal does
/// nothing.</summary>
public sealed class RegisteredDatabase(string name, string connectionString, string master) : IAsyncDisposable
{
    public string Name => name;

    public string ConnectionString => connectionString;

    public async ValueTask DisposeAsync() => await SqlServerFixture.DropAsync(master, name);
}
