using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

public static partial class Verbs
{
    /// <summary>What diff adds to the envelope: each side and its fingerprint, and the change, per element and per property.</summary>
    public static JsonObject DiffContent => new()
    {
        ["diff"] = Render.Record(new()
        {
            ["from"] = Side(), ["to"] = Side(),
            ["change"] = Render.Record(new()
            {
                ["created"] = Render.List(Render.Text()), ["dropped"] = Render.List(Render.Text()),
                ["renamed"] = Render.List(Render.Record(new() { ["before"] = Render.Text(), ["after"] = Render.Text() })),
                ["altered"] = Render.List(Render.Record(new()
                {
                    ["key"] = Render.Text(),
                    ["properties"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Values(), ["after"] = Values() })),
                    ["relationships"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Render.List(Render.Text()), ["after"] = Render.List(Render.Text()) })),
                })),
            }),
        }),
    };

    /// <summary>
    /// estate diff --from &lt;target&gt; --to &lt;target&gt; [--project &lt;path&gt;] [--fail-on-change]: Change.Between the two models with the renames
    /// their refactorlogs record (V3_ARCHITECTURE.md §8.5); matches at exit 0, or differs with one line per change, at exit 5 with --fail-on-change.
    /// </summary>
    public static Envelope Diff(Checkout here, IReadOnlyList<string> words)
    {
        if (Contract.Flags(words, ["--from", "--to"], ["--project"], ["--fail-on-change"]).Bind(flags => SqlServer.Target.Parse(flags["--from"], "--from")
            .Bind(from => SqlServer.Target.Parse(flags["--to"], "--to").Bind(to => Pinned(here).Bind(pin => Reading(here, from, flags.GetValueOrDefault("--project"))
            .Bind(before => Reading(here, to, flags.GetValueOrDefault("--project")).Bind(after =>
                Change.Between(before.Model.Elements, after.Model.Elements, SortedArray.Of(before.Model.Renames.Concat(after.Model.Renames).Distinct()))
                    .Map(change => (Before: before, After: after, Change: change, Fail: flags.ContainsKey("--fail-on-change"), Pin: pin))))))))
            .Failed(out var diff, out var error))
        {
            return Contract.Failed(Of("diff"), error, Stamped(null, null));
        }

        return Diff(diff.Before, diff.After, diff.Change, diff.Fail, Stamped(diff.Before.Image ?? diff.After.Image, diff.Pin));
    }

    /// <summary>
    /// The answer of a diff whose sides are read: matches (exit 0) when the change is empty, else differs, at exit 5 only with
    /// --fail-on-change; the message counts the changes, and the change's lines are the Markdown body.
    /// </summary>
    internal static Envelope Diff(Source before, Source after, Change change, bool failOnChange, Stamp stamp)
    {
        var (lines, printer) = (Lines(change).ToList(), new Printer());
        var message = lines.Count == 0 ? "No change from " + before.Target + " to " + after.Target + "."
            : lines.Count.ToString(CultureInfo.InvariantCulture) + (lines.Count == 1 ? " change from " : " changes from ") + before.Target + " to " + after.Target + ".";
        var content = new JsonObject { ["diff"] = new JsonObject { ["from"] = Side(before), ["to"] = Side(after), ["change"] = Json(change, printer) } };
        return Contract.Answer(Of("diff").Output, Of("diff").Outcome(change.IsEmpty ? "matches" : "differs"), failOnChange && !change.IsEmpty ? 5 : 0, message,
            [
                .. before.IsDatabase == after.IsDatabase ? [] : new[] { Finding.Note("diff.unlike-sources", "estate diff", before.Target + " and " + after.Target
                    + " are read one from a package and one from a database, and SQL Server keeps a check's or a default's text as it normalized it, so such text can differ where the schemas agree.") },
                .. printer.Findings,
            ],
            stamp, content: content, lines: lines);
    }

    /// <summary>A change as lines: each element created, dropped or renamed, then each property (with its values, a text's or a script's left out) or relationship that is altered.</summary>
    internal static IEnumerable<string> Lines(Change change) =>
        change.Created.Select(e => "created " + e.Key)
            .Concat(change.Dropped.Select(e => "dropped " + e.Key))
            .Concat(change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Altered.SelectMany(a => a.Properties
                .Select(p => a.Key + ": " + p.Name + (p.Before is Value.Text or Value.Script || p.After is Value.Text or Value.Script ? "" : " " + (p.Before?.ToString() ?? "none") + " → " + (p.After?.ToString() ?? "none")))
                .Concat(a.Relationships.Select(r => a.Key + ": " + r.Name))));

    /// <summary>A change as JSON: the keys created, dropped and renamed, and each alteration with its values before and after, a script's through the printer.</summary>
    private static JsonObject Json(Change change, Printer printer) => new()
    {
        ["created"] = Render.Array(change.Created.Select(e => (JsonNode?)e.Key.ToString())),
        ["dropped"] = Render.Array(change.Dropped.Select(e => (JsonNode?)e.Key.ToString())),
        ["renamed"] = Render.Array(change.Renamed.Select(r => new JsonObject { ["before"] = r.Before.ToString(), ["after"] = r.After.ToString() })),
        ["altered"] = Render.Array(change.Altered.Select(a => new JsonObject
        {
            ["key"] = a.Key.ToString(),
            ["properties"] = Render.Array(a.Properties.Select(p => new JsonObject { ["name"] = p.Name, ["before"] = printer.Json(a.Key, p.Name, p.Before), ["after"] = printer.Json(a.Key, p.Name, p.After) })),
            ["relationships"] = Render.Array(a.Relationships.Select(r => new JsonObject
            {
                ["name"] = r.Name, ["before"] = Render.Array(r.Before.Select(t => (JsonNode?)t.Key.ToString())), ["after"] = Render.Array(r.After.Select(t => (JsonNode?)t.Key.ToString())),
            })),
        })),
    };

    private static JsonObject Side() => Render.Record(new() { ["from"] = Render.Text(), ["fingerprint"] = Render.Fingerprint() });

    private static JsonObject Side(Source source) => new() { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(Fingerprint.Of(source.Model.Elements)) };
}
