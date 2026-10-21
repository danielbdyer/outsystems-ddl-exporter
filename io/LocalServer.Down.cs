using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// What synthetic-copy status and down read of the registry and of the local server (V3_MILESTONES.md WP 3.6): the registry's rows of a
/// synthetic copy's name, and the databases of that form the local server holds, read from master once R15 clears the server and its
/// linked servers as Standing clears them. Nothing here makes or drops a database; down drops each copy through Drop.
/// </summary>
public static partial class LocalServer
{
    /// <summary>The databases of a synthetic copy's name on the server, by name alone, read from master.</summary>
    private const string SyntheticNames = "SELECT name FROM sys.databases WHERE name LIKE N'dbchange[_]synthetic[_]%' ORDER BY name;";

    /// <summary>This machine as a registry row's machine field writes it.</summary>
    internal static string ThisMachine => CopyName.Make(Environment.MachineName, 0, 0).Machine;

    /// <summary>The rows of .dbchange/copies.json whose name is a synthetic copy's, in the registry's order.</summary>
    internal static Result<IReadOnlyList<(CopyName Name, JsonObject Row)>> SyntheticRows(string repositoryRoot) =>
        Rows(repositoryRoot).Map(rows => (IReadOnlyList<(CopyName, JsonObject)>)
        [
            .. rows.Select(row => (Named: CopyName.Of("a copy " + LocalState.CopiesName + " registers", (string?)row["name"]), Row: row))
                .Where(r => r.Named is Result<CopyName>.Ok { Value.IsSynthetic: true })
                .Select(r => (((Result<CopyName>.Ok)r.Named).Value, r.Row)),
        ]);

    /// <summary>
    /// The databases of a synthetic copy's name the server given holds, once R15 clears it against dbchange/environments.json before
    /// anything connects, and then its linked servers, read from master before any other statement, as Standing clears a server.
    /// </summary>
    internal static Result<IReadOnlyList<CopyName>> SyntheticDatabases(string repositoryRoot, string server, SqlServer.QueryLog log)
    {
        var (master, resolve) = (new SqlServer.Copy(CopyName.Synthetic(Fingerprint.Of("master"), null), server, repositoryRoot),
            Once(Resolved));
        return ServerName(server).Bind(named => EnvironmentsFile.Read(repositoryRoot)
            .Bind(environments => Unnamed(environments, repositoryRoot, named, resolve)
                .Bind(cleared => environments.All.Count == 0 ? Result.Ok(cleared)
                    : Linked(master, log).Bind(linked => Unlinked(environments, cleared, linked, resolve)))))
            .Bind(_ => Run(master, "synthetic copies", SyntheticNames, log, rows => (IReadOnlyList<CopyName>)
            [
                .. rows.Select(r => CopyName.Of("a database of the local server", r[0] as string)).OfType<Result<CopyName>.Ok>()
                    .Select(named => named.Value).Where(named => named.IsSynthetic),
            ]));
    }
}
