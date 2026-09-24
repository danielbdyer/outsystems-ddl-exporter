using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Estate.Budgets.Tests;
using Microsoft.Data.SqlClient;

namespace Estate.Io.Tests;

/// <summary>
/// One SQL Server per test run, and a registered database per test, estate_&lt;host&gt;_&lt;pid&gt;_&lt;rand&gt;, dropped after it, so
/// concurrent runs and agents sharing a server never collide. The server is io/Substrate's (WP 1.4): ESTATE_SQL when set; else,
/// where docker info answers, the estate-sql container that ci/sql.sh up (ci/sql.ps1 up on Windows) pulls and starts, reached
/// through ~/.estate/sql.env; else LocalDB's MSSQLLocalDB. With none, every fixture test fails with the remedy; the fixture lane
/// never skips.
/// </summary>
public static class SqlServerFixture
{
    private const string NoServer = "No SQL Server for the fixture tests: ESTATE_SQL is unset, docker info does not answer, and sqllocaldb is absent. "
        + "Remedy: start Docker (the fixture then runs ci/sql.sh up, or ci/sql.ps1 up on Windows); or set ESTATE_SQL to a connection string; or install SQL Server Express LocalDB.";

    private const string Create = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    /// <summary>The database, then its read-only principal's login, once no session holds it (a session that ended first is no error).</summary>
    private const string Drop = "DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';'; "
        + "IF DB_ID(@name) IS NOT NULL EXEC (@sql); "
        + "DECLARE @reader sysname = @name + N'" + ReadOnlyPrincipal.Suffix + "'; SET @sql = N''; "
        + "SELECT @sql += N'BEGIN TRY KILL ' + CAST(session_id AS nvarchar(10)) + N'; END TRY BEGIN CATCH END CATCH; ' FROM sys.dm_exec_sessions WHERE login_name = @reader; "
        + "IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @reader) SET @sql += N'DROP LOGIN ' + QUOTENAME(@reader) + N';'; "
        + "EXEC (@sql);";

    /// <summary>What io/Substrate names this host's databases with, up to the process: estate_&lt;host&gt;_.</summary>
    private static readonly string Prefix = Substrate.CopyName(Environment.MachineName, 0, "00000000")[..^"0_00000000".Length];

    private static readonly Lazy<Task<string>> Master = new(ChooseAsync);

    /// <summary>The run's SQL Server, master as its catalog: where io/Substrate makes the fixture tests' copies.</summary>
    public static Task<string> ServerAsync() => Master.Value;

    public static async Task<RegisteredDatabase> RegisterAsync()
    {
        var master = await Master.Value;
        var name = DatabaseName(Environment.MachineName, Environment.ProcessId, Convert.ToHexString(RandomNumberGenerator.GetBytes(4)));
        await ExecuteAsync(master, Create, name);
        return new RegisteredDatabase(name, new SqlConnectionStringBuilder(master) { InitialCatalog = name, Pooling = false }.ConnectionString, master);
    }

    /// <summary>A registered database is named as io/Substrate names a copy, so one sweep serves both.</summary>
    public static string DatabaseName(string host, int pid, string random) => Substrate.CopyName(host, pid, random);

    /// <summary>An estate's root for the copies a test makes: the folder given, its estate/posture.json naming no environment, so R15 reads it and clears the substrate.</summary>
    public static string EstateRoot(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "estate"));
        File.WriteAllText(Path.Combine(folder, "estate", "posture.json"), "{ \"environments\": {} }");
        return Path.GetFullPath(folder);
    }

    public static async Task<bool> ExistsAsync(string name) => await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.databases WHERE name = @name;", name) == 1;

    public static async Task<bool> LoginExistsAsync(string name) => await ScalarAsync(await Master.Value, "SELECT COUNT(*) FROM sys.server_principals WHERE name = @name;", name) == 1;

    public static async Task ExecuteAsync(string connectionString, string sql, string? name = null) => await RunAsync(connectionString, sql, name, c => c.ExecuteNonQueryAsync());

    public static async Task<int> ScalarAsync(string connectionString, string sql, string? name = null) =>
        Convert.ToInt32(await RunAsync(connectionString, sql, name, c => c.ExecuteScalarAsync()), CultureInfo.InvariantCulture);

    internal static async Task DropAsync(string master, string name)
    {
        await ExecuteAsync(master, Drop, name);
        ReadOnlyPrincipal.Forget(name);
    }

    /// <summary>io/Substrate's choice, once the fixture has started what it chooses: the container when docker info answers, else LocalDB's instance.</summary>
    private static async Task<string> ChooseAsync()
    {
        var given = Environment.GetEnvironmentVariable("ESTATE_SQL");
        var docker = string.IsNullOrEmpty(given) && Command.Run("docker", ["info"], TimeSpan.FromSeconds(30)).Exit == 0 && Up();
        var localDb = string.IsNullOrEmpty(given) && !docker && Command.Run("sqllocaldb", ["start", "MSSQLLocalDB"], TimeSpan.FromMinutes(2)).Exit == 0;
        var chosen = Substrate.Server(given, docker ? Substrate.SqlEnv : "", localDb).Match(server => server, _ => throw new InvalidOperationException(NoServer));
        var master = new SqlConnectionStringBuilder(chosen) { InitialCatalog = "master", ApplicationName = "estate-tests", TrustServerCertificate = true }.ConnectionString;
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
        var (exit, output) = OperatingSystem.IsWindows()
            ? Command.Run("pwsh", ["-NoProfile", "-File", Path.Combine(Repository.Root, "ci", "sql.ps1"), "up"])
            : Command.Run("bash", [Path.Combine(Repository.Root, "ci", "sql.sh"), "up"]);
        if (exit != 0)
        {
            throw new InvalidOperationException("ci/sql up failed:\n" + output);
        }

        return true;
    }

    /// <summary>Drops what this host registered for a process no longer running: a killed run's databases and their principals' logins.</summary>
    private static async Task SweepAsync(string master)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var list = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE N'estate[_]%' UNION SELECT LEFT(name, LEN(name) - LEN(@suffix)) "
            + "FROM sys.server_principals WHERE name LIKE N'estate[_]%' AND RIGHT(name, LEN(@suffix)) = @suffix;", connection);
        list.Parameters.Add(new SqlParameter("@suffix", System.Data.SqlDbType.NVarChar, 128) { Value = ReadOnlyPrincipal.Suffix });
        await using var reader = await list.ExecuteReaderAsync();
        var owned = new Regex("^" + Regex.Escape(Prefix) + "([0-9]+)_[0-9a-f]{8}$", RegexOptions.CultureInvariant);
        while (await reader.ReadAsync())
        {
            if (owned.Match(reader.GetString(0)) is { Success: true } match && !Running(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)))
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

    private static async Task<T> RunAsync<T>(string connectionString, string sql, string? name, Func<SqlCommand, Task<T>> run)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        if (name is not null)
        {
            command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = name });
        }

        return await run(command);
    }
}

/// <summary>A test's own database on the run's SQL Server; disposing it drops it, and a second disposal does nothing.</summary>
public sealed class RegisteredDatabase(string name, string connectionString, string master) : IAsyncDisposable
{
    public string Name => name;

    public string ConnectionString => connectionString;

    public async ValueTask DisposeAsync() => await SqlServerFixture.DropAsync(master, name);
}
