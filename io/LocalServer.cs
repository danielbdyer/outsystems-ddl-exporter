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
/// DBCHANGE_SQL names, else the SQL Server ~/.dbchange/sql.env names, else LocalDB, chosen inside io, so no caller holds its login or
/// makes a copy anywhere else, and started by ci/sql.sh up or ci/sql.ps1 up, never by io; Create, which names a copy for this host and
/// process, records it and its server in .dbchange/copies.json and makes its database; Drop, which removes both; and the registry,
/// against which alone copy: resolves, on the server its row records. A local server on the host an environment names in
/// dbchange/environments.json, by spelling or by address, is refused before anything connects (R15), and synthetic-copy up holds the
/// local server's linked servers to the same comparison before it makes anything (LocalServer.Linked.cs). Before a copy is made, this
/// host's copies registered on the same server more than a day earlier are dropped (the sweep, LocalServer.Sweep.cs), and a copy is backed
/// up into the copy cache and restored from it (LocalServer.Cache.cs). Its CREATE, DROP, BACKUP and RESTORE go through io/SqlServer.Query,
/// the one statement path, whose first connect to the local server is retried once (FirstConnect).
/// </summary>
public static partial class LocalServer
{
    /// <summary>
    /// The copy's database made with no guard: a database of the name already standing is one this registry did not make (for the
    /// synthetic copy's name, which is the same in every checkout of the same inputs, another checkout's), which SQL Server refuses with
    /// Msg 1801, so the copy never takes it over and no later Drop or sweep drops it.
    /// </summary>
    private const string Make = "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);";

    /// <summary>
    /// SQL Server's refusals of a make because a database of the copy's name already stands, measured on SQL Server 2022 Express on
    /// 2026-10-20: CREATE DATABASE's Msg 1801; RESTORE's Msg 3154, a backup of another database than the one standing; Msg 3159, a
    /// standing database of the backup's own family whose log tail is not backed up; and Msg 1834, a file the restore writes that the
    /// standing database holds open.
    /// </summary>
    private static readonly HashSet<int> Taken = [1801, 3154, 3159, 1834];

    /// <summary>
    /// The copy's database dropped, its sessions ended first, under an application lock in master that this session alone holds
    /// (sp_getapplock, 'dbchange drop ' and the name), so two drops of one copy at once, by two runs' sweeps or by two Drop calls, run one
    /// after the other and the second finds the database gone. Without the lock the second fails in two ways: its ALTER DATABASE or DROP
    /// DATABASE reaches a database the first dropped after the second's DB_ID check (Msg 3701, 5011, 5069), or the first's ROLLBACK
    /// IMMEDIATE kills it at severity 21 (Msg 596), which closes its connection before its CATCH runs. A database left RESTORING by a
    /// RESTORE that failed or whose run was killed takes DROP DATABASE alone, since SQL Server refuses ALTER DATABASE on it (Msg 5052).
    /// </summary>
    private const string Unmake = "DECLARE @lock nvarchar(255) = N'dbchange drop ' + @name, @held int; "
        + "EXEC @held = sp_getapplock @Resource = @lock, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = -1; "
        + "IF @held < 0 THROW 50000, N'sp_getapplock refused the lock that keeps two drops of one copy apart.', 1; "
        + "BEGIN TRY IF DB_ID(@name) IS NOT NULL BEGIN "
        + "DECLARE @sql nvarchar(max) = CASE WHEN DATABASEPROPERTYEX(@name, 'Status') = N'RESTORING' THEN N'' "
        + "ELSE N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' END "
        + "+ N'DROP DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql); END END TRY "
        + "BEGIN CATCH EXEC sp_releaseapplock @Resource = @lock, @LockOwner = N'Session'; THROW; END CATCH; "
        + "EXEC sp_releaseapplock @Resource = @lock, @LockOwner = N'Session';";

    /// <summary>
    /// How long CREATE DATABASE and DROP DATABASE may wait. Each waits for the database locks that other copies being made or
    /// dropped on the same server hold, and LocalDB on the four-core Windows CI runner has taken longer than SqlClient's default of
    /// 30 seconds for a DROP while parallel test classes made and dropped their own databases.
    /// </summary>
    internal static readonly TimeSpan DatabaseStatementTimeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// How long the one outbound lookup dbchange makes may take (VALUES.md X5): DNS for a named environment's host, which R15 compares with
    /// the local server's and with each host its linked servers name; a lookup that does not answer in time reads as an address it does
    /// not have.
    /// </summary>
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

