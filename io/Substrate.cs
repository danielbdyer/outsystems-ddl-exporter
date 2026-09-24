using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Estate.Kernel;
using Microsoft.Data.SqlClient;

namespace Estate.Io;

/// <summary>
/// The local substrate, minimal (V3_MILESTONES.md §2.2, WP 1.4; WP 3.4 completes it): the SQL Server a copy is made on, the one
/// ESTATE_SQL names, else the estate-sql container through ~/.estate/sql.env, else LocalDB; Create, which names a copy for this host
/// and process, records it in .estate/copies.json and makes its database; Drop, which removes both; and the registry, against which
/// alone copy: resolves. A substrate on a host an environment's reference resolves to is refused before anything connects (R15).
/// </summary>
public static class Substrate
{
    private const string Registry = ".estate/copies.json";

    private const string Make = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    private const string Unmake = "IF DB_ID(@name) IS NOT NULL BEGIN DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) "
        + "+ N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql); END";

    /// <summary>The estate-sql container's port and SA password, which only ci/sql.sh and ci/sql.ps1 write.</summary>
    public static string SqlEnv { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".estate", "sql.env");

    public static Result<string> Server() =>
        Server(Environment.GetEnvironmentVariable("ESTATE_SQL"), SqlEnv, OperatingSystem.IsWindows() && Doctor.Run("sqllocaldb", ["info", "MSSQLLocalDB"]) is (0, _));

