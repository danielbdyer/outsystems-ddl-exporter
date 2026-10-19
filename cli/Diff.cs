using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Io;
using DbChange.Kernel;

namespace DbChange.Cli;

public static partial class Verbs
{
    /// <summary>What diff adds to the envelope: each side and its fingerprint, the counts of the whole change, and the change, per element and per property, each of its four lists one that can be long.</summary>
    public static JsonObject DiffContent => new()
    {
        ["diff"] = Render.Record(new()
        {
            ["from"] = Side(), ["to"] = Side(),
            ["counts"] = Render.Record(new() { ["created"] = Count(), ["dropped"] = Count(), ["renamed"] = Count(), ["altered"] = Count(), ["caseOnlyRenamed"] = Count() }),
            ["change"] = ChangeSchema(),
        }),
    };

    /// <summary>A change as diff and check drift write one: the keys created and dropped, the renames and case-only pairs, and each alteration, each list one that can be long.</summary>
    internal static JsonObject ChangeSchema() => Render.Record(new()
    {
        ["created"] = Render.Long(Render.Text()), ["dropped"] = Render.Long(Render.Text()),
        ["renamed"] = Render.Long(Render.Record(new() { ["before"] = Render.Text(), ["after"] = Render.Text() })),
        ["caseOnlyRenamed"] = Render.Long(Render.Record(new() { ["before"] = Render.Text(), ["after"] = Render.Text() })),
        ["altered"] = Render.Long(Render.Record(new()
        {
            ["key"] = Render.Text(),
            ["properties"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Values(), ["after"] = Values() })),
            ["relationships"] = Render.List(Render.Record(new() { ["name"] = Render.Text(), ["before"] = Render.List(Render.Text()), ["after"] = Render.List(Render.Text()) })),
        })),
    });

    /// <summary>
    /// dbchange diff --from &lt;target&gt; --to &lt;target&gt; [--project &lt;path&gt;] [--fail-on-change]: Change.Between the two models with the renames
    /// their refactorlogs record (V3_ARCHITECTURE.md §8.5), names compared under the from side's collation, the target a deploy of the to
    /// side would plan against; matches at exit 0, or differs with one line per change, at exit 5 with --fail-on-change.
    /// </summary>
    public static Envelope Diff(Checkout here, SqlServer.QueryLog log, IReadOnlyList<string> words)
    {
        if (Contract.Flags(words, ["--from", "--to"], ["--project"], ["--fail-on-change"]).Bind(flags => SqlServer.Target(flags["--from"], "--from")
            .Bind(from => SqlServer.Target(flags["--to"], "--to").Map(to => (Flags: flags, From: from, To: to))))
            .Failed(out var asked, out var error))
        {
            return Contract.Failed(Of("diff"), error, Standing.Committed);
        }

        var diff = ModelDiff.Run(here, asked.From, asked.To, asked.Flags.GetValueOrDefault("--project"), log);
        return diff.Result.Failed(out var answer, out error) ? Contract.Failed(Of("diff"), error, diff.Stamp) : Diff(answer, asked.Flags.ContainsKey("--fail-on-change"), diff.Stamp!);
    }

    /// <summary>
    /// The answer of a diff whose sides are read: matches (exit 0) when the change is empty, else differs, at exit 5 only with
    /// --fail-on-change; the message counts the changes, the change's lines are the Markdown body, and each case-only pair is a note.
    /// </summary>
    internal static Envelope Diff(ModelDiff.Answer diff, bool failOnChange, Stamp stamp)
    {
        var (before, after, change, collation) = (diff.From, diff.To, diff.Change, diff.Collation);
        var (lines, printer) = (Lines(change).ToList(), new Printer());
        var message = lines.Count == 0 ? "No change from " + before.Target + " to " + after.Target + "."
            : lines.Count.ToString(CultureInfo.InvariantCulture) + (lines.Count == 1 ? " change from " : " changes from ") + before.Target + " to " + after.Target + ".";
        var counts = new JsonObject
        {
            ["created"] = change.Created.Count, ["dropped"] = change.Dropped.Count, ["renamed"] = change.Renamed.Count, ["altered"] = change.Altered.Count, ["caseOnlyRenamed"] = change.CaseOnlyRenamed.Count,
        };
        var content = new JsonObject { ["diff"] = new JsonObject { ["from"] = Side(before), ["to"] = Side(after), ["counts"] = counts, ["change"] = Json(change, printer) } };
        return Contract.Answer(Of("diff").Output, Of("diff").Outcome(change.IsEmpty ? "in-sync" : "differs"), failOnChange && !change.IsEmpty ? 5 : 0, message,
            [
                .. before.IsDatabase == after.IsDatabase ? [] : new[] { Finding.Note("diff.unlike-sources", "dbchange diff", before.Target + " and " + after.Target
                    + " are read one from a package and one from a database: SQL Server keeps a check's, a default's and a computed column's Expression as it normalized the text,"
                    + " and an index's DataCompressionOption reads otherwise from each, so those four properties can differ where the schemas agree.") },
                .. before.Notes,
                .. after.Notes,
                .. change.CaseOnlyRenamed.Select(pair => CaseOnly("diff.case-only-rename", pair, collation)),
                .. printer.Findings,
            ],
            stamp, content: content, lines: lines);
    }

    /// <summary>A change as lines: each element created, dropped or renamed, then each property (with its values, a text's or a script's left out) or relationship that is altered.</summary>
    internal static IEnumerable<string> Lines(Change change) =>
        change.Created.Select(e => "created " + e.Key)
            .Concat(change.Dropped.Select(e => "dropped " + e.Key))
            .Concat(change.Renamed.Select(r => "renamed " + r.Before + " to " + r.After))
            .Concat(change.Altered.SelectMany(a => a.Properties.Select(p => a.Key + ": " + Changed(p)).Concat(a.Relationships.Select(r => a.Key + ": " + r.Name))));

    /// <summary>A property that changed: its name and its values before and after, none for an absent one; a text's or a script's values left out.</summary>
    internal static string Changed(Change.Property property) => property.Name
        + (property.Before is Value.Text or Value.Script || property.After is Value.Text or Value.Script ? "" : " " + (property.Before?.ToString() ?? "none") + " → " + (property.After?.ToString() ?? "none"));

    /// <summary>A change as JSON: the keys created, dropped and renamed, and each alteration with its values before and after, a script's through the printer.</summary>
    internal static JsonObject Json(Change change, Printer printer) => new()
    {
        ["created"] = Render.Array(change.Created.Select(e => (JsonNode?)e.Key.ToString())),
        ["dropped"] = Render.Array(change.Dropped.Select(e => (JsonNode?)e.Key.ToString())),
        ["renamed"] = Render.Array(change.Renamed.Select(r => new JsonObject { ["before"] = r.Before.ToString(), ["after"] = r.After.ToString() })),
        ["caseOnlyRenamed"] = Render.Array(change.CaseOnlyRenamed.Select(r => new JsonObject { ["before"] = r.Before.ToString(), ["after"] = r.After.ToString() })),
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

    private static JsonObject Side(ModelRead.Source source) => new() { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(Fingerprint.Of(source.Model.Elements)) };
}
