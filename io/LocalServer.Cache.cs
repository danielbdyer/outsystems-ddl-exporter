using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbChange.Kernel;
using Microsoft.Data.SqlClient;

namespace DbChange.Io;

/// <summary>
/// The copy cache (V3_MILESTONES.md WP 3.4, §4 row 5): a copy backed up under a key, its base's fingerprint, and restored into a new
/// registered copy, so a fresh copy of a base costs a RESTORE where a publish from scratch would cost the whole schema. A backup is written
/// where the server can write it and a restore on this machine can read it: LocalDB runs as the caller and writes into .dbchange/cache/; a
/// SQL Server on this machine runs under its own account (the mssql user on Linux, a service account on Windows), which cannot write into
/// the caller's checkout, so it writes into its own default backup folder (SERVERPROPERTY('InstanceDefaultBackupPath')); a server on
/// another machine writes to that machine's disk, so it has no cache, and its base is published from scratch with the note
/// cache.unavailable. .dbchange/cache/backups.json records each backup, by key and server, with the file the server wrote.
/// </summary>
public static partial class LocalServer
{
    /// <summary>A backup into a folder given: COPY_ONLY, so it starts no backup chain; no COMPRESSION, which Express and LocalDB
    /// refuse.</summary>
    private const string BackupInto = "BACKUP DATABASE @name TO DISK = @path WITH COPY_ONLY, INIT, FORMAT, CHECKSUM; SELECT @path;";

    /// <summary>A backup into the server's own default backup folder, the file named by @file; the path the server wrote is the
    /// answer.</summary>
    private static readonly string BackupIntoServerFolder =
        "DECLARE @folder nvarchar(4000) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)); "
        + "DECLARE @path nvarchar(4000) = @folder + " + Separator("@folder") + " + @file; "
        + "BACKUP DATABASE @name TO DISK = @path WITH COPY_ONLY, INIT, FORMAT, CHECKSUM; SELECT @path;";

    /// <summary>
    /// How long a run waits for another run that holds a base's lock while it publishes and backs the base up: a publish of the SSDT
    /// repository's schema from scratch, which S4 times, many times over.
    /// </summary>
    private static readonly TimeSpan BaseTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Where a backup of a copy is written: by LocalDB into the copy cache, or by a SQL Server on this machine into its own
    /// default backup folder. The cases are closed.</summary>
    internal abstract record BackupFolder
    {
        private BackupFolder()
        {
        }

        internal sealed record Cache(string Path) : BackupFolder;

        internal sealed record Server : BackupFolder;
    }

    /// <summary>A backup the cache holds: the key it was taken under, the server that wrote it and the file the server wrote.</summary>
    public sealed record Backup(Fingerprint Key, Kernel.ServerName Server, string Path);

    /// <summary>A copy fresh from a base: restored from the base's backup, or published from scratch; and the notes on why it was not
    /// restored or not backed up.</summary>
    public sealed record Fresh(SqlServer.Copy Copy, bool Restored, IReadOnlyList<Finding> Notes);

    /// <summary>
    /// Where <paramref name="server"/> writes a backup a restore on this machine reads again: LocalDB into .dbchange/cache/, a server on
    /// this machine, by spelling or by address, into its own folder; a server on another machine, copy.uncached.
    /// </summary>
    internal static Result<BackupFolder> BackupFolderOf(string repositoryRoot, Kernel.ServerName server,
        Func<string, IPAddress[]> resolve) =>
        server.Host == Host.LocalDb ? new BackupFolder.Cache(new LocalState(repositoryRoot).Cache)
        : Addresses(server.Host, resolve).Overlaps(Local.Value) ? new BackupFolder.Server()
        : new Error("copy.uncached",
            "The local server " + server + " runs on another machine, which writes a backup to its own disk, where no restore on this "
            + "machine reads it, so a copy's base is not cached there.",
            "Unset DBCHANGE_SQL, or point it at a SQL Server on this machine, to restore a copy's base from "
                + LocalState.Name + "/cache/.");

