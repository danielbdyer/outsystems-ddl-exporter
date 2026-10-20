using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// A statement of an environment's deploy plan, or of the pre-deploy script a change carries, as far as Classify reads one: a table the
/// statement rebuilds (DacFx creates the table anew, copies its rows and renames it into place), or a table whose rows it modifies (INSERT,
/// UPDATE, DELETE, MERGE or TRUNCATE TABLE). io reads the statements from the script; which ScriptDom statements make a rebuild is spike
/// S2's measurement. The cases are closed, and sort rebuilds first, then by table and verb.
/// </summary>
public abstract record ScriptStatement : IComparable<ScriptStatement>
{
    private ScriptStatement()
    {
    }

    public T Match<T>(Func<Rebuilds, T> rebuilds, Func<ModifiesRows, T> modifiesRows) => this switch
    {
        Rebuilds r => rebuilds(r),
        ModifiesRows m => modifiesRows(m),
        _ => throw new UnreachableException(),
    };

    public int CompareTo(ScriptStatement? other) => (this, other) switch
    {
        (_, null) => 1,
        (Rebuilds a, Rebuilds b) => a.Table.CompareTo(b.Table),
        (ModifiesRows a, ModifiesRows b) => a.Table.CompareTo(b.Table) is var t and not 0 ? t : string.CompareOrdinal(a.Verb, b.Verb),
        (Rebuilds, _) => -1,
        _ => 1,
    };

    /// <summary>The plan rebuilds the table: a new table, the rows copied, then renamed into the old one's place.</summary>
    public sealed record Rebuilds(Name Table) : ScriptStatement;

    /// <summary>The statement modifies the table's rows: its verb, as T-SQL writes it (UPDATE, MERGE, TRUNCATE TABLE).</summary>
    public sealed record ModifiesRows(Name Table, string Verb) : ScriptStatement;
}

/// <summary>
/// What a change is, provisionally (V3_ARCHITECTURE.md §8.3): each operation a recognize: block names for an entry of the change, with the
/// element it names and how it ships; each pre-deploy script the change creates or edits, a step of its own; each entry no block
/// recognizes, an unclassified change to prove; and the findings, among them the refusals no publish can raise, each an error that
/// carries its remedy. <see cref="Summary"/> opens with the word provisional, which only a proof may drop.
/// </summary>
public sealed record ChangeClassification(SortedArray<ChangeClassification.Recognized> Operations,
    SortedArray<ChangeClassification.Entry> Steps, SortedArray<ChangeClassification.Entry> Unclassified,
    SortedArray<ChangeClassification.Raised> Found)
{
    /// <summary>Whether a finding refuses the change: a rename without its refactorlog entry, or a change to a table CDC tracks.</summary>
    public bool Refused => Found.Any(r => r.Finding.Severity == Severity.Error);

    /// <summary>The findings, the refusals first.</summary>
    public IEnumerable<Finding> Findings => Found.Select(r => r.Finding);

    /// <summary>
    /// The answer in one line, its first word provisional: the operations recognized, then <c>a pre-deploy step</c> when the change edits
    /// the pre-deploy script, then <c>unclassified change: prove it</c> when an entry is unclassified, then <c>refused:</c> and each
    /// refusal's code; <c>provisional: no change</c> for an empty change.
    /// </summary>
    public string Summary => "provisional: " + string.Join("; ", ((string?[])
    [
        Operations.Count == 0 ? null : string.Join(", ", Operations.Select(o => o.Operation).Distinct()),
        Steps.Count == 0 ? null : "a pre-deploy step",
        Unclassified.Count == 0 ? null : "unclassified change: prove it",
        !Refused ? null : "refused: " + string.Join(", ", Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Code).Distinct()),
    ]).OfType<string>().DefaultIfEmpty("no change"));

    /// <summary>
    /// One line per entry and finding: each operation and the element it names, each pre-deploy step, each unclassified entry with its
    /// kind, and each other finding as its severity, code and subject.
    /// </summary>
    public IReadOnlyList<string> Lines =>
    [
        .. Operations.Select(o => o.Operation + " " + o.Subject),
        .. Steps.Select(s => "pre-deploy step " + s.Subject),
        .. Unclassified.Select(u => "unclassified change: prove it, " + u.Kind.Word() + " " + u.Subject),
        .. Found.Where(r => r.Finding.Code != Classify.UnclassifiedCode)
            .Select(r => (r.Finding.Severity == Severity.Error ? "error " : "warning ") + r.Finding.Code + " " + r.Finding.Subject),
    ];

    /// <summary>An operation a recognize: block names for an entry: the operation, the element (its key after the change, before it for a
    /// drop), and how the block says it ships.</summary>
    public sealed record Recognized(string Operation, ElementKey Subject, string Ships) : IComparable<Recognized>
    {
        public int CompareTo(Recognized? other) =>
            other is null ? 1
            : string.CompareOrdinal(Operation, other.Operation) is var o and not 0 ? o
            : Subject.CompareTo(other.Subject) is var s and not 0 ? s
            : string.CompareOrdinal(Ships, other.Ships);
    }

    /// <summary>An entry of the change: its kind (created, dropped, renamed or altered) and the element.</summary>
    public sealed record Entry(ChangeKind Kind, ElementKey Subject) : IComparable<Entry>
    {
        public int CompareTo(Entry? other) =>
            other is null ? 1 : Subject.CompareTo(other.Subject) is var s and not 0 ? s : Kind.CompareTo(other.Kind);
    }

    /// <summary>A finding, sorted errors first, then by subject, code and message.</summary>
    public sealed record Raised(Finding Finding) : IComparable<Raised>
    {
        public int CompareTo(Raised? other) =>
            other is null ? 1
            : Finding.Severity.CompareTo(other.Finding.Severity) is var v and not 0 ? v
            : string.CompareOrdinal(Finding.Subject, other.Finding.Subject) is var s and not 0 ? s
            : string.CompareOrdinal(Finding.Code, other.Finding.Code) is var c and not 0 ? c
            : string.CompareOrdinal(Finding.Message, other.Finding.Message);
    }
}

