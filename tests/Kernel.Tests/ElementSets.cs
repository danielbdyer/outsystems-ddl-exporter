using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;

namespace Estate.Kernel.Tests;

/// <summary>
/// Builders and generators for the kernel's view of a model: element sets with tables, their columns and indexes,
/// properties of every value case, relationships whose targets are the set's own keys or strangers, and both deploy
/// scripts. Names come from a four-letter alphabet so sets share keys often; an edit writes "~", which no generated
/// name holds, so an edited key or value is always new.
/// </summary>
internal static class ElementSets
{
    private static readonly Gen<string> Word = Gen.Char["abAB"].Array[1, 3].Select(cs => new string(cs));

    private static readonly Gen<ElementKey> TopKey =
        Gen.Select(Gen.OneOfConst("Table", "View"), Word, Word).Select((type, schema, name) => Key(type, schema, name));

    private static readonly Gen<ElementKey[]> Keys =
        Gen.Select(TopKey, Gen.Select(Gen.OneOfConst("Column", "Index"), Word).Array[0, 3]).Array[0, 5]
            .Select(tops => tops.SelectMany(t => t.Item2.Select(c => Key(t.Item1, c.Item1, c.Item2)).Prepend(t.Item1)).Distinct().ToArray());

    /// <summary>A value of every case; a text and a script draw from one alphabet, so a serialization that tagged them alike would fingerprint two unequal models alike.</summary>
    public static readonly Gen<Value> Values = Gen.OneOf(
        Gen.Const<Value>(new Value.Null()),
        Gen.Bool.Select(b => (Value)new Value.Boolean(b)),
        Gen.Long[-2, 2].Select(n => (Value)new Value.Integer(n)),
        Word.Select(s => (Value)new Value.Text(s)),
        Gen.Select(Gen.OneOfConst("SqlDataType", "SortOrder"), Word).Select((type, member) => (Value)new Value.Enumeration(type, member)),
        Word.Select(s => (Value)new Value.Script(s)));

    private static readonly Gen<(string, Value)[]> Properties =
        Gen.Select(Gen.OneOfConst("Nullable", "Length", "Collation", "IsClustered", "SqlDataType"), Values).Array[0, 4];

    private static readonly Gen<string> ScriptText = Gen.OneOf(Gen.Char["ab\r\n'é"].Array[0, 6].Select(cs => new string(cs)), Gen.String);

    /// <summary>Element sets: up to five tables or views with up to three columns or indexes each, and both deploy scripts.</summary>
    public static readonly Gen<SortedArray<Element>> Sets = Keys.SelectMany(keys =>
        Gen.Select(Properties.Array[keys.Length], Relationships(keys).Array[keys.Length], ScriptText, ScriptText).Select((ps, rs, pre, post) =>
            SortedArray.Of(keys.Select((k, i) => New(k, ps[i].DistinctBy(p => p.Item1), rs[i].DistinctBy(r => r.Item1)))
                .Append(Element.PreDeploy(pre)).Append(Element.PostDeploy(post)))));

    /// <summary>A set and one edit of one of its elements, with the change the edit makes.</summary>
    public static readonly Gen<(SortedArray<Element> Before, Edit Edit)> Edits =
        Gen.Select(Sets, Gen.Int[0, 1000], Gen.Int[0, 7]).Select((set, pick, kind) => (set, Apply(set, set[pick % set.Count], kind, pick)));

    /// <summary>Renames between keys of the same shape as the generated ones; most pair nothing.</summary>
    public static readonly Gen<SortedArray<Rename>> Renames =
        Gen.Select(TopKey, TopKey).Select((a, b) => new Rename(a, b)).Array[0, 4].Select(rs => SortedArray.Of(rs));

