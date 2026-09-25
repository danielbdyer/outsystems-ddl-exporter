using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Estate.Tests;

/// <summary>
/// estate/posture.json as a record, field for field in the file's own names, written under an estate's root; a key left null is left
/// out of the file. A malformed posture, which no record can hold, is written as its text by <see cref="WriteText"/>.
/// </summary>
internal sealed record PostureFile(IReadOnlyDictionary<string, PostureFile.Environment> Environments, string? ScratchServer = null)
{
    /// <summary>The profile the golden project commits, at the path an estate keeps it.</summary>
    public const string Pipeline = "estate/profiles/pipeline.publish.xml";

    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>One environment of the posture, in the file's own names: its host, its connection reference and its profile, and whatever else it gives.</summary>
    internal sealed record Environment(string Connection, string Host = "dev-sql", string Profile = Pipeline, string? Classification = null, string? ConfirmedBy = null,
        string? ConfirmedOn = null, IReadOnlyList<string>? Readers = null, IReadOnlyDictionary<string, SqlCmd>? Sqlcmd = null, string? Metamodel = null);

    /// <summary>A SQLCMD value as the posture gives it: a reference (env:NAME or file:path), or a literal marked non-sensitive.</summary>
    internal abstract record SqlCmd
    {
        private SqlCmd()
        {
        }

        internal sealed record Reference(string Text) : SqlCmd;

        internal sealed record Literal(string Text, bool Sensitive = false) : SqlCmd;
    }

    /// <summary>A posture naming one environment.</summary>
    public static PostureFile Of(string name, Environment environment) => new(new Dictionary<string, Environment> { [name] = environment });

    /// <summary>The environment dev, its connection the reference given, on the host given.</summary>
    public static PostureFile Dev(string connection, string host = "dev-sql") => Of("dev", new Environment(connection, host));

    /// <summary>The posture written as estate/posture.json under <paramref name="estateRoot"/>, whose estate/profiles/ folder is made; the root.</summary>
    public string WriteTo(string estateRoot) => WriteText(estateRoot, Json());

    /// <summary>The text written as estate/posture.json under <paramref name="estateRoot"/>, as given, UTF-8 without a byte-order mark; the root.</summary>
    public static string WriteText(string estateRoot, string json)
    {
        Directory.CreateDirectory(Path.Combine(estateRoot, "estate", "profiles"));
        File.WriteAllText(Path.Combine(estateRoot, "estate", "posture.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return estateRoot;
    }

    /// <summary>The posture as JSON, each key in the file's own name and a null one left out.</summary>
    public string Json()
    {
        var environments = new JsonObject();
        foreach (var (name, e) in Environments)
        {
            var entry = new JsonObject
            {
                ["host"] = e.Host, ["classification"] = e.Classification, ["confirmedBy"] = e.ConfirmedBy, ["confirmedOn"] = e.ConfirmedOn,
                ["readers"] = e.Readers is null ? null : new JsonArray([.. e.Readers.Select(r => (JsonNode?)r)]),
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

        var posture = new JsonObject { ["environments"] = environments };
        if (ScratchServer is not null)
        {
            posture["scratchServer"] = ScratchServer;
        }

        return posture.ToJsonString(Written) + "\n";
    }
}
