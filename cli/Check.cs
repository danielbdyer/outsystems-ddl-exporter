using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

public static partial class Verbs
{
    /// <summary>The checks the verb table names beyond drift, and the milestone each arrives in.</summary>
    private static readonly Dictionary<string, int> Later = new(StringComparer.Ordinal) { ["cdc"] = 2, ["evidence"] = 3, ["inflight"] = 5, ["outsystems"] = 6, ["environments"] = 6 };

    /// <summary>What check adds to the envelope: the kind of check, the target, the ref and its commit, and each operation the plan holds.</summary>
    public static JsonObject CheckContent => new()
    {
        ["check"] = Render.Record(new()
        {
            ["kind"] = Render.Enum(["drift"]), ["target"] = Render.Text(), ["at"] = Render.Text(), ["commit"] = Render.Pattern("^[0-9a-f]{40,64}$"),
            ["operations"] = Render.List(Render.Record(new() { ["operation"] = Render.Text(), ["type"] = Render.Text(), ["name"] = Render.Text() })),
        }),
    };

    /// <summary>estate check drift --target &lt;target&gt; --at &lt;ref&gt; [--profile &lt;path&gt;] [--project &lt;path&gt;]; the other checks arrive later.</summary>
    public static Envelope Check(Checkout here, IReadOnlyList<string> words) => words switch
    {
        ["drift", ..] => Drift(here, [.. words.Skip(1)]),
        [var kind, ..] when Later.TryGetValue(kind, out var arrives) => Contract.NotBuilt(Of("check") with { Name = "check " + kind, Arrives = arrives }) with { Schema = Of("check").Output },
        _ => Contract.Failed(Of("check"), new Error("arguments.unknown-check", "estate check needs the check to run; this build runs check drift.", "estate check drift --target <target> --at <ref>")),
    };

    /// <summary>
    /// Whether a database matches the repository at a ref (§1 fact 4; M1 exits 1 and 3): the ref built, then planned against the target under
    /// the pipeline's profile as the caller, or as the environment's reference names. An empty deploy report is exit 0, "&lt;target&gt; matches
    /// &lt;ref&gt;"; else exit 5, a finding per object the report names. The receipt stamps the committed engine, which must stand inside the
    /// toolchain ledger's window (R13); a denial is reported before anything builds, in one sentence naming the environment (R16).
    /// </summary>
    private static Envelope Drift(Checkout here, IReadOnlyList<string> words)
    {
        var (verb, stamp) = (Of("check"), Stamped(null, null));
        if (Contract.Flags(words, ["--target", "--at"], ["--profile", "--project"], []).Bind(flags => SqlServer.Target.Parse(flags["--target"], "--target").Map(target => (Flags: flags, Target: target)))
            .Bind(asked => Io.Doctor.Toolchain(here.Root, Contract.Version).Map(pin => (asked.Flags, asked.Target, Pin: pin))).Failed(out var asked, out var error))
        {
            return Contract.Failed(verb, error, stamp);
        }

        stamp = Stamped(null, asked.Pin);
        if ((asked.Pin.Rejects(stamp.Engine) is { } outside ? Result.Fail<SqlServer.Database>(outside) : SqlServer.Resolve(asked.Target, here.Root))
            .Bind(database => Profile(here, database, asked.Flags.GetValueOrDefault("--profile")).Map(profile => (asked.Flags, asked.Target, Database: database, Profile: profile)))
            .Failed(out var drift, out error))
        {
            return Contract.Failed(verb, error, stamp);
        }

        stamp = Stamped(Substrate.Image(drift.Database), stamp.Pin);
        var log = SqlServer.QueryLog.Start(here.Root);
        var at = drift.Flags["--at"];
        if (SqlServer.Reach(drift.Database, log).Bind(_ => Built(here, at, drift.Flags.GetValueOrDefault("--project"))).Bind(built => Packaged(built.Dacpac)
                .Bind(model => SqlServer.Plan(built.Dacpac, drift.Database, drift.Profile, log).Map(plan => (built.Commit, Model: model, Plan: plan))))
            .Failed(out var planned, out error))
        {
            return Contract.Failed(verb, error, stamp);
        }

        var receipt = new Receipt(Fingerprint.Of(planned.Model.Elements), Fingerprint.Of(planned.Plan.Report), null, stamp.Engine, drift.Profile.Fingerprint, drift.Target.ToString(),
            DateTimeOffset.UtcNow);
        var items = planned.Plan.Items;
        return Contract.Answer(verb.Output, items.Count == 0 ? "converged" : "differs",
            drift.Target + (items.Count == 0 ? " matches " + at : " differs from " + at + " in each object below."),
            [
                .. items.Select(i => new Finding("drift." + i.Operation.ToLowerInvariant(), "warn", Named(i.Type) + " " + i.Name,
                    "The plan against " + drift.Target + " would " + i.Operation + " " + Named(i.Type) + " " + i.Name + ".", "estate diff --from " + drift.Target + " --to ref:" + at)),
                .. items.Count == 0 ? [] : Columns(drift.Database, planned.Model, items, log).Select(line => line.Split(": ", 2) is [var key, var change]
                    ? new Finding("drift.column", "warn", key, change + ", from the target to the repository.", null) : new Finding("drift.column", "warn", line, line + ".", null)),
                .. stamp.Pin is Pin.Unpinned ? new[] { new Finding("engine.unpinned", "note", "estate check drift", "This receipt stands on DacFx " + stamp.Engine.DacFx
                    + ", UNPINNED: " + Io.Doctor.Ledger + " pins no engine for estate " + Contract.Version.Split('+')[0] + ".", null) } : [],
                Unverified,
            ],
            items.Count == 0 ? 0 : 5, stamp, receipt, new JsonObject
            {
                ["check"] = new JsonObject
                {
                    ["kind"] = "drift", ["target"] = drift.Target.ToString(), ["at"] = at, ["commit"] = planned.Commit,
                    ["operations"] = Render.Array(items.Select(i => new JsonObject { ["operation"] = i.Operation, ["type"] = Named(i.Type), ["name"] = i.Name })),
                },
            });
    }

