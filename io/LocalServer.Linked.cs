using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// R15 over the local server's linked servers (DECISIONS.md, 2026-10-21). A statement on a copy reaches every server the copy's local
/// server links to: a seed or a trigger the base's publish runs, and a declared witness, which runs as one batch with triggers firing,
/// through a four-part name, OPENQUERY or a view or a function written over either. So before synthetic-copy up makes or plants anything,
/// it reads each server sys.servers lists beside the local one (server_id 0), a linked server or a remote server an older SQL Server
/// keeps, and refuses the run as copy.named-host where one names a host an environment runs on, compared as R15 compares the local
/// server's own host: by spelling, then by address.
/// </summary>
public static partial class LocalServer
{
    /// <summary>The servers the local server links to, read from master; their login mappings (sys.linked_logins) are not read.</summary>
    private const string LinkedServers = "SELECT name, data_source, provider_string FROM sys.servers WHERE server_id <> 0;";

    /// <summary>The keys under which a provider string names its server: OLE DB's and ODBC's, compared ignoring case.</summary>
    private static readonly HashSet<string> ServerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server", "Data Source", "Address", "Addr", "Network Address",
    };

    /// <summary>
    /// A server sys.servers lists beside the local one: its name, its data source, and its provider string, which an OLE DB or an ODBC
    /// linked server with no data source names its server in, and which can hold a password, so no message quotes it.
    /// </summary>
    internal sealed record LinkedServer(string Name, string? DataSource, string? ProviderString);

    /// <summary>The servers the local server links to, read on master through io/SqlServer.Query.</summary>
    private static Result<IReadOnlyList<LinkedServer>> Linked(SqlServer.Copy copy, SqlServer.QueryLog log) =>
        Run(copy, "linked servers", LinkedServers, log, rows => (IReadOnlyList<LinkedServer>)
            [.. rows.Select(r => new LinkedServer(r[0] as string ?? "", r[1] as string, r[2] as string))]);

    /// <summary>
    /// The local server cleared where no linked server of it names a host an environment of the environments file runs on: by its data
    /// source, read as a connection's data source is read (ServerName.Of), or by the server its provider string names under Server, Data
    /// Source, Address, Addr or Network Address; else copy.named-host, naming the first such linked server and the environment.
    /// </summary>
    internal static Result<Kernel.ServerName> Unlinked(Environments environments, Kernel.ServerName server,
        IReadOnlyList<LinkedServer> linked, Func<string, IPAddress[]> resolve) =>
        linked.SelectMany(l => HostsOf(l).Select(host => (Linked: l, Host: host, Environment: On(environments.All, host, resolve))))
            .FirstOrDefault(l => l.Environment is not null) is { Environment: { } named } found
            ? new Error("copy.named-host",
                "The local server holds the linked server [" + found.Linked.Name.Replace("]", "]]", StringComparison.Ordinal)
                    + "], which names " + found.Host + (found.Host == named.Host ? "" : ", an alias of " + named.Host) + ", the host "
                    + named.Target + " runs on, so a seed, a trigger or a declared witness on a copy there can read or write that"
                    + " environment's rows; a copy is made only where no named environment is reachable.",
                "Drop the linked server from the local server with sp_dropserver, or point DBCHANGE_SQL at a local SQL Server that"
                    + " links to no host an environment runs on.")
            : Result.Ok(server);

    /// <summary>A resolver that asks <paramref name="resolve"/> once a host name, so one run looks each host up once.</summary>
    internal static Func<string, IPAddress[]> Once(Func<string, IPAddress[]> resolve)
    {
        var answered = new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase);
        return host => answered.TryGetValue(host, out var found) ? found : answered[host] = resolve(host);
    }

    /// <summary>The hosts a linked server names: its data source's, and the server's of each key of its provider string that names one.
    /// </summary>
    private static IEnumerable<Host> HostsOf(LinkedServer linked) =>
        new[] { linked.DataSource }.Concat((linked.ProviderString ?? "").Split(';').Select(pair => pair.Split('=', 2))
                .Where(pair => pair.Length == 2 && ServerKeys.Contains(pair[0].Trim())).Select(pair => pair[1].Trim().Trim('{', '}', '"')))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => Kernel.ServerName.Of(source!, Environment.MachineName).Host);

    /// <summary>The first environment that runs on <paramref name="host"/>, by spelling, then by address; null where none does.</summary>
    private static NamedEnvironment? On(IEnumerable<NamedEnvironment> environments, Host host, Func<string, IPAddress[]> resolve)
    {
        var addresses = new Lazy<HashSet<IPAddress>>(() => Addresses(host, resolve));
        return environments.FirstOrDefault(e => e.Host == host)
            ?? environments.FirstOrDefault(e => Addresses(e.Host, resolve).Overlaps(addresses.Value));
    }
}
