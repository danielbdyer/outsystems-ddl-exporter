using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using Estate.Kernel;
using Microsoft.Data.SqlClient;

namespace Estate.Io;

/// <summary>
/// The scratch server, minimal (V3_MILESTONES.md §2.2, WP 1.4; WP 3.4 completes it): the SQL Server a copy is made on, the one
/// ESTATE_SQL names, else the estate-sql container through ~/.estate/sql.env, else LocalDB, chosen inside io, so no caller holds its
/// login or makes a copy anywhere else; Create, which names a copy for this host and process, records it and its server in
/// .estate/copies.json and makes its database; Drop, which removes both; and the registry, against which alone copy: resolves, on the
/// server its row records. A scratch server on a host an environment's reference resolves to, by spelling or by address, is refused before
/// anything connects (R15).
/// </summary>
public static class ScratchServer
{
    internal const string Registry = ".estate/copies.json";

    private const string Make = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    private const string Unmake = "IF DB_ID(@name) IS NOT NULL BEGIN DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) "
        + "+ N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql); END";

    /// <summary>
    /// How long CREATE DATABASE and DROP DATABASE may wait. Each waits for the database locks that other copies being made or
    /// dropped on the same server hold, and LocalDB on the four-core Windows CI runner has taken longer than SqlClient's default of
    /// 30 seconds for a DROP while parallel test classes made and dropped their own databases.
    /// </summary>
    internal const int DatabaseStatementSeconds = 180;

    /// <summary>This machine's addresses as R15 reads them: loopback, and each its network interfaces hold.</summary>
    private static readonly Lazy<HashSet<IPAddress>> Local = new(() =>
    {
        try
        {
            return [IPAddress.Loopback, IPAddress.IPv6Loopback, .. NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(u => Plain(u.Address))];
        }
        catch (NetworkInformationException)
        {
            return [IPAddress.Loopback, IPAddress.IPv6Loopback];
        }
    });

    /// <summary>The estate-sql container's port and SA password, which only ci/sql.sh and ci/sql.ps1 write.</summary>
    public static string SqlEnv { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".estate", "sql.env");

    /// <summary>The scratch server, from the sources given, as the registry records it and R15 compares it (localhost,11433); nothing of its login.</summary>
    public static Result<string> ServerName(string? estateSql, string sqlEnv, bool localDb) => Server(estateSql, sqlEnv, localDb).Bind(ServerName);

    /// <summary>A copy on this machine's scratch server, refused on a named environment's host (R15).</summary>
    public static Result<SqlServer.Copy> Create(string estateRoot) => Server().Bind(server => Create(estateRoot, server));