    /// <summary>
    /// Two models and the refactorlog between them. The second is the first with elements dropped (fate 0), others
    /// given generated properties (fate 1) and another set's elements added; then up to five renames, made one at a
    /// time and each recorded under the keys of its moment, so a column's entry may name its table's old key, its new
    /// key or a key the table held between the two.
    /// </summary>
    public static readonly Gen<Renaming> Renamings = Sets.SelectMany(a =>
        Gen.Select(Gen.Int[0, 2].Array[a.Count], Properties.Array[a.Count], Sets, Gen.Int[0, 1000].Array[0, 5]).Select((fates, properties, other, picks) =>
        {
            var kept = a.Select((e, i) => fates[i] switch
            {
                0 => null,
                1 => Ok(Element.Of(e.Key, properties[i].DistinctBy(p => p.Item1).Select(p => new Element.Property(p.Item1, p.Item2)), e.Relationships)),
                _ => e,
            }).OfType<Element>().ToArray();
            var after = SortedArray.Of(kept.Concat(other.Where(o => a.All(e => e.Key != o.Key))));
            var entries = new List<Rename>();
            foreach (var pick in after.Count == 0 ? [] : picks)
            {
                var e = after[pick % after.Count];
                (after, var made) = Renamed(after, (e.Key, e.Key.Name.Base + "~"));
                entries.AddRange(made);
            }

            return new Renaming(a, after, entries, [.. a.Where((_, i) => fates[i] == 0)], [.. kept.Select(e => e.Key)]);
        }));

    /// <summary>A generated renaming: both models, the entries in the order they were made, what was dropped and what was kept.</summary>
    public sealed record Renaming(SortedArray<Element> Before, SortedArray<Element> After, IReadOnlyList<Rename> Entries, Element[] Dropped, ElementKey[] Kept)
    {
        public SortedArray<Rename> Renames => SortedArray.Of(Entries);

        /// <summary>The renames Change must report: each kept element whose own name the entries changed.</summary>
        public SortedArray<Rename> Expected => SortedArray.Of(Kept.Select(k => new Rename(k, Final(k, Entries))).Where(r => r.After.Name != r.Before.Name));
    }

    /// <summary>One edit: its kind, the set after it, the change it makes, and the renames that explain it.</summary>
    public sealed record Edit(string Kind, SortedArray<Element> After, Change Expected, SortedArray<Rename> Renames);

