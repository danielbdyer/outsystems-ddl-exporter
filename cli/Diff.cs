using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

public static partial class Verbs
{
    /// <summary>What diff adds to the envelope: each side read and its fingerprint, and the change, per element and per property.</summary>
    public static JsonObject DiffContent => new()
    {
        ["diff"] = Render.Record(new()
        {
            ["from"] = Side(), ["to"] = Side(),
            ["change"] = Render.Record(new()
            {
                ["added"] = Render.List(Render.Text()), ["removed"] = Render.List(Render.Text()),
                ["renamed"] = Render.List(Render.Record(new() { ["before"] = Render.Text(), ["after"] = Render.Text() })),
                ["changed"] = Render.List(Render.Record(new()
                {
                    ["key"] = Render.Text(),
                    ["properties"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Values(), ["after"] = Values() })),
                    ["relationships"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Render.List(Render.Text()), ["after"] = Render.List(Render.Text()) })),
                })),
            }),
        }),
    };

    /// <summary>
    /// estate diff --from &lt;target&gt; --to &lt;target&gt; [--project &lt;path&gt;] [--fail-on-change]: Change.Between the two reads with the renames
    /// their refactorlogs record (V3_ARCHITECTURE.md §8.5), one line per change; exit 5 with --fail-on-change when anything changes.
    /// </summary>
    public static Envelope Diff(Checkout here, IReadOnlyList<string> words)
    {
        if (Contract.Flags(words, ["--from", "--to"], ["--project"], ["--fail-on-change"]).Bind(flags => SqlServer.Target.Parse(flags["--from"], "--from")
            .Bind(from => SqlServer.Target.Parse(flags["--to"], "--to").Bind(to => Pinned(here).Bind(pin => Reading(here, from, flags.GetValueOrDefault("--project"))
            .Bind(before => Reading(here, to, flags.GetValueOrDefault("--project")).Bind(after =>
                Change.Between(before.Read.Elements, after.Read.Elements, Seq.Of(before.Read.Renames.Concat(after.Read.Renames).Distinct()))
                    .Map(change => (Before: before, After: after, Change: change, Fail: flags.ContainsKey("--fail-on-change"), Pin: pin))))))))
            .Failed(out var diff, out var error))
        {
            return Contract.Failed(Of("diff"), error, Stamped(null, null));
        }

        var (lines, fails) = (Lines(diff.Change).ToList(), diff.Fail && !diff.Change.IsEmpty);
        return Contract.Answer(Of("diff").Output, fails ? "differs" : "done", lines.Count == 0 ? "No change from " + diff.Before.Target + " to " + diff.After.Target + "." : string.Join('\n', lines),
            diff.Before.IsDatabase == diff.After.IsDatabase ? [] : [new("diff.unlike-sources", "note", "estate diff", diff.Before.Target + " and " + diff.After.Target
                + " are read one from a package and one from a database, and SQL Server keeps a check's or a default's text as it normalized it, so such text can differ where the schemas agree.", null)],
            fails ? 5 : 0, Stamped(diff.Before.Image ?? diff.After.Image, diff.Pin), content: new JsonObject
            {
                ["diff"] = new JsonObject { ["from"] = Side(diff.Before), ["to"] = Side(diff.After), ["change"] = Json(diff.Change) },
            });
    }

    /// <summary>A change as lines: each element added, removed or renamed, then each property (with its values, a text's left out) or relationship that differs.</summary>
    internal static IEnumerable<string> Lines(Change change) =>
        change.Added.Select(e => "added " + e.Key)
            .Concat(change.Removed.Select(e => "removed " + e.Key))
            .Concat(change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Changed.SelectMany(a => a.Properties
                .Select(p => a.Key + ": " + p.Name + (p.Before is Value.Text || p.After is Value.Text ? "" : " " + (p.Before?.ToString() ?? "none") + " → " + (p.After?.ToString() ?? "none")))
                .Concat(a.Relationships.Select(r => a.Key + ": " + r.Name))));

    private static JsonObject Json(Change change) => new()
    {
        ["added"] = Render.Array(change.Added.Select(e => (JsonNode?)e.Key.ToString())),
        ["removed"] = Render.Array(change.Removed.Select(e => (JsonNode?)e.Key.ToString())),
        ["renamed"] = Render.Array(change.Renamed.Select(r => new JsonObject { ["before"] = r.Before.ToString(), ["after"] = r.After.ToString() })),
        ["changed"] = Render.Array(change.Changed.Select(a => new JsonObject
        {
            ["key"] = a.Key.ToString(),
            ["properties"] = Render.Array(a.Properties.Select(p => new JsonObject { ["name"] = p.Name, ["before"] = Json(p.Before), ["after"] = Json(p.After) })),
            ["relationships"] = Render.Array(a.Relationships.Select(r => new JsonObject
            {
                ["name"] = r.Name, ["before"] = Render.Array(r.Before.Select(t => (JsonNode?)t.Key.ToString())), ["after"] = Render.Array(r.After.Select(t => (JsonNode?)t.Key.ToString())),
            })),
        })),
    };

    private static JsonObject Side() => Render.Record(new() { ["from"] = Render.Text(), ["fingerprint"] = Render.Fingerprint() });

    private static JsonObject Side(Source source) => new() { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(Fingerprint.Of(source.Read.Elements)) };
}