    /// <summary>
    /// The copy backed up under <paramref name="key"/> where its server writes a backup (<see cref="BackupFolderOf"/>), and recorded in
    /// .dbchange/cache/backups.json in place of an earlier backup under the same key on the same server; its BACKUP DATABASE goes to the
    /// run's log. A server on another machine is copy.uncached, and nothing is written.
    /// </summary>
    internal static Result<Backup> BackUp(SqlServer.Copy copy, Fingerprint key, SqlServer.QueryLog log)
    {
        var server = ConnectionString.ServerOf(new SqlConnectionStringBuilder(copy.Connection));
        var state = new LocalState(copy.Root);
        return BackupFolderOf(copy.Root, server, Resolved).Bind(folder => folder is BackupFolder.Cache cache
            ? state.Made(cache.Path).Bind(_ => Run(copy, "BACKUP DATABASE", BackupInto, log, Written,
                ("@path", Path.Combine(cache.Path, Short(key) + ".bak"))))
            : Run(copy, "BACKUP DATABASE", BackupIntoServerFolder, log, Written, ("@file", ServerFile(copy.Root, key))))
            .Bind(path => path is null ? new Error("server.failed", copy.Target + " wrote its backup to no path it reports.",
                    "Check that SERVERPROPERTY('InstanceDefaultBackupPath') names a folder on " + server + ", then run the step again.")
                : Indexed(copy.Root, new Backup(key, server, path)));
    }

    /// <summary>A new registered copy on this machine's local server, restored from the backup the cache holds under the key.</summary>
    public static Result<SqlServer.Copy> Restore(string repositoryRoot, Fingerprint key, SqlServer.QueryLog log) =>
        Server().Bind(server => Restore(repositoryRoot, server, key, log));

    /// <summary>
    /// A new registered copy on the server given, made as Create makes one (R15, the sweep, the row written first) and restored from the
    /// backup .dbchange/cache/backups.json holds under the key on that server, each file moved to the server's default data or log folder
    /// under the new copy's name; its RESTORE goes to the run's log. A key the cache holds no backup of there is copy.uncached, before
    /// anything connects. <paramref name="now"/> is the clock the sweep and the row read, as Create takes it.
    /// </summary>
    internal static Result<SqlServer.Copy> Restore(string repositoryRoot, string server, Fingerprint key, SqlServer.QueryLog log,
        DateTimeOffset? now = null) =>
        ServerName(server).Bind(name => Backed(repositoryRoot, key, name).Bind(backup => backup is null
            ? new Error("copy.uncached",
                LocalState.Name + "/cache/backups.json holds no backup of the base " + Short(key) + " on " + name + ".",
                "Publish the base to a new copy and back it up, which records the backup in " + LocalState.Name + "/cache/backups.json.")
            : Made(repositoryRoot, server, log, Resolved, now ?? DateTimeOffset.UtcNow, copy => Restored(copy, backup.Path, log),
                leavesDatabase: true)));

    /// <summary>A fresh copy of the base <paramref name="key"/> names on this machine's local server (<see cref="FromBase(string, string,
    /// Fingerprint, Func{SqlServer.Copy, Result{SqlServer.Copy}}, SqlServer.QueryLog, Func{Kernel.ServerName, Result{BackupFolder}}?,
    /// DateTimeOffset?)"/>).</summary>
    public static Result<Fresh> FromBase(string repositoryRoot, Fingerprint key, Func<SqlServer.Copy, Result<SqlServer.Copy>> publish,
        SqlServer.QueryLog log) =>
        Server().Bind(server => FromBase(repositoryRoot, server, key, publish, log));

