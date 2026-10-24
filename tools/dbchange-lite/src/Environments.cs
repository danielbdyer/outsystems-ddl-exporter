using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace DbChangeLite;

public sealed record NamedEnvironment(string Name, string Host, string Connection, string Profile, IReadOnlyDictionary<string, string> SqlCmd,
    IReadOnlyList<string> ReaderGroups, bool NeverRead, int DeployOrder);

public sealed record Environments(IReadOnlyList<NamedEnvironment> All)
{
    public NamedEnvironment Named(string name) => All.FirstOrDefault(e => e.Name == name)
        ?? throw Stop.Of("bad-arguments", "environments.unknown", name, EnvironmentsFile.Name + " names no environment " + name + ".",
            "Name one of " + string.Join(", ", All.Select(e => e.Name)) + ".");

    public IReadOnlyList<NamedEnvironment> InDeployOrder(IEnumerable<string> names) =>
        names.Select(Named).OrderBy(e => e.DeployOrder).ThenBy(e => All.ToList().IndexOf(e)).ToList();
}

public static class EnvironmentsFile
{
    public const string Name = "dbchange/environments.json";

    public static Environments Read(string root)
    {
        var path = Path.Combine(root, Name);
        if (!File.Exists(path))
        {
            throw Stop.Of("configuration-refused", "environments.missing", Name, "The repository root " + root + " holds no " + Name + ".",
                "Run from a checkout of the SSDT repository, or add " + Name + " with each environment's host, connection reference and profile.");
        }

        using var document = Parse(path);
        var all = new List<NamedEnvironment>();
        foreach (var property in document.RootElement.GetProperty("environments").EnumerateObject())
        {
            var e = property.Value;
            var connection = Text(e, "connection") ?? throw Refused(property.Name, "connection", "has no connection reference");
            if (!IsReference(connection))
            {
                throw Refused(property.Name, "connection", "holds a literal connection; a reference is env:NAME or file:path, and the value stays outside the repository");
            }

            var sqlcmd = e.TryGetProperty("sqlcmd", out var values)
                ? values.EnumerateObject().ToDictionary(v => v.Name, v => SqlCmdValue(property.Name, v), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var readers = e.TryGetProperty("readerGroups", out var groups) ? groups.EnumerateArray().Select(g => g.GetString() ?? "").ToList() : [];
            all.Add(new NamedEnvironment(property.Name, Text(e, "host") ?? throw Refused(property.Name, "host", "names no host"), connection,
                Text(e, "profile") ?? throw Refused(property.Name, "profile", "names no publish profile"), sqlcmd, readers,
                e.TryGetProperty("neverRead", out var never) && never.ValueKind == JsonValueKind.True,
                e.TryGetProperty("deployOrder", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : int.MaxValue));
        }

        return new Environments(all);
    }

    public static string Resolve(string root, string reference)
    {
        if (reference.StartsWith("env:", StringComparison.Ordinal))
        {
            return Environment.GetEnvironmentVariable(reference[4..]) is { Length: > 0 } value ? value
                : throw Stop.Of("configuration-refused", "environments.reference-unset", reference, "The environment variable " + reference[4..] + " is not set.",
                    "Set " + reference[4..] + " to the connection string in this shell, outside any file the repository holds.");
        }

        var path = Path.GetFullPath(Path.Combine(root, reference[5..]));
        return File.Exists(path) ? File.ReadAllText(path).Trim()
            : throw Stop.Of("configuration-refused", "environments.reference-missing", reference, "The file " + reference[5..] + " does not exist.",
                "Write the connection string to " + reference[5..] + ", readable by this login alone, and keep it out of git.");
    }

    public static bool IsReference(string value) => (value.StartsWith("env:", StringComparison.Ordinal) || value.StartsWith("file:", StringComparison.Ordinal))
        && !value.Contains(';', StringComparison.Ordinal) && !value.Contains('=', StringComparison.Ordinal);

    private static JsonDocument Parse(string path)
    {
        var text = File.ReadAllText(path);
        if (text.Contains("password=", StringComparison.OrdinalIgnoreCase) || text.Contains("pwd=", StringComparison.OrdinalIgnoreCase))
        {
            throw Stop.Of("configuration-refused", "environments.literal-credential", Name, Name + " holds a password.",
                "Remove it, and reference the connection as env:NAME or file:path.");
        }

        try
        {
            var document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("environments", out var environments) || environments.ValueKind != JsonValueKind.Object)
            {
                throw Stop.Of("unparsed-input", "environments.shape", Name, Name + " has no environments object.", "Write { \"environments\": { \"dev\": { … } } }.");
            }

            return document;
        }
        catch (JsonException e)
        {
            throw Stop.Of("unparsed-input", "environments.json", Name, Name + " is not JSON: " + e.Message, "Correct the file at the position the message names.");
        }
    }

    private static string SqlCmdValue(string environment, JsonProperty value) => value.Value.ValueKind switch
    {
        JsonValueKind.String when IsReference(value.Value.GetString()!) => value.Value.GetString()!,
        JsonValueKind.String => value.Value.GetString()!,
        JsonValueKind.Object when value.Value.TryGetProperty("literal", out var literal) => literal.GetString() ?? "",
        _ => throw Refused(environment, "sqlcmd." + value.Name, "is neither a string nor an object with a literal"),
    };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Stop Refused(string environment, string key, string what) => Stop.Of("configuration-refused", "environments." + key.Replace('.', '-'),
        environment, Name + ": environment " + environment + "'s " + key + " " + what + ".", "Correct " + key + " of " + environment + " in " + Name + ".");
}

public static class Hosts
{
    public static string Of(string dataSource)
    {
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            return "(localdb)";
        }

        var end = host.IndexOfAny([',', '\\']);
        return end < 0 ? host : host[..end];
    }

    public static bool IsLocal(string dataSource)
    {
        var host = Of(dataSource);
        if (host is "(localdb)" or "." or "(local)" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Addresses(host) is { Count: > 0 } addresses && addresses.All(IPAddress.IsLoopback);
    }

    public static bool Same(string a, string b)
    {
        var (first, second) = (Of(a), Of(b));
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase) || (IsLocal(first) && IsLocal(second)))
        {
            return true;
        }

        return Addresses(first).Intersect(Addresses(second)).Any(address => !IPAddress.IsLoopback(address));
    }

    private static IReadOnlyList<IPAddress> Addresses(string host)
    {
        if (host is "(localdb)" or "." or "(local)")
        {
            return [IPAddress.Loopback];
        }

        try
        {
            return Dns.GetHostAddresses(host).Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6).ToList();
        }
        catch (SocketException)
        {
            return [];
        }
    }
}
