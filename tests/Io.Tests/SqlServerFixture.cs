using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Estate.Kernel;
using Microsoft.Data.SqlClient;

namespace Estate.Io.Tests;

/// <summary>
/// One SQL Server per test run, and a registered database per test, estate_&lt;host&gt;_&lt;pid&gt;_&lt;rand&gt;, dropped after it, so
/// concurrent runs and agents sharing a server never collide. The server is io/LocalServer's (WP 1.4): ESTATE_SQL when set; else,
/// where docker info answers, the estate-sql container that ci/sql.sh up (ci/sql.ps1 up on Windows) pulls and starts, reached
/// through ~/.estate/sql.env; else LocalDB's MSSQLLocalDB. With none, every fixture test fails with the remedy; the fixture lane
/// never skips. Every login made for a database is named after it, &lt;database&gt;_&lt;role&gt;, and is dropped with it.
/// </summary>
public static class SqlServerFixture
{
    private const string NoServer = "No SQL Server for the fixture tests: ESTATE_SQL is unset, docker info does not answer, and sqllocaldb is absent. "
        + "Remedy: start Docker (the fixture then runs ci/sql.sh up, or ci/sql.ps1 up on Windows); or set ESTATE_SQL to a connection string; or install SQL Server Express LocalDB.";

    private const string Create = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    /// <summary>The database, then each login named after it, once no session holds it (a session that ended first is no error).</summary>
    private const string Drop = "DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';'; "
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
        var name = CopyName.Make(Environment.MachineName, Environment.ProcessId, BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4))).ToString();
        await ExecuteAsync(master, Create, name);
        return new RegisteredDatabase(name, new SqlConnectionStringBuilder(master) { InitialCatalog = name, Pooling = false }.ConnectionString, master);
    }

    /// <summary>An estate's root for the copies a test makes: the folder given, its estate/environments.json naming no environment, so R15 reads it and clears the local server.</summary>
    public static string EstateRoot(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "estate"));
        File.WriteAllText(Path.Combine(folder, "estate", "environments.json"), "{ \"environments\": {} }");
        return Path.GetFullPath(folder);
    }

    public static async Task<bool> ExistsAsync(string name) => await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.databases WHERE name = @name;", name) == 1;

    public static async Task<bool> LoginExistsAsync(string name) => await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.server_principals WHERE name = @name;", name) == 1;

    /// <summary>The statement run with @name and any further parameter given, each nvarchar(128).</summary>
    public static async Task ExecuteAsync(string connectionString, string sql, string? name = null, params (string Name, string Value)[] parameters) =>
        await RunAsync(connectionString, sql, name, parameters, c => c.ExecuteNonQueryAsync());

    public static async Task<int> ScalarAsync(string connectionString, string sql, string? name = null) =>
        Convert.ToInt32(await RunAsync(connectionString, sql, name, [], c => c.ExecuteScalarAsync()), CultureInfo.InvariantCulture);

    internal static async Task DropAsync(string master, string name)
    {
        await ExecuteAsync(master, Drop, name);
        ReadOnlyPrincipal.Forget(name);
    }

    /// <summary>io/LocalServer's choice, once the fixture has started what it chooses: the container when docker info answers, else LocalDB's instance.</summary>
    private static async Task<string> ChooseAsync()
    {
        var given = Environment.GetEnvironmentVariable("ESTATE_SQL");
        var docker = string.IsNullOrEmpty(given) && new Command("docker", ["info"], TimeSpan.FromSeconds(30)).Run() is Ran.Exited { Code: 0 } && Up();
        var localDb = string.IsNullOrEmpty(given) && !docker && new Command("sqllocaldb", ["start", "MSSQLLocalDB"], TimeSpan.FromMinutes(2)).Run() is Ran.Exited { Code: 0 };
        var chosen = LocalServer.Server(given, docker ? LocalServer.SqlEnv : "", localDb).Match(server => server, _ => throw new InvalidOperationException(NoServer));
        var master = new SqlConnectionStringBuilder(chosen) { InitialCatalog = "master", ApplicationName = "estate-tests", TrustServerCertificate = true, ConnectTimeout = 60 }.ConnectionString;
        try
        {
            await SweepAsync(master);
        }
        catch (SqlException e)
        {
            throw new InvalidOperationException("SQL Server at " + new SqlConnectionStringBuilder(master).DataSource + " does not answer: " + e.Message + " " + NoServer, e);
        }

        return master;
    }

    /// <summary>The estate-sql container, up: its port and SA password go into ~/.estate/sql.env, which only the scripts write.</summary>
    private static bool Up()
    {
        var (exit, output) = (OperatingSystem.IsWindows()
            ? Programs.InRepository("pwsh", "-NoProfile", "-File", Path.Combine(Repository.Root, "ci", "sql.ps1"), "up")
            : Programs.InRepository("bash", Path.Combine(Repository.Root, "ci", "sql.sh"), "up")).Finish().Joined();
        if (exit != 0)
        {
            throw new InvalidOperationException("ci/sql up failed:\n" + output);
        }

        // Finding NFR-13: the scripts leave the SA password readable by its owner alone, mode 0600; Windows keeps no such mode.
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(LocalServer.SqlEnv) is var mode && mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        {
            throw new InvalidOperationException(LocalServer.SqlEnv + " is mode " + Convert.ToString((int)mode, 8) + " after ci/sql up, and the SA password it holds is read by its owner alone (0600).");
        }

        return true;
    }

    /// <summary>Drops what this host registered for a process no longer running: a killed run's databases, and the logins named after them.</summary>
    private static async Task SweepAsync(string master)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        // A login named after a database is <database>_<role>, so the database's name is the login's up to its last underscore.
        await using var list = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE N'estate[_]%' UNION SELECT LEFT(name, LEN(name) - CHARINDEX(N'_', REVERSE(name))) "
            + "FROM sys.server_principals WHERE name LIKE N'estate[_]%';", connection);
        await using var reader = await list.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // A registered database is named as io/LocalServer names a copy (CopyName.Make), so one sweep serves both.
            if (CopyName.Of("a database on the local server", reader.GetString(0)) is Result<CopyName>.Ok(var named) && named.Machine == Machine && !Running(named.Pid))
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

    private static async Task<T> RunAsync<T>(string connectionString, string sql, string? name, (string Name, string Value)[] parameters, Func<SqlCommand, Task<T>> run)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // The fixture creates and drops databases while other test classes do the same; it waits as long as io/LocalServer does.
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = LocalServer.DatabaseStatementSeconds };
        foreach (var (parameter, value) in parameters.Concat(name is null ? [] : [("@name", name)]))
        {
            command.Parameters.Add(new SqlParameter(parameter, System.Data.SqlDbType.NVarChar, 128) { Value = value });
        }

        return await run(command);
    }
}

/// <summary>A test's own database on the run's SQL Server; disposing it drops it and every login named after it, and a second disposal does nothing.</summary>
public sealed class RegisteredDatabase(string name, string connectionString, string master) : IAsyncDisposable
{
    public string Name => name;

    public string ConnectionString => connectionString;

    public async ValueTask DisposeAsync() => await SqlServerFixture.DropAsync(master, name);
}