    /// <summary>The copy's database dropped, its sessions ended first, then its row; a database already gone is no error.</summary>
    public static Result<string> Drop(SqlServer.Copy copy)
    {
        SqlConnection.ClearPool(new SqlConnection(copy.Connection));
        return Run(copy, Unmake).Bind(_ => Change(copy.Root, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name)])).Map(_ => copy.Name);
    }

    /// <summary>The digest of the SQL Server image a database runs in: the pinned image's for a copy on the estate-sql container; none on LocalDB or a server ESTATE_SQL names.</summary>
    public static string? Image(SqlServer.Database target) => target is SqlServer.Copy copy && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ESTATE_SQL"))
        && Server(null, SqlEnv, localDb: false).Bind(ServerName) is Result<string>.Ok { Value: var container }
        && ServerName(copy.Connection) is Result<string>.Ok { Value: var made } && made == container ? Doctor.ImageDigest : null;

    /// <summary>A copy's name: estate_&lt;host&gt;_&lt;pid&gt;_&lt;rand&gt;, lower case and [a-z0-9_] only.</summary>
    public static string CopyName(string host, int pid, string random) =>
        "estate_" + Host(host) + "_" + pid.ToString(CultureInfo.InvariantCulture) + "_" + random.ToLowerInvariant();

    internal static Result<string> Server() =>
        Server(Environment.GetEnvironmentVariable("ESTATE_SQL"), SqlEnv, OperatingSystem.IsWindows() && Doctor.Run("sqllocaldb", ["info", "MSSQLLocalDB"]) is (0, _));

    /// <summary>The scratch server, in the fixture's order: ESTATE_SQL; the container, when sql.env gives its port and password; LocalDB, when installed.</summary>
    internal static Result<string> Server(string? estateSql, string sqlEnv, bool localDb)
    {
        var env = File.Exists(sqlEnv)
            ? File.ReadAllLines(sqlEnv).Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.Ordinal)
            : [];
        return !string.IsNullOrEmpty(estateSql) ? estateSql
            : env.GetValueOrDefault("MSSQL_SA_PASSWORD") is { Length: > 0 } password && env.GetValueOrDefault("ESTATE_SQL_PORT") is { Length: > 0 } port
                ? new SqlConnectionStringBuilder { DataSource = "127.0.0.1," + port, UserID = "sa", Password = password, TrustServerCertificate = true }.ConnectionString
            : localDb ? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true"
            : new Error("scratch-server.missing", "No scratch server: ESTATE_SQL is unset, " + sqlEnv + " gives no container's port and password, and LocalDB is not installed.",
                "Start Docker and run ci/sql.sh up, or ci/sql.ps1 up on Windows, or set ESTATE_SQL; then run estate doctor.");
    }

    /// <summary>A server as the registry records it and R15 compares it: its host as SqlServer.Host spells it, then its port or instance, in lower case.</summary>
    internal static Result<string> ServerName(string server)
    {
        string source;
        try
        {
            source = new SqlConnectionStringBuilder(server).DataSource;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        {
            return new Error("scratch-server.missing", "The scratch server, as ESTATE_SQL gives it, is no connection string SqlClient reads; its text is withheld.",
                "Correct ESTATE_SQL, or unset it; then run estate doctor.");
        }

        var bare = Regex.Replace(source.Trim(), @"\A(?:tcp|np|lpc|admin):", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var rest = bare.IndexOfAny([',', '\\'], bare.StartsWith(@"\\", StringComparison.Ordinal) ? 2 : 0) is >= 0 and var at ? bare[at..] : "";
        return SqlServer.Host(source) + Regex.Replace(rest, @"\s", "").ToLowerInvariant();
    }

    /// <summary>A copy on the server given, refused on a named environment's host; recorded with its server before its database is made, so a crash leaves a row to follow.</summary>
    internal static Result<SqlServer.Copy> Create(string estateRoot, string server, Func<string, IPAddress[]>? resolve = null) =>
        ServerName(server).Bind(name => Unnamed(estateRoot, name, resolve ?? Resolved)).Bind(name =>
        {
            var copy = new SqlServer.Copy(CopyName(Environment.MachineName, Environment.ProcessId, Convert.ToHexString(RandomNumberGenerator.GetBytes(4))), server, estateRoot);
            var row = new JsonObject
            {
                ["name"] = copy.Name, ["server"] = name, ["host"] = Host(Environment.MachineName), ["pid"] = Environment.ProcessId,
                ["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            };
            return Change(estateRoot, rows => [.. rows, row]).Bind(_ => Run(copy, Make).Match(
                made => Result.Ok(made),
                error => Change(estateRoot, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name)]).Bind(_ => Result.Fail<SqlServer.Copy>(error))));
        });

    /// <summary>copy: resolved against .estate/copies.json alone: the row holding the name, on a server no environment's reference resolves to, which the scratch server this machine names must still be.</summary>
    internal static Result<SqlServer.Copy> Registered(string estateRoot, string name) => Registered(estateRoot, name, Server, Resolved);

    internal static Result<SqlServer.Copy> Registered(string estateRoot, string name, Func<Result<string>> chosen, Func<string, IPAddress[]> resolve) =>
        Rows(estateRoot).Bind(rows => rows.FirstOrDefault(r => (string?)r["name"] == name) is not { } row
            ? new Error("copy.unregistered", "copy:" + name + " is no copy " + Registry + " holds, and copy: names only a database estate made and recorded there.",
                "Name a copy that " + Registry + " holds on this machine.")
            : Unnamed(estateRoot, (string)row["server"]!, resolve).Bind(made => chosen().Bind(server => ServerName(server).Bind(now => now == made
                ? Result.Ok(new SqlServer.Copy(name, server, estateRoot))
                : new Error("copy.unregistered", "copy:" + name + " was made on another server than the scratch server this machine names now, so " + Registry + " holds no such copy here.",
                    "Set ESTATE_SQL back to the server that made the copy, or make a new copy on this one.")))));

    /// <summary>
    /// R15: the server's host is none an environment's reference resolves to, compared by spelling, then by address; this machine is
    /// every loopback address and each of its own. An environment whose reference resolves to nothing on this machine goes uncompared,
    /// its host unknown here; one SqlClient reads no connection string from is an error, and so is an estate without its posture.
    /// </summary>
    internal static Result<string> Unnamed(string estateRoot, string serverName, Func<string, IPAddress[]> resolve) =>
        Profiles.Environments(estateRoot).Bind(environments => Result.All(environments.Select(environment => SqlServer.DataSource(environment, estateRoot)
            .Map(source => (Environment: environment, Source: source)))))
        .Bind(sources =>
        {
            var hosts = sources.Where(s => s.Source is not null).Select(s => (s.Environment, Host: SqlServer.Host(s.Source!))).ToList();
            var host = SqlServer.Host(serverName);
            var addresses = new Lazy<HashSet<IPAddress>>(() => Addresses(host, resolve));
            return hosts.Where(h => h.Host == host).Concat(hosts.Where(h => h.Host != host && Addresses(h.Host, resolve).Overlaps(addresses.Value))).Select(h => h.Environment).FirstOrDefault() is { } named
                ? new Error("copy.named-host", "The scratch server is on the host env:" + named.Name + "'s connection resolves to, and a copy is made only where no named environment lives.",
                    "Point ESTATE_SQL at a local SQL Server, or unset it and run ci/sql.sh up (ci/sql.ps1 up on Windows).")
                : Result.Ok(serverName);
        });

    /// <summary>A host's addresses: this machine's, for a host spelled as this machine or LocalDB or resolving to any of this machine's; else what it resolves to.</summary>
    private static HashSet<IPAddress> Addresses(string host, Func<string, IPAddress[]> resolve)
    {
        HashSet<IPAddress> found = [.. (host is "localhost" or "(localdb)" ? [IPAddress.Loopback] : IPAddress.TryParse(host, out var literal) ? [literal] : resolve(host)).Select(Plain)];
        return found.Any(a => IPAddress.IsLoopback(a) || Local.Value.Contains(a)) ? [.. found, .. Local.Value] : found;
    }

    /// <summary>An IPv4 address mapped into IPv6, as the IPv4 address it is.</summary>
    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>What DNS resolves a host name to, waited on for five seconds; none when it does not answer.</summary>
    private static IPAddress[] Resolved(string host)
    {
        try
        {
            var lookup = Dns.GetHostAddressesAsync(host);
            return lookup.Wait(TimeSpan.FromSeconds(5)) ? lookup.Result : [];
        }
        catch (Exception e) when (e is AggregateException or SocketException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>A machine's name as a copy's name carries it: lower case, [a-z0-9_] only, at most forty characters.</summary>
    private static string Host(string machine) => new([.. machine.ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '_').Take(40)]);

    /// <summary>A statement about the copy's database, run against master on its server with the copy's name as @name.</summary>
    private static Result<SqlServer.Copy> Run(SqlServer.Copy copy, string statement)
    {
        try
        {
            using var connection = new SqlConnection(new SqlConnectionStringBuilder(copy.Connection) { InitialCatalog = "master", Pooling = false }.ConnectionString);
            connection.Open();
            using var command = new SqlCommand(statement, connection) { CommandTimeout = DatabaseStatementSeconds };
            command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = copy.Name });
            command.ExecuteNonQuery();
            return copy;
        }
        catch (SqlException e)
        {
            return copy.ErrorOf(e.Number, e.Message, fatal: e.Class >= 20);
        }
    }

    /// <summary>The registry's rows, none when it is absent, each naming its copy and the server it was made on. A write replaces the file whole, so a reader sees the rows before a change or after it.</summary>
    private static Result<List<JsonObject>> Rows(string estateRoot)
    {
        var path = Path.Combine(estateRoot, Registry);
        try
        {
            return !File.Exists(path) ? new List<JsonObject>()
                : JsonNode.Parse(File.ReadAllText(path))?["copies"] is JsonArray rows
                    && rows.All(r => r is JsonObject o && o["name"]?.GetValueKind() == JsonValueKind.String && o["server"]?.GetValueKind() == JsonValueKind.String)
                    ? rows.Select(r => (JsonObject)r!.DeepClone()).ToList()
                    : throw new JsonException("not the registry");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new Error("registry.unreadable", Registry + " under " + estateRoot + " is not the registry estate writes; its text is withheld.",
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