/// <summary>Classify (WP 2.4): the operation a change is, from the recognize: blocks, and the findings no publish can raise.</summary>
public static class Classify
{
    internal const string UnclassifiedCode = "classify.unclassified";

    /// <summary>The relationships through which an element names the object it stands on: a key's or a default's table, an index's, a
    /// statistic's, a trigger's, a default's column.</summary>
    private static readonly string[] On = ["Host", "DefiningTable", "IndexedObject", "OnObject", "Parent", "TargetColumn"];

    /// <summary>
    /// What Classify reads beside the change: the tables dbchange/ledgers/cdc-tracked.md names for the environments classified; the
    /// statements of an environment's deploy plan; the statements of the after model's pre-deploy script; and the collation names compare
    /// under.
    /// </summary>
    public sealed record Inputs(SortedArray<Name> CdcTracked, SortedArray<ScriptStatement> Plan, SortedArray<ScriptStatement> PreDeploy,
        Collation Collation)
    {
        /// <summary>No table tracked, no plan, no pre-deploy statement, names compared ordinally.</summary>
        public static Inputs None { get; } = new([], [], [], Collation.CaseSensitive);
    }

    /// <summary>
    /// The classification of <paramref name="change"/>, from <paramref name="before"/> to <paramref name="after"/>. Its entries are the
    /// elements created, dropped and renamed and the elements altered, refactorlog entries aside; an element stands in the entry of a
    /// created or dropped element it is keyed under or stands on (a column in its table's, a key in its table's, a default in its
    /// column's), a created history table stands in the entry of the created or altered table whose TemporalSystemVersioningHistoryTable
    /// gains it, and so does an alteration that only adds or removes elements the change creates or drops under the altered element,
    /// the other targets in their order (a table's Columns when a column is created), or that moves the element onto one the change
    /// creates (a key whose table is dropped and created under another name); columns put in another order, or an index whose key gains
    /// a column, is an altered entry. First the renames made by typing are paired and refused: a dropped and a created column of one type
    /// at one ordinal of one table, a dropped and a created table with the same columns in order, and a table of one name dropped from
    /// one schema and created in another. Then each pre-deploy script entry is a step, a data-modifying one when its statements modify
    /// rows; each other entry is the operation of every block that recognizes it, or unclassified. On a table CDC tracks, a column
    /// created, dropped or renamed, and a rebuild in the plan, are refused; a check the change creates or alters that is written with IN
    /// is named.
    /// </summary>
    public static ChangeClassification Of(SortedArray<Element> before, SortedArray<Element> after, Change change,
        SortedArray<Pattern> patterns, Inputs inputs)
    {
        var (was, now) = (Keyed(before), Keyed(after));
        var created = Rooted(change.Created, Versioned(change));
        var dropped = Rooted(change.Dropped, []);
        var entries = created.Where(c => c.Key == c.Value).Select(c => new ChangeClassification.Entry(ChangeKind.Created, c.Key))
            .Concat(dropped.Where(d => d.Key == d.Value).Select(d => new ChangeClassification.Entry(ChangeKind.Dropped, d.Key)))
            .Concat(change.Renamed.Select(r => new ChangeClassification.Entry(ChangeKind.Renamed, r.After)))
            .Concat(change.Altered.Where(a => !InPlaceOf(a, change, created, dropped))
                .Select(a => new ChangeClassification.Entry(ChangeKind.Altered, a.Key)))
            .ToList();

        var raised = new List<Finding>();
        var claimed = Renames(entries, was, now, change, inputs.Collation, raised);
        var steps = entries.Where(e => !claimed.Contains(e) && e.Subject.Type == Element.PreDeploymentScript).ToList();
        if (steps.Any(s => s.Kind != ChangeKind.Dropped))
        {
            raised.AddRange(inputs.PreDeploy.OfType<ScriptStatement.ModifiesRows>().Distinct().Select(m => Finding.Warning(
                "classify.data-modifying-step", steps[0].Subject.ToString(),
                "The pre-deploy script runs " + m.Verb + " on " + m.Table + " before DacFx's plan: a data-modifying step, which a failed"
                    + " deploy does not roll back.",
                "Make the step safe to run twice, name it in the pull request description, and prove the change on a copy.")));
        }

        var recognized = new List<ChangeClassification.Recognized>();
        var unclassified = new List<ChangeClassification.Entry>();
        foreach (var entry in entries.Where(e => !claimed.Contains(e) && !steps.Contains(e)))
        {
            var (element, alteration) = entry.Kind switch
            {
                ChangeKind.Dropped => (was.GetValueOrDefault(entry.Subject), (Change.Alteration?)null),
                ChangeKind.Altered =>
                    (now.GetValueOrDefault(entry.Subject), (Change.Alteration?)change.Altered.First(a => a.Key == entry.Subject)),
                _ => (now.GetValueOrDefault(entry.Subject), (Change.Alteration?)null),
            };
            var by = element is null ? [] : patterns.Where(p => p.Recognizes(entry.Kind, element, alteration)).ToList();
            recognized.AddRange(by.Select(p => new ChangeClassification.Recognized(p.Operation, entry.Subject, p.Ships)));
            if (by.Count == 0)
            {
                unclassified.Add(entry);
                raised.Add(Finding.Warning(UnclassifiedCode, entry.Subject.ToString(),
                    "The change " + entry.Kind.Word() + " " + entry.Subject + ", which no operation's recognize: block recognizes: an"
                        + " unclassified change.",
                    "Prove the change on a copy with dbchange prove."));
            }
        }

        raised.AddRange(Tracked(change, inputs));
        raised.AddRange(InChecks(change, now));
        return new ChangeClassification(SortedArray.Of(recognized), SortedArray.Of(steps), SortedArray.Of(unclassified),
            SortedArray.Of(raised.Select(f => new ChangeClassification.Raised(f))));
    }

