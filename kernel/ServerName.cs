using System;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// The machine a SQL Server runs on, as R15 compares machines (V3_MILESTONES.md §4 row 16): lower case, with no protocol, port or
/// instance, and localhost for this machine however a connection spells it. <see cref="ServerName"/> reads the host inside a
/// connection's data source, keeping any spelling SqlClient accepts; <see cref="Of"/> reads the host estate/environments.json gives an
/// environment, which is a host name (letters, digits, '-', '_' and '.', a letter or digit first and last), an IP address, or
/// (localdb). Two hosts are one here when they are spelled alike; io/LocalServer also asks DNS whether two spellings share an
/// address. default(Host) is not a host.
/// </summary>
public readonly record struct Host : IComparable<Host>
{
    private static readonly Regex Written = new(
        @"\A(?:[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?|\(localdb\)|[0-9a-f]*:[0-9a-f.]*:[0-9a-f:.]*)\z", RegexOptions.CultureInvariant);

    private readonly string? _text;

    internal Host(string text) => _text = text;

    /// <summary>This machine: every spelling SqlClient reads as the local server (none, localhost, 127.0.0.1, ::1, '.', (local), the machine's own name).</summary>
    public static Host Localhost { get; } = new("localhost");

    /// <summary>SQL Server Express LocalDB, which runs on this machine and takes no host name.</summary>
    public static Host LocalDb { get; } = new("(localdb)");

    /// <summary>
    /// The host estate/environments.json gives an environment: trimmed and in lower case, an IPv6 address without its brackets, and
    /// localhost for 127.0.0.1 and ::1. A port, an instance, a protocol or white space is environments.host, since they belong to the
    /// connection string; the error leads with <paramref name="subject"/> and quotes nothing.
    /// </summary>
    public static Result<Host> Of(string subject, string? text) =>
        text?.Trim().Trim('[', ']').ToLowerInvariant() is { } host && Written.IsMatch(host)
            ? Local(host) ? Localhost : new Host(host)
            : new Error("environments.host", subject + " is no host name, IP address or (localdb); a port, an instance, a protocol and white space belong to the environment's connection string.",
                "Write the host alone, such as dev-sql.corp.example, and keep the port or the instance in the connection string.");

    /// <summary>Ordinally, by spelling.</summary>
    public int CompareTo(Host other) => string.CompareOrdinal(_text, other._text);

    public override string ToString() => _text ?? throw new InvalidOperationException("default(Host) is not a host; make one with Host.Of or ServerName.Of.");

    /// <summary>Whether a host spelled in lower case is this machine by a spelling that holds on every machine.</summary>
    internal static bool Local(string host) => host is "" or "localhost" or "127.0.0.1" or "::1" or "." or "(local)";
}

/// <summary>
/// A server as a connection's data source names it, in the one spelling .estate/copies.json records and R15 compares: its
/// <see cref="Host"/>, then its port or instance as the data source gives it, in lower case and without white space, such as
/// localhost,11433, dev-sql\sql2022 or (localdb)\mssqllocaldb. The protocol (tcp:, np:, lpc:, admin:) is dropped and a named pipe
/// \\host\pipe\… is read for its host. The spelling is the one the registry's rows carried before this type, so they still resolve,
/// and a server name read from its own spelling is itself. default(ServerName) is not a server name.
/// </summary>
public readonly record struct ServerName : IComparable<ServerName>
{
    private static readonly Regex Protocol = new(@"\A(?:tcp|np|lpc|admin):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Blank = new(@"\s", RegexOptions.CultureInvariant);

    private readonly string? _rest;

    private ServerName(Host host, string rest) => (Host, _rest) = (host, rest);

    /// <summary>The machine the server runs on.</summary>
    public Host Host { get; }

    /// <summary>The server <paramref name="dataSource"/> names, read on the machine named <paramref name="machineName"/>, whose own name, in any case, is localhost.</summary>
    public static ServerName Of(string dataSource, string machineName)
    {
        var bare = Protocol.Replace(dataSource.Trim(), "", 1);
        var pipe = bare.StartsWith(@"\\", StringComparison.Ordinal);
        var named = pipe ? bare[2..].Split('\\')[0] : bare.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ? "(localdb)" : bare;
        var host = named.Split(',')[0].Split('\\')[0].Trim().Trim('[', ']').ToLowerInvariant();
        var rest = bare.IndexOfAny([',', '\\'], pipe ? 2 : 0) is >= 0 and var at ? bare[at..] : "";
        return new(Host.Local(host) || host == machineName.ToLowerInvariant() ? Host.Localhost : new Host(host), Blank.Replace(rest, "").ToLowerInvariant());
    }

    /// <summary>Ordinally, by spelling.</summary>
    public int CompareTo(ServerName other) => string.CompareOrdinal(ToString(), other.ToString());

    public override string ToString() => Host.ToString() + _rest;
}
