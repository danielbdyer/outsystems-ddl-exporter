using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

/// <summary>The verbs' bodies, one file per verb.</summary>
public static partial class Verbs
{
    /// <summary>What doctor adds to the envelope: each check, what it found, and its remedy when the item is missing.</summary>
    public static JsonObject DoctorContent => new()
    {
        ["checks"] = Render.List(Render.Record(new() { ["item"] = Render.Text(), ["found"] = Render.Text(), ["remedy"] = Render.Nullable(Render.Text()) })),
    };

    /// <summary>estate doctor: can this machine do the work (V3_ARCHITECTURE.md §8.12), read-only.</summary>
    public static Envelope Doctor(Checkout here, IReadOnlyList<string> words) => words.Count > 0
        ? Contract.Failed(Of("doctor"), new Error("arguments.unknown-flag", "estate doctor takes no arguments.", "estate --help names each verb's flags"))
        : Doctor(Io.Doctor.Examine(System.AppContext.BaseDirectory, here.Tool, here.WorkingDirectory, Io.Doctor.Run, Contract.Version),
            Io.Doctor.Toolchain(here.Root, Contract.Version));

    /// <summary>
    /// One line, READY when nothing is missing and exit 0, else DEGRADED and exit 6 with a blocking finding and its remedy per item
    /// missing; the engine stamped as the committed DacFx, with the image's digest where Docker holds it, and the ledger's pin.
    /// </summary>
    public static Envelope Doctor(IReadOnlyList<Io.Doctor.Check> checks, Result<Pin> pin)
    {
        var ready = checks.All(c => c.Remedy is null);
        var image = checks.Any(c => c.Item == "image" && c.Found == "present") ? Io.Doctor.ImageDigest : null;
        var stamp = Stamped(image, pin.Match<Pin?>(p => p, _ => null));
        return Contract.Answer(Of("doctor").Output, ready ? "ready" : "degraded",
            string.Join(" | ", (string[])["estate doctor " + (ready ? "READY" : "DEGRADED"), .. checks.Select(c => c.Item + "=" + c.Found)]),
            [.. checks.Where(c => c.Remedy is not null).Select(c => new Finding("doctor." + c.Item, "block", "estate doctor", c.Item + ": " + c.Found + ".", c.Remedy))],
            ready ? 0 : 6, stamp, content: new JsonObject
            {
                ["checks"] = Render.Array(checks.Select(c => new JsonObject { ["item"] = c.Item, ["found"] = c.Found, ["remedy"] = c.Remedy })),
            });
    }

    private static Verb Of(string name) => Contract.Verbs.Single(v => v.Name == name);
}