    /// <summary>
    /// Pairs each dropped column with a created column of one type at its ordinal of its table, then each dropped table with a created
    /// table of its name in another schema, then with one holding the same columns, and refuses each pair; the entries it pairs.
    /// </summary>
    private static HashSet<ChangeClassification.Entry> Renames(List<ChangeClassification.Entry> entries,
        Dictionary<ElementKey, Element> was, Dictionary<ElementKey, Element> now, Change change, Collation collation, List<Finding> raised)
    {
        var claimed = new HashSet<ChangeClassification.Entry>();
        ChangeClassification.Entry[] Of(ChangeKind kind, string type) => [.. entries.Where(e => e.Kind == kind && e.Subject.Type == type)];
        void Pair(ChangeClassification.Entry[] gone, ChangeClassification.Entry[] come, Func<ElementKey, ElementKey, bool> alike,
            Func<ElementKey, ElementKey, Error> refusal)
        {
            foreach (var d in gone.Where(d => !claimed.Contains(d)))
            {
                if (come.FirstOrDefault(c => !claimed.Contains(c) && alike(d.Subject, c.Subject)) is { } c)
                {
                    claimed.UnionWith([d, c]);
                    raised.Add(Finding.Of(refusal(d.Subject, c.Subject), c.Subject.ToString()));
                }
            }
        }

        Pair(Of(ChangeKind.Dropped, "Column"), Of(ChangeKind.Created, "Column"), (d, c) => d.Parent is { } table && c.Parent is { } host
                && change.KeyBefore(host) == table && Ordinal(was, d) is { } at && at == Ordinal(now, c)
                && TypeOf(was, d) is { } type && type == TypeOf(now, c),
            (d, c) => new Error("rename.column-without-entry",
                d + " is dropped and " + c + " is created, of one type at column "
                    + ((Ordinal(now, c) ?? 0) + 1).ToString(CultureInfo.InvariantCulture) + " of " + c.Parent!.Name
                    + ", and the refactorlog holds no entry renaming one to the other: without it DacFx drops the column and adds an"
                    + " empty one, and every value the column holds is lost.",
                "Run dbchange diff --refactorlog to write the entry that renames " + d.Path + " to " + Own(c) + ", then classify again."));
        Pair(Of(ChangeKind.Dropped, "Table"), Of(ChangeKind.Created, "Table"), (d, c) => d.Name.Schema != c.Name.Schema
                && string.Equals(d.Name.Base, c.Name.Base, collation.Comparison),
            (d, c) => new Error("rename.schema-move-without-entry",
                d + " is dropped and " + c + " is created, one table's name in the schema [" + c.Name.Schema + "], and the refactorlog"
                    + " holds no entry moving it: without it the publish creates the table empty in its new schema, and the rows stay in"
                    + " the old table or are lost with it.",
                "Run dbchange diff --refactorlog to write the entry that moves " + d.Path + " into [" + c.Name.Schema + "], then classify"
                    + " again."));
        Pair(Of(ChangeKind.Dropped, "Table"), Of(ChangeKind.Created, "Table"), (d, c) => Columns(was, d) is { Count: > 0 } columns
                && columns.SequenceEqual(Columns(now, c)),
            (d, c) => new Error("rename.table-without-entry",
                d + " is dropped and " + c + " is created with the same columns, and the refactorlog holds no entry renaming one to the"
                    + " other: without it the publish creates the table empty, and the rows stay in the old table or are lost with it.",
                "Run dbchange diff --refactorlog to write the entry that renames " + d.Path + " to " + Own(c) + ", then classify again."));
        return claimed;
    }

