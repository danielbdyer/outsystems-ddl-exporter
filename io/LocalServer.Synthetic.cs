using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// The synthetic copy in the registry (V3_MILESTONES.md WP 3.5, §4 row 13): a copy whose name is the synthetic form of CopyName, made
/// through Create, Restore or FromBase like any copy, R15 and the sweep first and its row first, and dropped through Drop. Its row also
/// records the inputs it was planted from, in full (the base's and the existing data's fingerprints, whose first sixteen digits its name
/// carries; the change's commit; the declared witnesses' fingerprint; dbchange's version), and whether its build ended, so synthetic-copy
/// up reads it as up to date, as left half built, or as built from other inputs. synthetic-copy, the target, is the synthetic copy whose
/// build ended last on the local server this machine names, the last such row of .dbchange/copies.json, which <see cref="Mark"/> moves to
/// the end. A database of the synthetic copy's name the registry does not hold is another checkout's, and is never taken over or dropped.
/// </summary>
public static partial class LocalServer
{
    /// <summary>
    /// The synthetic copy of a name as this checkout and the local server hold it, once R15 clears the server given against
    /// dbchange/environments.json, before anything connects, and then its linked servers, read from master before any other statement
    /// (LocalServer.Linked.cs; none are read where the file names no environment): the copy on that server, the registry's row of it
    /// there, null where the registry holds none, and whether the server holds its database, asked of master. A server on a named
    /// environment's host, or one linked to such a host, is copy.named-host, so no row of it answers current and no database there is
    /// dropped or made.
    /// </summary>
    internal static Result<(SqlServer.Copy Copy, JsonObject? Row, bool Exists)> Standing(string repositoryRoot, CopyName name,
        string server, SqlServer.QueryLog log)
    {
        var (copy, resolve) = (new SqlServer.Copy(name, server, repositoryRoot), Once(Resolved));
        return ServerName(server).Bind(named => EnvironmentsFile.Read(repositoryRoot)
            .Bind(environments => Cleared(environments, repositoryRoot, named, copy, log, resolve)))
            .Bind(cleared => Rows(repositoryRoot).Bind(rows => Run(copy, "DB_ID",
                    "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END;", log, found => found is [[1]])
                .Map(exists => (copy, rows.LastOrDefault(r => (string?)r["name"] == name.ToString()
                    && (string?)r["server"] == cleared.ToString()), exists))));
    }

    /// <summary>The fields given set on the copy's row, which moves to the registry's end: synthetic-copy names the last ready
    /// one.</summary>
    internal static Result<SqlServer.Copy> Mark(SqlServer.Copy copy, JsonObject fields) =>
        Change(copy.Root, rows =>
        {
            var row = rows.LastOrDefault(r => (string?)r["name"] == copy.Name.ToString());
            if (row is null)
            {
                return rows;
            }

            foreach (var (key, value) in fields)
            {
                row[key] = value?.DeepClone();
            }

            return [.. rows.Where(r => !ReferenceEquals(r, row)), row];
        }).Map(_ => copy);

    /// <summary>
    /// copy.foreign: the local server holds a database of the synthetic copy's name that this checkout's registry does not hold, so another
    /// checkout of the repository made it from the same inputs, and this one neither takes it over nor drops it.
    /// </summary>
    internal static Error Foreign(CopyName name, Kernel.ServerName server) => new("copy.foreign",
        server + " holds the database " + name + ", the synthetic copy's name for these inputs, which " + LocalState.CopiesName
            + " of this checkout does not hold: another checkout made it, and dbchange neither takes it over nor drops it.",
        "Run dbchange synthetic-copy up from the checkout that made it, or drop the database from there, then run the verb again here.");

    /// <summary>synthetic-copy resolved against the registry: the synthetic copy whose build ended last on this machine's local
    /// server.</summary>
    internal static Result<SqlServer.Copy> Synthetic(string repositoryRoot, Result<Environments> environmentsFile,
        SqlServer.QueryLog? log = null) =>
        Synthetic(repositoryRoot, environmentsFile, Server, Resolved, log);

    /// <summary>
    /// synthetic-copy against the registry: the last row whose name is the synthetic form and whose build ended, made on the local server
    /// this machine names now, which R15 clears against the environments file, its linked servers included, as Registered does;
    /// copy.synthetic-absent where none is.
    /// </summary>
    internal static Result<SqlServer.Copy> Synthetic(string repositoryRoot, Result<Environments> environmentsFile,
        Func<Result<string>> chosen, Func<string, IPAddress[]> resolve, SqlServer.QueryLog? log = null) =>
        chosen().Bind(server => ServerName(server).Bind(now => Rows(repositoryRoot).Bind(rows =>
            rows.Where(r => (string?)r["server"] == now.ToString() && r["ready"] is JsonValue ready
                    && ready.TryGetValue(out bool built) && built)
                .Select(r => CopyName.Of("a copy " + LocalState.CopiesName + " registers", (string?)r["name"]))
                .OfType<Result<CopyName>.Ok>().LastOrDefault(named => named.Value.IsSynthetic) is not { Value: var name }
                ? new Error("copy.synthetic-absent",
                    "synthetic-copy names the synthetic copy, and " + LocalState.CopiesName + " holds none built on " + now
                        + ", the local server this machine names.",
                    "Run dbchange synthetic-copy up, then name synthetic-copy again.")
                : environmentsFile.Bind(environments => Cleared(environments, repositoryRoot, now,
                    new SqlServer.Copy(name, server, repositoryRoot), log ?? SqlServer.QueryLog.Start(repositoryRoot), resolve))
                    .Map(_ => new SqlServer.Copy(name, server, repositoryRoot)))));
}
