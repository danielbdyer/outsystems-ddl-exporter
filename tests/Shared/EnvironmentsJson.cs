using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DbChange.Tests;

/// <summary>
/// dbchange/environments.json as a record, field for field in the file's own names, written under a repository root; a key left null is left
/// out of the file. A malformed environments file, which no record can hold, is written as its text by <see cref="WriteText"/>.
/// </summary>
internal sealed record EnvironmentsJson(IReadOnlyDictionary<string, EnvironmentsJson.Environment> Environments, string? LocalServer = null)
{
    /// <summary>The profile the golden project commits, at the path the SSDT repository keeps it.</summary>
    public const string Pipeline = "dbchange/profiles/pipeline.publish.xml";

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>One environment of the environments file, in the file's own names: its host, its connection reference and its profile, and whatever else it gives.</summary>
    internal sealed record Environment(string Connection, string Host = "dev-sql", string Profile = Pipeline, string? Classification = null, string? ConfirmedBy = null,
        string? ConfirmedOn = null, IReadOnlyList<string>? ReaderGroups = null, IReadOnlyDictionary<string, SqlCmd>? Sqlcmd = null, string? Metamodel = null);

    /// <summary>A SQLCMD value as the environments file gives it: a reference (env:NAME or file:path), or a literal marked non-sensitive.</summary>
    internal abstract record SqlCmd
    {
        private SqlCmd()
        {
        }

        internal sealed record Reference(string Text) : SqlCmd;

        internal sealed record Literal(string Text, bool Sensitive = false) : SqlCmd;
    }

    /// <summary>An environments file naming one environment.</summary>
    public static EnvironmentsJson Of(string name, Environment environment) => new(new Dictionary<string, Environment> { [name] = environment });

    /// <summary>The environment dev, its connection the reference given, on the host given.</summary>
    public static EnvironmentsJson Dev(string connection, string host = "dev-sql") => Of("dev", new Environment(connection, host));

    /// <summary>The environments file written as dbchange/environments.json under <paramref name="repositoryRoot"/>, whose dbchange/profiles/ folder is made; the root.</summary>
    public string WriteTo(string repositoryRoot) => WriteText(repositoryRoot, Json());

    /// <summary>The text written as dbchange/environments.json under <paramref name="repositoryRoot"/>, as given, UTF-8 without a byte-order mark; the root.</summary>
    public static string WriteText(string repositoryRoot, string json)
    {
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "dbchange", "profiles"));
        File.WriteAllText(Path.Combine(repositoryRoot, "dbchange", "environments.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return repositoryRoot;
    }

    /// <summary>The environments file as JSON, each key in the file's own name and a null one left out.</summary>
    public string Json()
    {
        var environments = new JsonObject();
        foreach (var (name, e) in Environments)
        {
            var entry = new JsonObject
            {
                ["host"] = e.Host, ["classification"] = e.Classification, ["confirmedBy"] = e.ConfirmedBy, ["confirmedOn"] = e.ConfirmedOn,
                ["readerGroups"] = e.ReaderGroups is null ? null : new JsonArray([.. e.ReaderGroups.Select(r => (JsonNode?)r)]),
                ["connection"] = e.Connection, ["profile"] = e.Profile,
                ["sqlcmd"] = e.Sqlcmd is null ? null : new JsonObject(e.Sqlcmd.Select(v => KeyValuePair.Create(v.Key, v.Value switch
                {
                    SqlCmd.Reference reference => (JsonNode?)reference.Text,
                    SqlCmd.Literal literal => new JsonObject { ["literal"] = literal.Text, ["sensitive"] = literal.Sensitive },
                    _ => null,
                }))),
                ["metamodel"] = e.Metamodel,
            };
            foreach (var absent in entry.Where(p => p.Value is null).Select(p => p.Key).ToList())
            {
                entry.Remove(absent);
            }

            environments[name] = entry;
        }

        var environmentsFile = new JsonObject { ["environments"] = environments };
        if (LocalServer is not null)
        {
            environmentsFile["localServer"] = LocalServer;
        }

        return environmentsFile.ToJsonString(Written) + "\n";
    }
}
