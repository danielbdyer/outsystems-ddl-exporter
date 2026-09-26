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
    public static Envelope Doctor(Checkout here, IReadOnlyList<string> words) => words.Count > 0
        ? Contract.Failed(Of("doctor"), new Error("arguments.unknown-flag", "dbchange doctor takes no arguments.", "Run dbchange doctor with no arguments."))
        : Doctor(Io.Doctor.Examine(Io.Doctor.Machine.Here(here.Tool, here.WorkingDirectory), Command.Run, Contract.Version), Io.Doctor.Toolchain(here.Root, Contract.Version));

    /// <summary>
    /// One line, READY when nothing is missing and exit 0, else DEGRADED and exit 6 with a finding of severity error and its remedy per item
    /// missing; the stamp names the DacFx release dbchange runs, when it can be named (the dacfx item says why when it cannot), and the ledger's
    /// pin. The doctor reaches no copy, so it stamps no SQL Server.
    /// </summary>
    public static Envelope Doctor(IReadOnlyList<Io.Doctor.Prerequisite> checks, Result<Pin> pin)
    {
        var ready = checks.All(c => c.Remedy is null);
        var stamp = DacFx.Version.Match<Stamp?>(dacfx => new Stamp(dacfx, pin.Match<Pin?>(p => p, _ => null)), _ => null);
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
