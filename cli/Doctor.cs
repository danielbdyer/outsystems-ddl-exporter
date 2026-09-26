using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

/// <summary>The verbs' bodies, one file per verb.</summary>
public static partial class Verbs
{
    /// <summary>What doctor adds to the envelope: each check, what it found, and its remedy when the item is missing.</summary>
    public static JsonObject DoctorContent => new()
    {
        ["checks"] = Render.List(Render.Record(new() { ["item"] = Render.Text(), ["found"] = Render.Text(), ["remedy"] = Render.Nullable(Render.Text()) })),
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
        var (ready, checks) = (readiness.Ready, readiness.Prerequisites);
        return Contract.Answer(Of("doctor").Output, Of("doctor").Outcome(ready ? "ready" : "degraded"), ready ? 0 : 6,
            string.Join(" | ", (string[])["dbchange doctor " + (ready ? "READY" : "DEGRADED"), .. checks.Select(c => c.Item + "=" + c.Found)]),
            [.. checks.Where(c => c.Remedy is not null).Select(c => Finding.Error("doctor." + c.Item, "dbchange doctor", c.Item + ": " + c.Found + ".", c.Remedy!))],
            stamp, content: new JsonObject
            {
                ["checks"] = Render.Array(checks.Select(c => new JsonObject { ["item"] = c.Item.Name, ["found"] = c.Found, ["remedy"] = c.Remedy })),
            });
    }

    private static Verb Of(string name) => Contract.Verbs.Single(v => v.Name == name);
}
