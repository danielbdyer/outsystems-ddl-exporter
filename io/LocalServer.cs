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
using System.Threading;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io;

/// <summary>
/// The local server, minimal (V3_MILESTONES.md §2.2, WP 1.4; WP 3.4 completes it): the SQL Server a copy is made on, the one
/// DBCHANGE_SQL names, else the dbchange-sql container through ~/.dbchange/sql.env, else LocalDB, chosen inside io, so no caller holds its
/// login or makes a copy anywhere else; Create, which names a copy for this host and process, records it and its server in
/// .dbchange/copies.json and makes its database; Drop, which removes both; and the registry, against which alone copy: resolves, on the
/// server its row records. A local server on the host an environment names in dbchange/environments.json, by spelling or by address, is
/// refused before anything connects (R15). Its CREATE and DROP DATABASE go through io/SqlServer.Query, the one statement path.
/// </summary>
public static class LocalServer
{

    private const string Make = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    private const string Unmake = "IF DB_ID(@name) IS NOT NULL BEGIN DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) "
        + "+ N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql); END";

    /// <summary>
    /// How long CREATE DATABASE and DROP DATABASE may wait. Each waits for the database locks that other copies being made or
    /// dropped on the same server hold, and LocalDB on the four-core Windows CI runner has taken longer than SqlClient's default of
    /// 30 seconds for a DROP while parallel test classes made and dropped their own databases.
    /// </summary>
    internal static readonly TimeSpan DatabaseStatementTimeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// How long the one outbound lookup dbchange makes may take (VALUES.md X5): DNS for a named environment's host, which R15 compares with
    /// the local server's; a lookup that does not answer in time reads as an address it does not have.
    /// </summary>
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

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

    /// <summary>The local server, from the sources given, as the registry records it and R15 compares it (localhost,11433); nothing of its login.</summary>
    public static Result<Kernel.ServerName> ServerName(string? dbChangeSql, string? sqlEnv, bool localDb) => Server(dbChangeSql, sqlEnv, localDb).Bind(ServerName);

    /// <summary>A copy on this machine's local server, refused on a named environment's host (R15); its CREATE DATABASE goes to the run's log.</summary>
    public static Result<SqlServer.Copy> Create(string repositoryRoot, SqlServer.QueryLog log) => Server().Bind(server => Create(repositoryRoot, server, log));