    /// <summary>
    /// A fresh copy of a base: restored from the base's backup where the cache holds one; else a new copy that <paramref name="publish"/>
    /// publishes the base to, backed up under the key. A run holds the base's lock, .dbchange/cache/&lt;key&gt;.lock, from its look at the
    /// cache to its backup, so two runs reaching one base at once publish it once and the second restores it. A server on another machine
    /// has no cache: each copy is published from scratch, with the note cache.unavailable. A backup the server can no longer read
    /// (cache.unreadable) or cannot write (cache.unwritable) is a note, and the copy is published from scratch all the same.
    /// <paramref name="folder"/> chooses where the server writes a backup, <see cref="BackupFolderOf"/> unless a test gives another
    /// choice; <paramref name="now"/> is the clock each copy's sweep and row read, as Create takes it.
    /// </summary>
    internal static Result<Fresh> FromBase(string repositoryRoot, string server, Fingerprint key,
        Func<SqlServer.Copy, Result<SqlServer.Copy>> publish, SqlServer.QueryLog log,
        Func<Kernel.ServerName, Result<BackupFolder>>? folder = null, DateTimeOffset? now = null) =>
        ServerName(server).Bind(name => (folder ?? (named => BackupFolderOf(repositoryRoot, named, Resolved)))(name).Match(
            _ => Cached(repositoryRoot, server, name, key, publish, log, now ?? DateTimeOffset.UtcNow),
            uncached => Published(repositoryRoot, server, publish, log, now ?? DateTimeOffset.UtcNow).Map(copy => new Fresh(copy, false,
                [Finding.Note("cache.unavailable", copy.Target.ToString(), uncached.Message + " The base was published from scratch.")]))));

    private static Result<Fresh> Cached(string repositoryRoot, string server, Kernel.ServerName name, Fingerprint key,
        Func<SqlServer.Copy, Result<SqlServer.Copy>> publish, SqlServer.QueryLog log, DateTimeOffset now)
    {
        var state = new LocalState(repositoryRoot);
        Result<Fresh> Built(IReadOnlyList<Finding> notes) => Published(repositoryRoot, server, publish, log, now).Map(copy =>
            BackUp(copy, key, log).Match(_ => new Fresh(copy, false, notes), error => new Fresh(copy, false,
                [.. notes, Finding.Note("cache.unwritable", copy.Target.ToString(), error.Message + " The base is not cached.")])));
        return state.Made(state.Cache).Bind(_ => FileLock.Take(state.BaseLock(Short(key)), BaseTimeout)).Bind(held =>
        {
            using (held)
            {
                return Backed(repositoryRoot, key, name).Bind(backup => backup is null ? Built([])
                    : Restore(repositoryRoot, server, key, log, now).Match(copy => new Fresh(copy, true, []),
                        error => error.Code == "server.failed"
                            ? Built([Finding.Note("cache.unreadable", backup.Path,
                                error.Message + " The base was published from scratch.")])
                            : Result.Fail<Fresh>(error)));
            }
        });
    }

    /// <summary>A new copy the base is published to; a copy whose publish failed is dropped, and the publish's error returned.</summary>
    private static Result<SqlServer.Copy> Published(string repositoryRoot, string server,
        Func<SqlServer.Copy, Result<SqlServer.Copy>> publish, SqlServer.QueryLog log, DateTimeOffset now) =>
        Create(repositoryRoot, server, log, now: now).Bind(copy => publish(copy).Match(Result.Ok, error =>
        {
            Drop(copy, log);   // a copy its DROP failed on keeps its row, and the sweep drops it
            return Result.Fail<SqlServer.Copy>(error);
        }));

    /// <summary>
    /// The backup's files listed (RESTORE FILELISTONLY: the logical name, the type, the file's id), then the backup restored into the
    /// copy's database, each file moved under the copy's name into the server's default data folder, or its log folder for the log.
    /// </summary>
    private static Result<SqlServer.Copy> Restored(SqlServer.Copy copy, string path, SqlServer.QueryLog log) =>
        Run(copy, "RESTORE FILELISTONLY", "RESTORE FILELISTONLY FROM DISK = @path;", log,
            rows => rows.Select(r => (Logical: (string)r[0]!, Type: (string)r[2]!, Id: Convert.ToInt64(r[6], CultureInfo.InvariantCulture)))
                .ToList(), ("@path", path))
        .Bind(files => Run(copy, "RESTORE DATABASE",
            "DECLARE @data nvarchar(4000) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), "
            + "@log nvarchar(4000) = CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)); "
            + "SET @data += " + Separator("@data") + "; SET @log += " + Separator("@log") + "; "
            + string.Concat(files.Select((f, i) => string.Create(CultureInfo.InvariantCulture,
                $"DECLARE @to{i} nvarchar(4000) = {(f.Type == "L" ? "@log" : "@data")} + @name + N'_{f.Id}{Extension(f.Type, f.Id)}'; ")))
            + "RESTORE DATABASE @name FROM DISK = @path WITH "
            + string.Join(", ", files.Select((_, i) => string.Create(CultureInfo.InvariantCulture, $"MOVE @file{i} TO @to{i}")))
            + ", CHECKSUM, RECOVERY;", log, _ => copy,
            [("@path", path), .. files.Select((f, i) => (string.Create(CultureInfo.InvariantCulture, $"@file{i}"), f.Logical))]));