    /// <summary>
    /// §17 item 15's default, on every receipt: until S7 commits the profile the Octopus step applies, the profile a receipt stands on is
    /// the proving ground's Pipeline profile or the estate's own, and neither is verified against that step.
    /// </summary>
    private static Finding Unverified => new("profile.unverified", "note", "estate check drift",
        "This receipt stands on a profile not verified against the Octopus step: S7 has not committed the profile that step applies.", null);

    /// <summary>The pipeline's profile: a named environment's own; for a copy, the one --profile names, else the one profile every environment of the posture names.</summary>
    private static Result<PublishProfile.Strict> Profile(Checkout here, SqlServer.Database database, string? named) =>
        database is SqlServer.Named environment ? Profiles.Of(environment.Environment, here.Root)
        : named is not null ? Profiles.Load(Path.GetFullPath(Path.Combine(here.Root, named)))
        : Profiles.Environments(here.Root).Bind(environments => environments.Select(e => e.ProfilePath).Distinct().ToList() is [var shared]
            ? Profiles.Load(Path.GetFullPath(Path.Combine(here.Root, shared)))
            : new Error("arguments.missing-flag", database + " is a copy, and " + Profiles.Posture + " names no one profile its environments share.",
                "estate check drift --profile <the pipeline's .publish.xml> names the profile to plan under"));

    /// <summary>
    /// The columns that differ under each table the report names, which DacFx's report names only as the table: the target's model read
    /// and compared with the package's, the column's own properties alone, so text SQL Server keeps as it normalized it plays no part.
    /// </summary>
    private static IEnumerable<string> Columns(SqlServer.Database database, Ssdt.ModelElements package, IReadOnlyList<(string Operation, string Type, string Name)> items, SqlServer.QueryLog log)
    {
        var tables = items.Where(i => i.Type == "SqlTable").Select(i => "Table " + i.Name).ToHashSet(StringComparer.Ordinal);
        bool Under(ElementKey key) => key.Type == "Column" && tables.Contains(key.Parent?.ToString() ?? "");
        return SqlServer.Model(database, log).Bind(model => Change.Between(model, package.Elements, [])).Match(
            change => Lines(new Change(SortedArray.Of(change.Created.Where(e => Under(e.Key))), SortedArray.Of(change.Dropped.Where(e => Under(e.Key))), [], SortedArray.Of(change.Altered.Where(a => Under(a.Key))))),
            _ => []);
    }

    /// <summary>A type as the deploy report serializes it (SqlTable), as io/Ssdt.Elements names it (Table).</summary>
    private static string Named(string type) => type.StartsWith("Sql", StringComparison.Ordinal) ? type[3..] : type;
}