    /// <summary>The substrate's server, in the fixture's order: ESTATE_SQL; the container, when sql.env gives its port and password; LocalDB, when installed.</summary>
    public static Result<string> Server(string? estateSql, string sqlEnv, bool localDb)
    {
        var env = File.Exists(sqlEnv)
            ? File.ReadAllLines(sqlEnv).Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.Ordinal)
            : [];
        return !string.IsNullOrEmpty(estateSql) ? estateSql
            : env.GetValueOrDefault("MSSQL_SA_PASSWORD") is { Length: > 0 } password && env.GetValueOrDefault("ESTATE_SQL_PORT") is { Length: > 0 } port
                ? new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + port, UserID = "sa", Password = password, TrustServerCertificate = true }.ConnectionString
            : localDb ? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true"
            : new Refusal("substrate.missing", "No substrate: ESTATE_SQL is unset, " + sqlEnv + " gives no container's port and password, and LocalDB is not installed.",
                "Start Docker and run ci/sql.sh up, or ci/sql.ps1 up on Windows, or set ESTATE_SQL; then run estate doctor.");
    }

    /// <summary>A copy's name: estate_&lt;host&gt;_&lt;pid&gt;_&lt;rand&gt;, lower case and [a-z0-9_] only.</summary>
    public static string CopyName(string host, int pid, string random) =>
        "estate_" + Host(host) + "_" + pid.ToString(CultureInfo.InvariantCulture) + "_" + random.ToLowerInvariant();

    public static Result<SqlServer.Copy> Create(string estateRoot) => Server().Bind(server => Create(estateRoot, server));

    /// <summary>A copy on the server given, refused on a named environment's host; recorded before its database is made, so a crash leaves a row to follow.</summary>
    public static Result<SqlServer.Copy> Create(string estateRoot, string server) => Unnamed(estateRoot, server).Bind(_ =>
    {
        var copy = new SqlServer.Copy(CopyName(Environment.MachineName, Environment.ProcessId, Convert.ToHexString(RandomNumberGenerator.GetBytes(4))), server, estateRoot);
        var row = new JsonObject
        {
            ["name"] = copy.Name, ["host"] = Host(Environment.MachineName), ["pid"] = Environment.ProcessId,
            ["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        return Change(estateRoot, rows => [.. rows, row]).Bind(_ => Run(copy, Make).Match(
            made => Result.Ok(made),
            refusal => Change(estateRoot, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name)]).Bind(_ => Result.Refuse<SqlServer.Copy>(refusal))));
    });

    /// <summary>The copy's database dropped, its sessions ended first, then its row; a database already gone is no error.</summary>
    public static Result<string> Drop(SqlServer.Copy copy)
    {
        SqlConnection.ClearPool(new SqlConnection(copy.Connection));
        return Run(copy, Unmake).Bind(_ => Change(copy.Root, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name)])).Map(_ => copy.Name);
    }

    /// <summary>copy: resolved against .estate/copies.json alone, on the substrate, which must not be a named environment's host.</summary>
    internal static Result<SqlServer.Copy> Registered(string estateRoot, string name) => Rows(estateRoot).Bind(rows => rows.Any(r => (string?)r["name"] == name)
        ? Server().Bind(server => Unnamed(estateRoot, server)).Map(server => new SqlServer.Copy(name, server, estateRoot))
        : new Refusal("copy.unregistered", "copy:" + name + " is no copy " + Registry + " holds, and copy: names only a database estate made and recorded there.",
            "Name a copy that " + Registry + " holds on this machine."));

    /// <summary>A machine's name as a copy's name carries it: lower case, [a-z0-9_] only, at most forty characters.</summary>
    private static string Host(string machine) => new([.. machine.ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '_').Take(40)]);

    /// <summary>R15: the server's host is none an environment's reference resolves to; an environment whose reference resolves to nothing here goes uncompared.</summary>
    private static Result<string> Unnamed(string estateRoot, string server)
    {
        string host;
        try
        {
            host = SqlServer.Host(new SqlConnectionStringBuilder(server).DataSource);
        }
        catch (ArgumentException)
        {
            return new Refusal("substrate.missing", "The substrate's server is no connection string SqlClient reads; its text is withheld.", "Correct ESTATE_SQL, or unset it; then run estate doctor.");
        }

        var environments = File.Exists(Path.Combine(estateRoot, Profiles.Posture)) ? Profiles.Environments(estateRoot) : Result.Ok(default(Seq<NamedEnvironment>));
        return environments.Bind(all => all.FirstOrDefault(e => SqlServer.Connect(e.ToString(), e.Connection, estateRoot)
                .Match(connection => SqlServer.Host(new SqlConnectionStringBuilder(connection).DataSource) == host, _ => false)) is { } named
            ? new Refusal("copy.named-host", "The substrate is on the host env:" + named.Name + "'s connection resolves to, and a copy is made only where no named environment lives.",
                "Point ESTATE_SQL at a local SQL Server, or unset it and run ci/sql.sh up (ci/sql.ps1 up on Windows).")
            : Result.Ok(server));
    }

    /// <summary>A statement about the copy's database, run against master on its server with the copy's name as @name.</summary>
    private static Result<SqlServer.Copy> Run(SqlServer.Copy copy, string statement)
    {
        try
        {
            using var connection = new SqlConnection(new SqlConnectionStringBuilder(copy.Connection) { InitialCatalog = "master", Pooling = false }.ConnectionString);
            connection.Open();
            using var command = new SqlCommand(statement, connection);
            command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = copy.Name });
            command.ExecuteNonQuery();
            return copy;
        }
        catch (SqlException e)
        {
            return copy.Refused(e.Number, e.Message, fatal: e.Class >= 20);
        }
    }

    /// <summary>The registry's rows, none when it is absent. A write replaces the file whole, so a reader sees the rows before a change or after it.</summary>
    private static Result<List<JsonObject>> Rows(string estateRoot)
    {
        var path = Path.Combine(estateRoot, Registry);
        try
        {
            return !File.Exists(path) ? new List<JsonObject>()
                : JsonNode.Parse(File.ReadAllText(path))?["copies"] is JsonArray rows && rows.All(r => r is JsonObject o && o["name"]?.GetValueKind() == JsonValueKind.String)
                    ? rows.Select(r => (JsonObject)r!.DeepClone()).ToList()
                    : throw new JsonException("not the registry");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new Refusal("registry.unreadable", Registry + " under " + estateRoot + " is not the registry estate writes; its text is withheld.",
                "Drop the copies it lists with DROP DATABASE, then delete " + Registry + ".");
        }
    }

    /// <summary>The registry changed under a lock this process alone holds while it reads, changes and writes the file back through io/Write.</summary>
    private static Result<List<JsonObject>> Change(string estateRoot, Func<List<JsonObject>, List<JsonObject>> change)
    {
        var folder = Directory.CreateDirectory(Path.Combine(estateRoot, ".estate")).FullName;
        using var held = Held(Path.Combine(folder, "copies.lock"));
        return Rows(estateRoot).Map(change).Map(rows =>
        {
            Write.Text(Path.Combine(estateRoot, Registry), Json.Text(new JsonObject { ["copies"] = new JsonArray([.. rows]) }));
            return rows;
        });
    }

    /// <summary>The lock file opened for this process alone, another holder waited out for up to a minute; the system closes it when its holder ends.</summary>
    private static FileStream Held(string path)
    {
        for (var waiting = Stopwatch.StartNew(); ; Thread.Sleep(20))
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException sharing) when (sharing.GetType() == typeof(IOException) && waiting.Elapsed < TimeSpan.FromMinutes(1))
            {
            }
        }
    }
}