    /// <summary>
    /// The copy's database dropped, its sessions ended first, then its row; a database already gone is no error. The DROP DATABASE goes
    /// to the run's log.
    /// </summary>
    public static Result<CopyName> Drop(SqlServer.Copy copy, SqlServer.QueryLog log)
    {
        SqlConnection.ClearPool(new SqlConnection(copy.Connection));
        return Run(copy, "DROP DATABASE", Unmake, log).Bind(_ => Change(copy.Root, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name.ToString())])).Map(_ => copy.Name);
    }

    /// <summary>
    /// The digest of the SQL Server image a database runs in, as Docker reports it for the running dbchange-sql container (finding
    /// ARCH-07), for a copy whose server is the port that container publishes on this machine, however DBCHANGE_SQL or ~/.dbchange/sql.env
    /// reached it: the registry digest the image was pulled by (docker image inspect's RepoDigests), which Doctor.ImageDigest pins; or,
    /// for an image built or loaded on the machine, which no registry names, its image id. Null for a named environment, a copy on
    /// another server (LocalDB among them), and where Docker or the container does not answer.
    /// </summary>
    public static string? Image(SqlServer.Database target) => Image(target, Command.Run);

    internal static string? Image(SqlServer.Database target, Runner run)
    {
        if (target is not SqlServer.Copy copy || ServerName(copy.Connection) is not Result<Kernel.ServerName>.Ok { Value: { Host: var host } server } || host != Host.Localhost
            || Docker(run, ["container", "inspect", "--format", "{{.Image}} {{json .NetworkSettings.Ports}}", Container]) is not { } inspected
            || inspected.Trim().Split(' ', 2) is not [var id, var ports])
        {
            return null;
        }

        try
        {
            var published = JsonNode.Parse(ports)?["1433/tcp"]?.AsArray().Select(binding => (string?)binding?["HostPort"]).OfType<string>() ?? [];
            return !published.Any(port => Kernel.ServerName.Of("127.0.0.1," + port, Environment.MachineName) == server) ? null
                : Docker(run, ["image", "inspect", "--format", "{{json .RepoDigests}}", id]) is { } digests
                    && JsonNode.Parse(digests)?.AsArray().Select(d => ((string?)d)?.Split('@', 2)).OfType<string[]>()
                        .Where(d => d.Length == 2 && d[1].StartsWith("sha256:", StringComparison.Ordinal)).ToList() is { } pulled
                    ? (pulled.FirstOrDefault(d => d[0] == PinnedRepository) ?? pulled.FirstOrDefault())?[1] ?? id
                : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>What docker writes when it exits 0 within the doctor's program timeout; null when it is absent, does not answer or fails.</summary>
    private static string? Docker(Runner run, IReadOnlyList<string> arguments) =>
        run(new Command("docker", arguments, Command.ProbeTimeout), CancellationToken.None) is Ran.Exited { Code: 0, Output: var output } ? output : null;

    /// <summary>The container ci/sql.sh and ci/sql.ps1 run the local server in.</summary>
    public const string Container = "dbchange-sql";

    /// <summary>The repository of the pinned image (Doctor.SqlServerImage without its tag and digest), whose registry digest is preferred where an image was pulled from several.</summary>
    private static readonly string PinnedRepository = Doctor.SqlServerImage.Split('@')[0] is var reference ? reference[..reference.LastIndexOf(':')] : "";

    internal static Result<string> Server() =>
        Server(Environment.GetEnvironmentVariable("DBCHANGE_SQL"), LocalState.UserSqlEnv, Doctor.LocalDbInstalled(Command.Run));

    /// <summary>
    /// The local server, in the fixture's order: DBCHANGE_SQL, with sql.env not read; the container, when sql.env gives its port and
    /// password; LocalDB, when installed. A sql.env that cannot be read, or that gives a key twice, is local-server.missing naming the file.
    /// </summary>
    internal static Result<string> Server(string? dbChangeSql, string? sqlEnv, bool localDb) =>
        !string.IsNullOrEmpty(dbChangeSql) ? dbChangeSql
        : Settings(sqlEnv).Bind(env => env.GetValueOrDefault("MSSQL_SA_PASSWORD") is { Length: > 0 } password && env.GetValueOrDefault("DBCHANGE_SQL_PORT") is { Length: > 0 } port
                ? ConnectionString.Container(port, password)
            : localDb ? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true"
            : Result.Fail<string>(new Error("local-server.missing", "No local server: DBCHANGE_SQL is unset, " + (sqlEnv ?? "~/" + LocalState.Name + "/sql.env") + " gives no container's port and password, and LocalDB is not installed.",
                "Start Docker and run ci/sql.sh up, or ci/sql.ps1 up on Windows, or set DBCHANGE_SQL; then run dbchange doctor.")));

    /// <summary>sql.env's settings, one KEY=value per line as ci/sql.sh writes them; none where the file is absent, or where the user's profile folder is unknown (LocalState.UserSqlEnv null).</summary>
    private static Result<Dictionary<string, string>> Settings(string? sqlEnv)
    {
        const string rewrite = "Run ci/sql.sh down, then ci/sql.sh up (ci/sql.ps1 on Windows), which writes the file again; then run dbchange doctor.";
        string[] lines;
        try
        {
            lines = sqlEnv is not null && File.Exists(sqlEnv) ? File.ReadAllLines(sqlEnv) : [];
        }
        catch (Exception e) when (Write.FileSystemFailure(e))
        {
            return new Error("local-server.missing", sqlEnv + " cannot be read: " + e.Message.TrimEnd('.') + ".", rewrite);
        }

        var pairs = lines.Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2).ToList();
        return pairs.GroupBy(pair => pair[0], StringComparer.Ordinal).FirstOrDefault(key => key.Count() > 1) is { } twice
            ? new Error("local-server.missing", sqlEnv + " gives " + twice.Key + " twice.", rewrite)
            : pairs.ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.Ordinal);
    }

    /// <summary>
    /// A server as the registry records it and R15 compares it, read from its connection string on this machine. Only DBCHANGE_SQL, which
    /// the operator writes, can give one SqlClient reads nothing from: connection.malformed, configuration (exit 6) rather than a server
    /// that does not answer.
    /// </summary>
    internal static Result<Kernel.ServerName> ServerName(string server) =>
        ConnectionString.Parse("DBCHANGE_SQL", server, "Correct DBCHANGE_SQL, or unset it; then run dbchange doctor.").Map(ConnectionString.ServerOf);

    /// <summary>
    /// A copy on the server given, refused on a named environment's host (R15 against dbchange/environments.json, read here once); recorded
    /// with its server before its database is made, so a crash leaves a row to follow.
    /// </summary>
    internal static Result<SqlServer.Copy> Create(string repositoryRoot, string server, SqlServer.QueryLog log, Func<string, IPAddress[]>? resolve = null) =>
        ServerName(server).Bind(name => EnvironmentsFile.Read(repositoryRoot).Bind(environments => Unnamed(environments, repositoryRoot, name, resolve ?? Resolved))).Bind(name =>
        {
            var copy = new SqlServer.Copy(CopyName.Make(Environment.MachineName, Environment.ProcessId, BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4))), server, repositoryRoot);
            var row = new JsonObject
            {
                ["name"] = copy.Name.ToString(), ["server"] = name.ToString(), ["machine"] = copy.Name.Machine, ["pid"] = Environment.ProcessId,
                ["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            };
            return Change(repositoryRoot, rows => [.. rows, row]).Bind(_ => Run(copy, "CREATE DATABASE", Make, log).Match(
                made => Result.Ok(made),
                error => Change(repositoryRoot, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name.ToString())]).Bind(_ => Result.Fail<SqlServer.Copy>(error))));
        });

    /// <summary>
    /// copy: resolved against .dbchange/copies.json alone: the row holding the name, on a server R15 clears against the environments file as the verb
    /// read it, which the local server this machine names must still be. A name the registry does not hold is refused before the
    /// environments file is consulted.
    /// </summary>
    internal static Result<SqlServer.Copy> Registered(string repositoryRoot, CopyName name, Result<Environments> environmentsFile) => Registered(repositoryRoot, name, environmentsFile, Server, Resolved);

    internal static Result<SqlServer.Copy> Registered(string repositoryRoot, CopyName name, Result<Environments> environmentsFile, Func<Result<string>> chosen, Func<string, IPAddress[]> resolve) =>
        Rows(repositoryRoot).Bind(rows => rows.FirstOrDefault(r => (string?)r["name"] == name.ToString()) is not { } row
            ? new Error("copy.unregistered", new Target.RegisteredCopy(name) + " is no copy " + LocalState.CopiesName + " holds, and copy: names only a database dbchange made and recorded there.",
                "Name a copy that " + LocalState.CopiesName + " holds on this machine.")
            : environmentsFile.Bind(environments => Unnamed(environments, repositoryRoot, Kernel.ServerName.Of((string)row["server"]!, Environment.MachineName), resolve)).Bind(made => chosen().Bind(server => ServerName(server).Bind(now => now == made
                ? Result.Ok(new SqlServer.Copy(name, server, repositoryRoot))
                : new Error("copy.unregistered", new Target.RegisteredCopy(name) + " was made on another server than the local server this machine names now, so " + LocalState.CopiesName + " holds no such copy here.",
                    "Set DBCHANGE_SQL back to the server that made the copy, or make a new copy on this one.")))));

    /// <summary>
    /// R15: the server's host is none an environment of the environments file names as its host (DECISIONS.md, 2026-09-25), compared by spelling,
    /// then by address; this machine is every loopback address and each of its own. Every environment is compared, its reference
    /// resolving on this machine or not. Where a reference does resolve, its server must be on the host the environments file names, since R15
    /// compares that host (environments.host); a reference SqlClient reads no connection string from, or whose file cannot be examined, is an
    /// error, R15 failing closed.
    /// </summary>
    internal static Result<Kernel.ServerName> Unnamed(Environments environments, string repositoryRoot, Kernel.ServerName server, Func<string, IPAddress[]> resolve) =>
        Result.All(environments.All.Select(environment => SqlServer.DataSource(environment, repositoryRoot).Bind(source => source is { } read && read.Host != environment.Host
            ? new Error("environments.host", SqlServer.EnvironmentDatabase.Subject(environment) + " names a server on another host than " + environment.Host + ", the host "
                + EnvironmentsFile.Json + " gives " + environment.Target + ", and dbchange makes no copy on the host the environments file gives.",
                "Write " + environment.Target + "'s host in " + EnvironmentsFile.Json + " as its connection string spells the server, or correct the connection string.")
            : Result.Ok(environment))))
        .Bind(compared =>
        {
            var addresses = new Lazy<HashSet<IPAddress>>(() => Addresses(server.Host, resolve));
            return compared.Where(e => e.Host == server.Host).Concat(compared.Where(e => e.Host != server.Host && Addresses(e.Host, resolve).Overlaps(addresses.Value))).FirstOrDefault() is { } named
                ? new Error("copy.named-host", "The local server is on " + named.Host + ", the host " + named.Target + " runs on, and a copy is made only where no named environment lives.",
                    "Point DBCHANGE_SQL at a local SQL Server, or unset it and run ci/sql.sh up (ci/sql.ps1 up on Windows).")
                : Result.Ok(server);
        });

    /// <summary>A host's addresses: this machine's, for a host spelled as this machine or LocalDB or resolving to any of this machine's; else what it resolves to.</summary>
    private static HashSet<IPAddress> Addresses(Host host, Func<string, IPAddress[]> resolve)
    {
        HashSet<IPAddress> found = [.. (host == Host.Localhost || host == Host.LocalDb ? [IPAddress.Loopback] : IPAddress.TryParse(host.ToString(), out var literal) ? [literal] : resolve(host.ToString())).Select(Plain)];
        return found.Any(a => IPAddress.IsLoopback(a) || Local.Value.Contains(a)) ? [.. found, .. Local.Value] : found;
    }

    /// <summary>An IPv4 address mapped into IPv6, as the IPv4 address it is.</summary>
    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>What DNS resolves a host name to, waited on for <see cref="DnsTimeout"/>; none when it does not answer.</summary>
    private static IPAddress[] Resolved(string host)
    {
        try
        {
            var lookup = Dns.GetHostAddressesAsync(host);
            return lookup.Wait(DnsTimeout) ? lookup.Result : [];
        }
        catch (Exception e) when (e is AggregateException or SocketException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// A statement about the copy's database, through the one statement path (io/SqlServer.Query): run against master on its server, on
    /// a connection of its own outside SqlClient's pool, with the copy's name as @name, waiting up to <see cref="DatabaseStatementTimeout"/>.
    /// </summary>
    private static Result<SqlServer.Copy> Run(SqlServer.Copy copy, string site, string statement, SqlServer.QueryLog log) =>
        SqlServer.Query(copy, new SqlServer.Statement(site, statement)
        {
            Timeout = DatabaseStatementTimeout, Catalog = "master", Pooled = false, Parameters = [("@name", copy.Name.ToString())],
        }, log, _ => copy);

    /// <summary>The registry's rows, none when it is absent, each naming its copy and the server it was made on. A write replaces the file whole, so a reader sees the rows before a change or after it.</summary>
    private static Result<List<JsonObject>> Rows(string repositoryRoot)
    {
        var path = new LocalState(repositoryRoot).Copies;
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
            return new Error("registry.unreadable", LocalState.CopiesName + " under " + repositoryRoot + " is not the registry dbchange writes; its text is withheld.",
                "Drop the copies it lists with DROP DATABASE, then delete " + LocalState.CopiesName + ".");
        }
    }

    /// <summary>The registry changed under a lock this process alone holds while it reads, changes and writes the file back through io/Write.</summary>
    private static Result<List<JsonObject>> Change(string repositoryRoot, Func<List<JsonObject>, List<JsonObject>> change)
    {
        var state = new LocalState(repositoryRoot);
        return state.Made(state.Folder).Bind(_ => Held(state.CopiesLock)).Bind(held =>
        {
            using (held)
            {
                return Rows(repositoryRoot).Map(change).Bind(rows => Write.Text(state.Copies, Json.Text(new JsonObject { ["copies"] = new JsonArray([.. rows]) })).Map(_ => rows));
            }
        });
    }

    /// <summary>How long a registry change waits for another dbchange process's: a change takes milliseconds.</summary>
    private static readonly TimeSpan RegistryTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The registry's lock file, taken for this process alone through io/FileLock; another holder is waited out for <see cref="RegistryTimeout"/>.</summary>
    private static Result<FileLock> Held(string path) => FileLock.Take(path, RegistryTimeout);
}