    /// <summary>A file's extension by its type in RESTORE FILELISTONLY: the primary data file .mdf, another .ndf, the log .ldf; a
    /// FILESTREAM or full-text container is a folder.</summary>
    private static string Extension(string type, long id) => type switch
    {
        "D" => id == 1 ? ".mdf" : ".ndf",
        "L" => ".ldf",
        _ => "",
    };

    /// <summary>The separator a folder SERVERPROPERTY names takes before a file's name: none after one it ends in, else the one it
    /// uses.</summary>
    private static string Separator(string folder) =>
        "CASE WHEN RIGHT(" + folder + ", 1) IN (N'/', N'\\') THEN N'' WHEN CHARINDEX(N'\\', " + folder + ") > 0 THEN N'\\' ELSE N'/' END";

    /// <summary>The backup's file in a server's own folder, which every checkout on the machine shares: the key's and the checkout's first
    /// digits, so two checkouts never write one file.</summary>
    private static string ServerFile(string repositoryRoot, Fingerprint key) => "dbchange_" + Short(key) + "_"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(repositoryRoot))))[..8].ToLowerInvariant() + ".bak";

    /// <summary>A key's first sixteen hexadecimal digits, as a file's name carries it.</summary>
    private static string Short(Fingerprint key) => key.ToString()[..16];

    private static string? Written(IReadOnlyList<IReadOnlyList<object?>> rows) => rows is [[string path]] ? path : null;

    /// <summary>The backup the cache holds under the key on the server, or none.</summary>
    private static Result<Backup?> Backed(string repositoryRoot, Fingerprint key, Kernel.ServerName server) =>
        Entries(repositoryRoot).Map(entries => entries
            .Where(e => (string?)e["key"] == key.ToString() && (string?)e["server"] == server.ToString() && e["path"] is JsonValue)
            .Select(e => new Backup(key, server, (string)e["path"]!)).FirstOrDefault());

    /// <summary>The backup recorded in .dbchange/cache/backups.json, under its lock, in place of an earlier one under its key and server.
    /// </summary>
    private static Result<Backup> Indexed(string repositoryRoot, Backup backup)
    {
        var state = new LocalState(repositoryRoot);
        return state.Made(state.Cache).Bind(_ => Held(state.BackupsLock)).Bind(held =>
        {
            using (held)
            {
                return Entries(repositoryRoot).Bind(entries => Write.Text(state.Backups, Json.Text(new JsonObject
                {
                    ["backups"] = new JsonArray([.. entries.Where(e => (string?)e["key"] != backup.Key.ToString()
                        || (string?)e["server"] != backup.Server.ToString()), new JsonObject
                    {
                        ["key"] = backup.Key.ToString(), ["server"] = backup.Server.ToString(), ["path"] = backup.Path,
                    }]),
                }))).Map(_ => backup);
            }
        });
    }

    /// <summary>The index's entries, none when it is absent. A write replaces the file whole, so a reader sees it before a change or
    /// after.</summary>
    private static Result<List<JsonObject>> Entries(string repositoryRoot)
    {
        var path = new LocalState(repositoryRoot).Backups;
        try
        {
            return !File.Exists(path) ? new List<JsonObject>()
                : JsonNode.Parse(File.ReadAllText(path))?["backups"] is JsonArray entries && entries.All(e => e is JsonObject)
                    ? entries.Select(e => (JsonObject)e!.DeepClone()).ToList()
                    : throw new JsonException("not the index");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new Error("registry.unreadable",
                LocalState.Name + "/cache/backups.json under " + repositoryRoot
                    + " is not the index dbchange writes; its text is withheld.",
                "Delete " + LocalState.Name + "/cache/backups.json, and each base is published from scratch once more.");
        }
    }
}