    /// <summary>
    /// On each table CDC tracks, a column the change creates, drops or renames, and a rebuild the plan makes, refused: a capture instance
    /// is bound to the column list it was enabled with and to the table's object_id, which a rebuild replaces.
    /// </summary>
    private static IEnumerable<Finding> Tracked(Change change, Inputs inputs)
    {
        bool Is(ElementKey? table, Name tracked) => table is { Type: "Table" } && table.Name.Matches(tracked, inputs.Collation);
        foreach (var table in inputs.CdcTracked)
        {
            var subject = "Table " + table;
            if (change.Created.Concat(change.Dropped).Where(e => e.Key.Type == "Column").Any(e => Is(e.Key.Parent, table))
                || change.Renamed.Where(r => r.After.Type == "Column").Any(r => Is(r.After.Parent, table) || Is(r.Before.Parent, table)))
            {
                yield return Finding.Of(new Error("cdc.column-list",
                    table + " is tracked by Change Data Capture (dbchange/ledgers/cdc-tracked.md), and the change creates, drops or renames"
                        + " a column of it: a capture instance keeps the column list it was enabled with, so a column created is not"
                        + " captured and one dropped or renamed leaves the instance on the old list.",
                    "Add a pre-deploy step that enables a capture instance on the new column list and disables the old one."), subject);
            }

            if (inputs.Plan.OfType<ScriptStatement.Rebuilds>().Any(r => r.Table.Matches(table, inputs.Collation)))
            {
                yield return Finding.Of(new Error("cdc.table-rebuild",
                    table + " is tracked by Change Data Capture (dbchange/ledgers/cdc-tracked.md), and the environment's plan rebuilds it:"
                        + " DacFx creates a new table, copies the rows and renames it into place, which drops the capture instance bound"
                        + " to the old table's object_id.",
                    "Shape the change so that DacFx plans no rebuild of the table, or add the steps that enable its capture instance"
                        + " again after the deploy."), subject);
            }
        }
    }