    public static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new InvalidOperationException(error.Code + ": " + error.Message));

    public static ElementKey Key(string type, string schema, string name) => Ok(ElementKey.Of(type, Ok(Name.Of(schema, name))));

    public static ElementKey Key(ElementKey parent, string type, string name) => Ok(ElementKey.Of(parent, type, Ok(Name.Of(name))));

    public static Element New(
        ElementKey key, IEnumerable<(string Name, Value Value)> properties, IEnumerable<(string Name, ElementKey[] Targets)>? relationships = null) =>
        Ok(Element.Of(
            key,
            properties.Select(p => new Element.Property(p.Name, p.Value)),
            (relationships ?? []).Select(r => Element.Relationship.Of(r.Name, r.Targets))));

    public static Value Bool(bool value) => new Value.Boolean(value);

    public static Value Int(long value) => new Value.Integer(value);

    public static Value Text(string value) => new Value.Text(value);

    /// <summary>The same element built again, its properties and relationships given in reverse.</summary>
    public static Element Rebuilt(Element e) => Ok(Element.Of(e.Key, e.Properties.Reverse(), e.Relationships.Reverse()));

    /// <summary>A change with one altered element.</summary>
    public static Change Altered(ElementKey key, SortedArray<Change.Property> properties = default, SortedArray<Change.Relationship> relationships = default) =>
        new([], [], [], [new Change.Alteration(key, properties, relationships)]);

    /// <summary>
    /// What <see cref="Change.Between"/> in the other direction returns: every side swapped, and each altered element
    /// keyed back, through the rename that moved it or an ancestor, to the key it had before.
    /// </summary>
    public static Change Mirror(Change c) => new(
        c.Dropped,
        c.Created,
        SortedArray.Of(c.Renamed.Select(r => r.Inverted())),
        SortedArray.Of(c.Altered.Select(a => new Change.Alteration(
            Back(a.Key, c.Renamed),
            SortedArray.Of(a.Properties.Select(p => new Change.Property(p.Name, p.After, p.Before))),
            SortedArray.Of(a.Relationships.Select(r => new Change.Relationship(r.Name, r.After, r.Before)))))));

    /// <summary>The key an element had before the renames that moved it or one of its ancestors.</summary>
    public static ElementKey Back(ElementKey key, SortedArray<Rename> renames) =>
        renames.FirstOrDefault(r => r.After == key) is { } rename ? rename.Before
        : key.Parent is { } parent && Back(parent, renames) is var was && was != parent ? Ok(ElementKey.Of(was, key.Type, key.Name))
        : key;

    /// <summary>
    /// Renames made one at a time, as SSDT makes and records them: each step names an element by its key in
    /// <paramref name="set"/> and its new name, and its entry is recorded under the keys the element and its parent
    /// hold at that moment. Returns the set after, and the entries in the order they were made.
    /// </summary>
    public static (SortedArray<Element> After, IReadOnlyList<Rename> Entries) Renamed(SortedArray<Element> set, params (ElementKey Element, string Name)[] steps)
    {
        var entries = new List<Rename>();
        foreach (var (element, name) in steps)
        {
            var rename = Ok(Rename.Of(Final(element, entries), name));
            entries.Add(rename);
            set = Moved(set, rename.Before, rename.After);
        }

        return (set, entries);
    }

    /// <summary>The key an element holds after entries made in this order.</summary>
    public static ElementKey Final(ElementKey key, IEnumerable<Rename> entries) => entries.Aggregate(key, (k, r) => Rebase(k, r.Before, r.After));

    /// <summary>The key with <paramref name="from"/> replaced by <paramref name="to"/> wherever it is the key or an ancestor.</summary>
    public static ElementKey Rebase(ElementKey key, ElementKey from, ElementKey to) =>
        key == from ? to
        : key.Parent is { } parent && Rebase(parent, from, to) is var moved && moved != parent ? Ok(ElementKey.Of(moved, key.Type, key.Name))
        : key;

    /// <summary>A rename as SSDT makes one: the element, everything keyed under it and every reference to them move.</summary>
    public static SortedArray<Element> Moved(SortedArray<Element> set, ElementKey from, ElementKey to) => SortedArray.Of(set.Select(e => Ok(Element.Of(
        Rebase(e.Key, from, to),
        e.Properties,
        e.Relationships.Select(r => new Element.Relationship(r.Name, SortedArray.Of(r.Targets.Select(t => t with { Key = Rebase(t.Key, from, to) }))))))));

    private static Gen<(string, ElementKey[])[]> Relationships(ElementKey[] keys) =>
        Gen.Select(Gen.OneOfConst("Columns", "Schema", "DataType"), (keys.Length == 0 ? TopKey : Gen.OneOf(Gen.OneOfConst(keys), TopKey)).Array[0, 3]).Array[0, 2];

    private static SortedArray<Element> Replaced(SortedArray<Element> set, Element before, params Element[] after) =>
        SortedArray.Of(set.Where(e => e != before).Concat(after));

    private static Edit Apply(SortedArray<Element> set, Element e, int kind, int pick)
    {
        var stranger = Key("Table", "~", "~");
        switch (kind)
        {
            case 0:
                var name = e.Properties.Count > 0 ? e.Properties[0].Name : "Added~";
                var was = e[name];
                var now = Text(was is Value.Text t ? t.Content + "~" : "~");
                var withValue = Ok(Element.Of(e.Key, e.Properties.Where(p => p.Name != name).Append(new Element.Property(name, now)), e.Relationships));
                return new Edit("a property value", Replaced(set, e, withValue), Altered(e.Key, [new Change.Property(name, was, now)]), []);
            case 1:
                var relationship = e.Relationships.Count > 0 ? e.Relationships[0] : new Element.Relationship("Refers~", []);
                SortedArray<Element.Relationship.Target> targets = relationship.Targets.Count > 0
                    ? SortedArray.Of(relationship.Targets.Select(x => x.Position == 0 ? x with { Key = stranger } : x))
                    : [new Element.Relationship.Target(0, stranger)];
                var withTarget = Ok(Element.Of(e.Key, e.Properties, e.Relationships.Where(r => r.Name != relationship.Name).Append(relationship with { Targets = targets })));
                return new Edit("a relationship target", Replaced(set, e, withTarget), Altered(e.Key, relationships: [new Change.Relationship(relationship.Name, relationship.Targets, targets)]), []);
            case 2:
                var script = set.First(x => x.Key.Type == (pick % 2 == 0 ? Element.PreDeploymentScript : Element.PostDeploymentScript));
                var text = ((Value.Script)script["Text"]!).Content;
                var edited = pick % 2 == 0 ? Element.PreDeploy(text.Insert(pick % (text.Length + 1), "~")) : Element.PostDeploy(text.Insert(pick % (text.Length + 1), "~"));
                return new Edit("a script's text", Replaced(set, script, edited), Altered(script.Key, [new Change.Property("Text", script["Text"], edited["Text"])]), []);
            case 3:
                var rekeyed = Ok(Element.Of(Ok(Rename.Of(e.Key, e.Key.Name.Base + "~")).After, e.Properties, e.Relationships));
                return new Edit("a key", Replaced(set, e, rekeyed), new Change([rekeyed], [e], [], []), []);
            case 4:
                // The refactorlog's history may name the dropped key, renamed to a key both models hold: that changes nothing.
                SortedArray<Rename> history = set.FirstOrDefault(x => x != e) is { } other ? [new Rename(e.Key, other.Key)] : [];
                return new Edit("an element removed", Replaced(set, e), new Change([], [e], [], []), history);
            case 5:
                var added = New(stranger, [("Nullable", Bool(true))]);
                return new Edit("an element added", SortedArray.Of(set.Append(added)), new Change([added], [], [], []), []);
            case 7 when set.FirstOrDefault(x => set.Any(y => y.Key.Parent == x.Key)) is { } table:
                // A table and one of its columns renamed, the column's entry recorded under the table's old key, its
                // new key, or a key the table held between the two, as SSDT records each entry under the keys of its moment.
                var columns = set.Where(x => x.Key.Parent == table.Key).ToArray();
                var (owner, column) = (table.Key, columns[pick % columns.Length].Key);
                (ElementKey, string)[] steps = (pick % 3) switch
                {
                    0 => [(column, column.Name.Base + "~"), (owner, owner.Name.Base + "~")],
                    1 => [(owner, owner.Name.Base + "~"), (column, column.Name.Base + "~")],
                    _ => [(owner, owner.Name.Base + "~~"), (column, column.Name.Base + "~"), (owner, owner.Name.Base + "~")],
                };
                var (both, made) = Renamed(set, steps);
                var expected = new Change([], [], [new Rename(owner, Final(owner, made)), new Rename(column, Final(column, made))], []);
                var under = (pick % 3) switch { 0 => "old", 1 => "new", _ => "between" };
                return new Edit("a table and a column renamed, the column's entry under the table's " + under + " key", both, expected, SortedArray.Of(made));
            default:
                // Two times in three the renamed element has children, which must move with it; half the time the
                // refactorlog renamed it twice, through a name neither read holds.
                e = pick % 3 == 0 ? e : set.FirstOrDefault(x => set.Any(c => c.Key.Parent == x.Key)) ?? e;
                var rename = Ok(Rename.Of(e.Key, e.Key.Name.Base + "~"));
                var through = Ok(Rename.Of(e.Key, e.Key.Name.Base + "~~")).After;
                SortedArray<Rename> entries = pick % 2 == 0 ? [rename] : [new Rename(e.Key, through), new Rename(through, rename.After)];
                return new Edit("a rename with its refactorlog entries", Moved(set, rename.Before, rename.After), new Change([], [], [rename], []), entries);
        }
    }
}