    /// <summary>This machine's addresses as R15 reads them: loopback, and each its network interfaces hold.</summary>
    private static readonly Lazy<HashSet<IPAddress>> Local = new(() =>
    {
        try
        {
            return [IPAddress.Loopback, IPAddress.IPv6Loopback,
                .. NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => Plain(u.Address))];
        }
        catch (NetworkInformationException)
        {
            return [IPAddress.Loopback, IPAddress.IPv6Loopback];
        }
    });

    /// <summary>
    /// The local server, from the sources given, as the registry records it and R15 compares it (localhost,11434); nothing of its login.
    /// </summary>
    public static Result<Kernel.ServerName> ServerName(string? dbChangeSql, string? sqlEnv, bool localDb) =>
        Server(dbChangeSql, sqlEnv, localDb).Bind(ServerName);

    /// <summary>
    /// A copy on this machine's local server, refused on a named environment's host (R15); its CREATE DATABASE goes to the run's log.
    /// </summary>
    public static Result<SqlServer.Copy> Create(string repositoryRoot, SqlServer.QueryLog log) =>
        Server().Bind(server => Create(repositoryRoot, server, log));

    /// <summary>
    /// The copy's database dropped, its sessions ended first, then its row; a database already gone is no error. The DROP DATABASE goes
    /// to the run's log.
    /// </summary>
    public static Result<CopyName> Drop(SqlServer.Copy copy, SqlServer.QueryLog log)
    {
        SqlConnection.ClearPool(new SqlConnection(copy.Connection));
        return Run(copy, "DROP DATABASE", Unmake, log).Bind(Unregistered).Map(_ => copy.Name);
    }

    /// <summary>The copy's row removed from .dbchange/copies.json.</summary>
    private static Result<SqlServer.Copy> Unregistered(SqlServer.Copy copy) =>
        Change(copy.Root, rows => [.. rows.Where(r => (string?)r["name"] != copy.Name.ToString())]).Map(_ => copy);

    /// <summary>
    /// The local server this machine would use, as it was chosen: the one DBCHANGE_SQL names; the SQL Server ci/sql.sh up runs, whose port
    /// and password ~/.dbchange/sql.env gives; or LocalDB's default instance, which ci/sql.ps1 up starts. Each case prints its kind alone,
    /// never its connection, which can hold a password; the doctor reports the one copies use, since both ask <see cref="Choice"/>.
    /// io starts none of them (DECISIONS.md, 2026-10-19).
    /// </summary>
    public abstract record Chosen
    {
        private Chosen(string connection) => Connection = connection;

        internal string Connection { get; }

        public sealed record Variable : Chosen
        {
            internal Variable(string connection)
                : base(connection)
            {
            }

            public override string ToString() => "DBCHANGE_SQL";
        }

        public sealed record SqlEnv : Chosen
        {
            internal SqlEnv(string connection)
                : base(connection)
            {
            }

            public override string ToString() => "the SQL Server ~/" + LocalState.Name + "/sql.env names";
        }

        public sealed record LocalDb : Chosen
        {
            internal LocalDb()
                : base(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true")
            {
            }

            public override string ToString() => "LocalDB MSSQLLocalDB";
        }
    }

    internal static Result<string> Server() =>
        Choice(Environment.GetEnvironmentVariable("DBCHANGE_SQL"), LocalState.UserSqlEnv, () => Doctor.LocalDbInstalled(Command.Run))
            .Map(chosen => chosen.Connection);

    /// <summary>The local server's connection, as <see cref="Choice"/> chooses it, LocalDB's install given.</summary>
    internal static Result<string> Server(string? dbChangeSql, string? sqlEnv, bool localDb) =>
        Choice(dbChangeSql, sqlEnv, () => localDb).Map(chosen => chosen.Connection);

    /// <summary>
    /// The local server, in the fixture's order: DBCHANGE_SQL, with sql.env not read; the SQL Server sql.env names, when it gives its port
    /// and password; LocalDB, when installed, which <paramref name="localDbInstalled"/> is asked only when neither gives one, since asking
    /// runs sqllocaldb. None of the three is started here. A sql.env that cannot be read, or that gives a key twice, is
    /// local-server.missing naming the file.
    /// </summary>
    internal static Result<Chosen> Choice(string? dbChangeSql, string? sqlEnv, Func<bool> localDbInstalled) =>
        !string.IsNullOrEmpty(dbChangeSql) ? new Chosen.Variable(dbChangeSql)
        : Settings(sqlEnv).Bind(env =>
            env.GetValueOrDefault("MSSQL_SA_PASSWORD") is { Length: > 0 } password
            && env.GetValueOrDefault("DBCHANGE_SQL_PORT") is { Length: > 0 } port
                ? new Chosen.SqlEnv(ConnectionString.SqlEnv(port, password))
            : localDbInstalled() ? new Chosen.LocalDb()
            : Result.Fail<Chosen>(new Error("local-server.missing",
                "No local server: DBCHANGE_SQL is unset, " + (sqlEnv ?? "~/" + LocalState.Name + "/sql.env")
                + " gives no server's port and password, and LocalDB is not installed.",
                "Run ci/sql.sh up, or ci/sql.ps1 up on Windows, or set DBCHANGE_SQL; then run dbchange doctor.")));

    /// <summary>
    /// sql.env's settings, one KEY=value per line as ci/sql.sh writes them; none where the file is absent, or where the user's profile
    /// folder is unknown (LocalState.UserSqlEnv null).
    /// </summary>
    private static Result<Dictionary<string, string>> Settings(string? sqlEnv)
    {
        const string rewrite =
            "Run ci/sql.sh down, then ci/sql.sh up (ci/sql.ps1 on Windows), which writes the file again; then run dbchange doctor.";
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
        ConnectionString.Parse("DBCHANGE_SQL", server, "Correct DBCHANGE_SQL, or unset it; then run dbchange doctor.")
            .Map(ConnectionString.ServerOf);

    /// <summary>
    /// A copy on the server given, its database made empty by CREATE DATABASE (<see cref="Made"/>); <paramref name="now"/> is the clock the
    /// sweep and the row read, the time now unless a test moves it; <paramref name="named"/> is the synthetic copy's name, where the copy
    /// is it, and a run's copy takes a name of its own.
    /// </summary>
    internal static Result<SqlServer.Copy> Create(string repositoryRoot, string server, SqlServer.QueryLog log,
        Func<string, IPAddress[]>? resolve = null, DateTimeOffset? now = null, CopyName? named = null)
    {
        var standing = false;
        return Made(repositoryRoot, server, log, resolve ?? Resolved, now ?? DateTimeOffset.UtcNow,
            copy => Making(copy, "CREATE DATABASE", Make, log, [], made => standing = made), _ => standing, named);
    }

    /// <summary>
    /// A copy on the server given, refused on a named environment's host (R15 against dbchange/environments.json, read here once); this
    /// host's copies registered on that server more than a day before <paramref name="now"/> swept first; recorded with its server before
    /// <paramref name="make"/> makes its database, so a crash leaves a row to follow. A failed make removes the row; where the failure can
    /// have left a database (<paramref name="leaves"/>: a RESTORE not refused as copy.foreign, or a CREATE whose connection opened and
    /// that SQL Server did not refuse), the database is dropped first, and a row whose database could not be dropped stays for the sweep;
    /// a make SQL Server refused, copy.foreign among them, leaves standing a database of the name that another checkout or no registry
    /// made. The copy takes <paramref name="named"/> where it is given, the synthetic copy's name, and else a name of its own; its row
    /// names this machine either way. The local server's linked servers are held to R15 before anything is swept or made (Cleared).
    /// </summary>
    private static Result<SqlServer.Copy> Made(string repositoryRoot, string server, SqlServer.QueryLog log,
        Func<string, IPAddress[]> resolve, DateTimeOffset now, Func<SqlServer.Copy, Result<SqlServer.Copy>> make, Func<Error, bool> leaves,
        CopyName? named = null) =>
        ServerName(server).Bind(name =>
        {
            var copy = new SqlServer.Copy(named ?? CopyName.Make(Environment.MachineName, Environment.ProcessId,
                BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4))), server, repositoryRoot);
            var row = new JsonObject
            {
                ["name"] = copy.Name.ToString(), ["server"] = name.ToString(),
                ["machine"] = CopyName.Make(Environment.MachineName, 0, 0).Machine, ["pid"] = Environment.ProcessId,
                ["created"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            };
            return EnvironmentsFile.Read(repositoryRoot)
                .Bind(environments => Cleared(environments, repositoryRoot, name, copy, log, resolve))
                .Bind(_ => Swept(repositoryRoot, server, name, now, log))
                .Bind(_ => Change(repositoryRoot, rows => [.. rows, row])).Bind(_ => make(copy).Match(
                    made => Result.Ok(made),
                    error => (leaves(error) ? Run(copy, "DROP DATABASE", Unmake, log) : Result.Ok(copy))
                        .Match(Unregistered, _ => Result.Ok(copy)).Bind(_ => Result.Fail<SqlServer.Copy>(error))));
        });

    /// <summary>
    /// A statement that makes the copy's database, sent as <see cref="Run{T}"/> sends one. Where the copy takes the synthetic copy's name,
    /// a refusal SQL Server gives because a database of that name already stands (<see cref="Taken"/>) is copy.foreign: another checkout
    /// made it between up's look at master and this statement. Any other refusal is the error its number maps to, as Run maps it.
    /// <paramref name="standing"/> hears, once the statement ended, whether a failure can have left the database: its connection opened,
    /// and SQL Server neither refused the statement below severity 20 nor ran it to the end; a timeout or a broken connection may have.
    /// </summary>
    private static Result<SqlServer.Copy> Making(SqlServer.Copy copy, string site, string statement, SqlServer.QueryLog log,
        IReadOnlyList<(string Name, string Value)> parameters, Action<bool>? standing = null)
    {
        var (opened, refused) = (false, false);
        var made = SqlServer.Query(copy, new SqlServer.Statement(site, statement)
        {
            Timeout = DatabaseStatementTimeout, Catalog = "master", Pooled = false,
            Parameters = [("@name", copy.Name.ToString()), .. parameters],
        }, log, _ => Result.Ok(copy), failed =>
        {
            refused = !failed.TimedOut;
            return copy.Name.IsSynthetic && Taken.Contains(failed.Number)
                ? Foreign(copy.Name, ConnectionString.ServerOf(new SqlConnectionStringBuilder(copy.Connection)))
                : Result.Fail<SqlServer.Copy>(copy.ErrorOf(failed.Number, failed.Message ?? "", fatal: false, opened: true));
        }, sent => opened = sent).Bind(made => made);
        standing?.Invoke(made is Result<SqlServer.Copy>.Failed && opened && !refused);
        return made;
    }

    /// <summary>
    /// copy: resolved against .dbchange/copies.json alone: the row holding the name, on a server R15 clears against the environments file
    /// as the verb read it, which the local server this machine names must still be, and whose linked servers R15 then clears (Cleared),
    /// read into <paramref name="log"/>. A name the registry does not hold is refused before the environments file is consulted.
    /// </summary>
    internal static Result<SqlServer.Copy> Registered(string repositoryRoot, CopyName name, Result<Environments> environmentsFile,
        SqlServer.QueryLog? log = null) =>
        Registered(repositoryRoot, name, environmentsFile, Server, Resolved, log);

    internal static Result<SqlServer.Copy> Registered(string repositoryRoot, CopyName name, Result<Environments> environmentsFile,
        Func<Result<string>> chosen, Func<string, IPAddress[]> resolve, SqlServer.QueryLog? log = null) =>
        Rows(repositoryRoot).Bind(rows => rows.FirstOrDefault(r => (string?)r["name"] == name.ToString()) is not { } row
            ? new Error("copy.unregistered",
                new Target.RegisteredCopy(name) + " is no copy " + LocalState.CopiesName
                + " holds, and copy: names only a database dbchange made and recorded there.",
                "Name a copy that " + LocalState.CopiesName + " holds on this machine.")
            : environmentsFile.Bind(environments =>
                Unnamed(environments, repositoryRoot, Kernel.ServerName.Of((string)row["server"]!, Environment.MachineName), resolve)
                .Bind(made => chosen().Bind(server => ServerName(server).Bind(now => now == made
                ? Linked(environments, now, new SqlServer.Copy(name, server, repositoryRoot),
                    log ?? SqlServer.QueryLog.Start(repositoryRoot), resolve)
                : new Error("copy.unregistered",
                    new Target.RegisteredCopy(name) + " was made on another server than the local server this machine names now, so "
                    + LocalState.CopiesName + " holds no such copy here.",
                    "Set DBCHANGE_SQL back to the server that made the copy, or make a new copy on this one."))))));

    /// <summary>
    /// R15: the server's host is none an environment of the environments file names as its host (DECISIONS.md, 2026-09-25), compared by
    /// spelling, then by address; this machine is every loopback address and each of its own. Every environment is compared, its reference
    /// resolving on this machine or not. Where a reference does resolve, its server must be on the host the environments file names, since
    /// R15 compares that host (environments.host); a reference SqlClient reads no connection string from, or whose file cannot be examined,
    /// is an error, R15 failing closed.
    /// </summary>
    internal static Result<Kernel.ServerName> Unnamed(Environments environments, string repositoryRoot, Kernel.ServerName server,
        Func<string, IPAddress[]> resolve) =>
        Result.All(environments.All.Select(environment =>
            SqlServer.DataSource(environment, repositoryRoot).Bind(source => source is { } read && read.Host != environment.Host
            ? new Error("environments.host",
                SqlServer.EnvironmentDatabase.Subject(environment) + " names a server on another host than " + environment.Host
                + ", the host " + EnvironmentsFile.Json + " gives " + environment.Target
                + ", and dbchange makes no copy on the host the environments file gives.",
                "Write " + environment.Target + "'s host in " + EnvironmentsFile.Json
                    + " as its connection string spells the server, or correct the connection string.")
            : Result.Ok(environment))))
        .Bind(compared => On(compared, server.Host, resolve) is { } named
            ? new Error("copy.named-host",
                "The local server is on " + named.Host + ", the host " + named.Target
                + " runs on, and a copy is made only where no named environment lives.",
                "Point DBCHANGE_SQL at a local SQL Server, or unset it and run ci/sql.sh up (ci/sql.ps1 up on Windows).")
            : Result.Ok(server));

    /// <summary>
    /// A host's addresses: this machine's, for a host spelled as this machine or LocalDB or resolving to any of this machine's; else what
    /// it resolves to.
    /// </summary>
    private static HashSet<IPAddress> Addresses(Host host, Func<string, IPAddress[]> resolve)
    {
        HashSet<IPAddress> found = [.. (host == Host.Localhost || host == Host.LocalDb ? [IPAddress.Loopback]
            : IPAddress.TryParse(host.ToString(), out var literal) ? [literal] : resolve(host.ToString())).Select(Plain)];
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
    /// a connection of its own outside SqlClient's pool, with the copy's name as @name, waiting up to
    /// <see cref="DatabaseStatementTimeout"/>.
    /// </summary>
    private static Result<SqlServer.Copy> Run(SqlServer.Copy copy, string site, string statement, SqlServer.QueryLog log) =>
        Run(copy, site, statement, log, _ => copy);

    /// <summary>A statement about the copy's database as <see cref="Run(SqlServer.Copy, string, string, SqlServer.QueryLog)"/> sends one,
    /// with the further parameters given, its rows read into the answer.</summary>
    private static Result<T> Run<T>(SqlServer.Copy copy, string site, string statement, SqlServer.QueryLog log,
        Func<IReadOnlyList<IReadOnlyList<object?>>, T> answer, params (string Name, string Value)[] parameters) =>
        SqlServer.Query(copy, new SqlServer.Statement(site, statement)
        {
            Timeout = DatabaseStatementTimeout, Catalog = "master", Pooled = false,
            Parameters = [("@name", copy.Name.ToString()), .. parameters],
        }, log, answer);

    /// <summary>
    /// The registry's rows, none when it is absent, each naming its copy and the server it was made on. A write replaces the file whole, so
    /// a reader sees the rows before a change or after it.
    /// </summary>
    private static Result<List<JsonObject>> Rows(string repositoryRoot)
    {
        var path = new LocalState(repositoryRoot).Copies;
        try
        {
            return !File.Exists(path) ? new List<JsonObject>()
                : JsonNode.Parse(File.ReadAllText(path))?["copies"] is JsonArray rows
                    && rows.All(r => r is JsonObject o
                        && o["name"]?.GetValueKind() == JsonValueKind.String && o["server"]?.GetValueKind() == JsonValueKind.String)
                    ? rows.Select(r => (JsonObject)r!.DeepClone()).ToList()
                    : throw new JsonException("not the registry");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new Error("registry.unreadable",
                LocalState.CopiesName + " under " + repositoryRoot + " is not the registry dbchange writes; its text is withheld.",
                "Drop the copies it lists with DROP DATABASE, then delete " + LocalState.CopiesName + ".");
        }
    }

    /// <summary>
    /// The registry changed under a lock this process alone holds while it reads, changes and writes the file back through io/Write.
    /// </summary>
    private static Result<List<JsonObject>> Change(string repositoryRoot, Func<List<JsonObject>, List<JsonObject>> change)
    {
        var state = new LocalState(repositoryRoot);
        return state.Made(state.Folder).Bind(_ => Held(state.CopiesLock)).Bind(held =>
        {
            using (held)
            {
                return Rows(repositoryRoot).Map(change)
                    .Bind(rows => Write.Text(state.Copies, Json.Text(new JsonObject { ["copies"] = new JsonArray([.. rows]) }))
                        .Map(_ => rows));
            }
        });
    }

    /// <summary>How long a registry change waits for another dbchange process's: a change takes milliseconds.</summary>
    private static readonly TimeSpan RegistryTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The registry's lock file, taken for this process alone through io/FileLock; another holder is waited out for
    /// <see cref="RegistryTimeout"/>.
    /// </summary>
    private static Result<FileLock> Held(string path) => FileLock.Take(path, RegistryTimeout);
}