    /// <summary>Each check the change creates or alters whose expression lists values with IN, which every plan drops and re-creates
    /// (the operator's ruling of 2026-10-19).</summary>
    private static IEnumerable<Finding> InChecks(Change change, Dictionary<ElementKey, Element> now) => change.Created.Select(e => e.Key)
        .Concat(change.Altered.Select(a => a.Key)).Where(k => k.Type == "CheckConstraint")
        .Where(k => now.GetValueOrDefault(k)?["Expression"] is Value.Script { Content: var text }
            && CheckWrittenWithIn.Lists(text))
        .Select(k => Finding.Warning("classify.check-in-list", k.ToString(),
            k + " is written with IN, which SQL Server stores as a chain of ORs, so every plan drops and re-creates the check.",
            "Write the check in the OR form SQL Server stores, each value compared with = and joined by OR."));

    /// <summary>
    /// Each history table the change creates, with the table it keeps the history of: the created or altered table whose
    /// TemporalSystemVersioningHistoryTable gains it. v2's temporal-convert creates the paired history table as a step of the conversion,
    /// so the history table stands in the versioned table's entry, whether the change declares it or DacFx generates it.
    /// </summary>
    private static Dictionary<ElementKey, ElementKey> Versioned(Change change)
    {
        const string History = "TemporalSystemVersioningHistoryTable";
        var made = change.Created.Select(e => e.Key).ToHashSet();
        var gained = change.Created.SelectMany(e => e.Relationships.Where(r => r.Name == History)
                .SelectMany(r => r.Targets).Select(t => (History: t.Key, Table: e.Key)))
            .Concat(change.Altered.SelectMany(a => a.Relationships.Where(r => r.Name == History)
                .SelectMany(r => r.After.Where(t => !r.Before.Any(b => b.Key == t.Key))).Select(t => (History: t.Key, Table: a.Key))));
        var versioned = new Dictionary<ElementKey, ElementKey>();
        foreach (var (history, table) in gained.Where(g => made.Contains(g.History) && g.History != g.Table))
        {
            versioned.TryAdd(history, table);
        }

        return versioned;
    }

    /// <summary>
    /// Each element of a change's created or dropped list, refactorlog entries aside, with the root of its entry: itself, or the root of
    /// the listed element it is keyed under or stands on, or for a history table, the table it keeps the history of
    /// (<paramref name="versioned"/>), which the change creates or alters.
    /// </summary>
    private static Dictionary<ElementKey, ElementKey> Rooted(SortedArray<Element> listed, Dictionary<ElementKey, ElementKey> versioned)
    {
        var set = listed.Where(e => e.Key.Type != Element.RefactorLogOperation).ToDictionary(e => e.Key);
        var roots = new Dictionary<ElementKey, ElementKey>();
        ElementKey Root(ElementKey key, HashSet<ElementKey> seen)
        {
            if (roots.TryGetValue(key, out var known))
            {
                return known;
            }

            var up = key.Parent is { } parent && set.ContainsKey(parent) ? parent
                : set[key].Relationships.Where(r => On.Contains(r.Name)).SelectMany(r => r.Targets).Select(t => t.Key)
                    .FirstOrDefault(t => t != key && set.ContainsKey(t))
                ?? versioned.GetValueOrDefault(key);
            return roots[key] = up is null || !seen.Add(up) ? key : set.ContainsKey(up) ? Root(up, seen) : up;
        }

        foreach (var key in set.Keys)
        {
            Root(key, [key]);
        }

        return roots;
    }

