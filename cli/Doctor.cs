using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

/// <summary>The verbs' bodies, one file per verb.</summary>
public static partial class Verbs
{
    /// <summary>What doctor adds to the envelope: each prerequisite, the item of the closed set it examines, what it found, and its remedy when the item is missing.</summary>
    public static JsonObject DoctorContent => new()
    {
        ["prerequisites"] = Render.List(Render.Record(new()
        {
            ["item"] = Render.Enum(Io.Doctor.Item.All.Select(item => (JsonNode?)item.Name)), ["found"] = Render.Text(), ["remedy"] = Render.Nullable(Render.Text()),
        })),
    };

    /// <summary>dbchange doctor: can this machine do the work (V3_ARCHITECTURE.md §8.12), read-only.</summary>
    public static Envelope Doctor(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words) => words.Count > 0
        ? Contract.Failed(Of("doctor"), new Error("arguments.unknown-flag", "dbchange doctor takes no arguments.", "Run dbchange doctor with no arguments."))
        : Doctor(Io.Doctor.Run(here, Command.Run));

    /// <summary>
    /// io/Doctor.Run's answer as one line: READY and exit 0 when nothing is missing, else DEGRADED and exit 6 with a finding of severity
    /// error and its remedy per item missing, under the doctor's stamp.
    /// </summary>
    public static Envelope Doctor(Stamped<Io.Doctor.Readiness> doctor) => doctor.Result.Match(readiness => Doctor(readiness, doctor.Stamp), error => Contract.Failed(Of("doctor"), error, doctor.Stamp));

    private static Envelope Doctor(Io.Doctor.Readiness readiness, Stamp? stamp)
    {
        var (ready, prerequisites) = (readiness.Ready, readiness.Prerequisites);
        return Contract.Answer(Of("doctor").Output, Of("doctor").Outcome(ready ? "ready" : "degraded"), ready ? 0 : 6,
            string.Join(" | ", (string[])["dbchange doctor " + (ready ? "READY" : "DEGRADED"), .. prerequisites.Select(p => p.Item + "=" + p.Found)]),
            [.. prerequisites.Where(p => p.Remedy is not null).Select(p => Finding.Error("doctor." + p.Item, "dbchange doctor", p.Item + ": " + p.Found + ".", p.Remedy!))],
            stamp, content: new JsonObject
            {
                ["prerequisites"] = Render.Array(prerequisites.Select(p => new JsonObject { ["item"] = p.Item.Name, ["found"] = p.Found, ["remedy"] = p.Remedy })),
            });
    }

    private static Verb Of(string name) => Contract.Verbs.Single(v => v.Name == name);
}