    /// <summary>
    /// Whether an alteration only adds or removes, in each relationship it changes, elements the change creates or drops that are keyed
    /// under the altered element, and keeps the order of the targets that stay, as a table's Columns does when a column is created: it
    /// then stands in their entry. An element moved from an element the change drops onto one it creates, as a key is when its table is
    /// dropped and created under another name, stands in their entry when every target it gains is created and every one it loses is
    /// dropped. Columns put in another order, or an index whose key gains a created column, is an alteration of its own, which DacFx plans
    /// as a rebuild of the table or of the index.
    /// </summary>
    private static bool InPlaceOf(Change.Alteration alteration, Change change, Dictionary<ElementKey, ElementKey> created,
        Dictionary<ElementKey, ElementKey> dropped)
    {
        var owner = (Before: change.KeyBefore(alteration.Key), After: alteration.Key);
        var moved = alteration.Relationships.Any(r => On.Contains(r.Name)
            && r.Before.Any(t => dropped.ContainsKey(t.Key)) && r.After.Any(t => created.ContainsKey(t.Key)));
        return alteration.Properties.Count == 0 && alteration.Relationships.All(r =>
        {
            var (before, after) = (r.Before.Select(t => t.Key).ToList(), r.After.Select(t => t.Key).ToList());
            var stays = after.Where(k => !created.ContainsKey(k)).Select(change.KeyBefore).ToList();
            var stayed = before.Where(k => !dropped.ContainsKey(k)).ToList();
            return moved ? stays.All(stayed.Contains) && stayed.All(stays.Contains)
                : stays.SequenceEqual(stayed)
                    && after.Where(created.ContainsKey).All(k => k.Parent == owner.After)
                    && before.Where(dropped.ContainsKey).All(k => k.Parent == owner.Before);
        });
    }

    /// <summary>An element's own name, its last part, as a refactorlog entry's NewName writes it: [MobileNumber].</summary>
    private static string Own(ElementKey key) => "[" + key.Name.Base.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>The position of a column in its table's Columns relationship, or null.</summary>
    private static int? Ordinal(Dictionary<ElementKey, Element> model, ElementKey column) =>
        column.Parent is { } table && model.GetValueOrDefault(table) is { } t
            ? t.Relationships.FirstOrDefault(r => r.Name == "Columns")?.Targets.FirstOrDefault(c => c.Key == column)?.Position : null;

    /// <summary>The type a column declares, or null for a column that declares none or that the model does not hold.</summary>
    private static ColumnType? TypeOf(Dictionary<ElementKey, Element> model, ElementKey column) =>
        model.GetValueOrDefault(column) is { } c && !ColumnType.Of(c).Failed(out var type, out _) ? type : null;

    /// <summary>A table's columns in order, each by its name and type.</summary>
    private static List<(string, ColumnType?)> Columns(Dictionary<ElementKey, Element> model, ElementKey table) =>
        [.. (model.GetValueOrDefault(table)?.Relationships.FirstOrDefault(r => r.Name == "Columns")?.Targets ?? [])
            .Select(t => (t.Key.Name.Base, TypeOf(model, t.Key)))];

    private static Dictionary<ElementKey, Element> Keyed(SortedArray<Element> model)
    {
        var keyed = new Dictionary<ElementKey, Element>();
        foreach (var e in model)
        {
            keyed.TryAdd(e.Key, e);
        }

        return keyed;
    }
}
