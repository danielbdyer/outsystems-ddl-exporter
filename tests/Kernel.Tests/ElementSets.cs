using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;

namespace Estate.Kernel.Tests;

/// <summary>
/// Builders and generators for the kernel's view of a read: element sets with tables, their columns and indexes,
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

    public static readonly Gen<Value> Values = Gen.OneOf(
        Gen.Const<Value>(new Value.Null()),
        Gen.Bool.Select(b => (Value)new Value.Boolean(b)),
        Gen.Long[-2, 2].Select(n => (Value)new Value.Integer(n)),
        Word.Select(s => (Value)new Value.Text(s)),
        Gen.Select(Gen.OneOfConst("SqlDataType", "SortOrder"), Word).Select((type, member) => (Value)new Value.Enumeration(type, member)));

    private static readonly Gen<(string, Value)[]> Properties =
        Gen.Select(Gen.OneOfConst("Nullable", "Length", "Collation", "IsClustered", "SqlDataType"), Values).Array[0, 4];

    private static readonly Gen<string> ScriptText = Gen.OneOf(Gen.Char["ab\r\n'é"].Array[0, 6].Select(cs => new string(cs)), Gen.String);

    /// <summary>Element sets: up to five tables or views with up to three columns or indexes each, and both deploy scripts.</summary>
    public static readonly Gen<Seq<Element>> Sets = Keys.SelectMany(keys =>
        Gen.Select(Properties.Array[keys.Length], Relationships(keys).Array[keys.Length], ScriptText, ScriptText).Select((ps, rs, pre, post) =>
            Seq.Of(keys.Select((k, i) => New(k, ps[i].DistinctBy(p => p.Item1), rs[i].DistinctBy(r => r.Item1)))
                .Append(Element.PreDeploy(pre)).Append(Element.PostDeploy(post)))));

    /// <summary>A set and one edit of one of its elements, with the change the edit makes.</summary>
    public static readonly Gen<(Seq<Element> Before, Edit Edit)> Edits =
        Gen.Select(Sets, Gen.Int[0, 1000], Gen.Int[0, 6]).Select((set, pick, kind) => (set, Apply(set, set[pick % set.Count], kind, pick)));

    /// <summary>Renames between keys of the same shape as the generated ones; most pair nothing.</summary>
    public static readonly Gen<Seq<Rename>> Renames =
        Gen.Select(TopKey, TopKey).Select((a, b) => new Rename(a, b)).Array[0, 4].Select(rs => Seq.Of(rs));

    /// <summary>One edit: its kind, the set after it, the change it makes, and the renames that explain it.</summary>
    public sealed record Edit(string Kind, Seq<Element> After, Change Expected, Seq<Rename> Renames);

    public static T Ok<T>(Result<T> result) =>
        result.Match(value => value, refusal => throw new InvalidOperationException(refusal.Code + ": " + refusal.Message));

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
    public static Change Altered(ElementKey key, Seq<Change.Property> properties = default, Seq<Change.Relationship> relationships = default) =>
        new([], [], [], [new Change.Altered(key, properties, relationships)]);

    /// <summary>What <see cref="Change.Between"/> in the other direction returns: every side swapped.</summary>
    public static Change Mirror(Change c) => new(
        c.Removed,
        c.Added,
        Seq.Of(c.Renamed.Select(r => r.Inverse)),
        Seq.Of(c.Changed.Select(a => new Change.Altered(
            a.Key,
            Seq.Of(a.Properties.Select(p => new Change.Property(p.Name, p.After, p.Before))),
            Seq.Of(a.Relationships.Select(r => new Change.Relationship(r.Name, r.After, r.Before)))))));

    /// <summary>The key with <paramref name="from"/> replaced by <paramref name="to"/> wherever it is the key or an ancestor.</summary>
    public static ElementKey Rebase(ElementKey key, ElementKey from, ElementKey to) =>
        key == from ? to
        : key.Parent is { } parent && Rebase(parent, from, to) is var moved && moved != parent ? Ok(ElementKey.Of(moved, key.Type, key.Name))
        : key;

    /// <summary>A rename as SSDT makes one: the element, everything keyed under it and every reference to them move.</summary>
    public static Seq<Element> Moved(Seq<Element> set, ElementKey from, ElementKey to) => Seq.Of(set.Select(e => Ok(Element.Of(
        Rebase(e.Key, from, to),
        e.Properties,
        e.Relationships.Select(r => new Element.Relationship(r.Name, Seq.Of(r.Targets.Select(t => t with { Key = Rebase(t.Key, from, to) }))))))));

    private static Gen<(string, ElementKey[])[]> Relationships(ElementKey[] keys) =>
        Gen.Select(Gen.OneOfConst("Columns", "Schema", "DataType"), (keys.Length == 0 ? TopKey : Gen.OneOf(Gen.OneOfConst(keys), TopKey)).Array[0, 3]).Array[0, 2];

    private static Seq<Element> Replaced(Seq<Element> set, Element before, params Element[] after) =>
        Seq.Of(set.Where(e => e != before).Concat(after));

    private static Edit Apply(Seq<Element> set, Element e, int kind, int pick)
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
                Seq<Element.Relationship.Target> targets = relationship.Targets.Count > 0
                    ? Seq.Of(relationship.Targets.Select(x => x.Position == 0 ? x with { Key = stranger } : x))
                    : [new Element.Relationship.Target(0, stranger)];
                var withTarget = Ok(Element.Of(e.Key, e.Properties, e.Relationships.Where(r => r.Name != relationship.Name).Append(relationship with { Targets = targets })));
                return new Edit("a relationship target", Replaced(set, e, withTarget), Altered(e.Key, relationships: [new Change.Relationship(relationship.Name, relationship.Targets, targets)]), []);
            case 2:
                var script = set.First(x => x.Key.Type == (pick % 2 == 0 ? Element.PreDeploymentScript : Element.PostDeploymentScript));
                var text = ((Value.Text)script["Text"]!).Content;
                var edited = pick % 2 == 0 ? Element.PreDeploy(text.Insert(pick % (text.Length + 1), "~")) : Element.PostDeploy(text.Insert(pick % (text.Length + 1), "~"));
                return new Edit("a script's text", Replaced(set, script, edited), Altered(script.Key, [new Change.Property("Text", script["Text"], edited["Text"])]), []);
            case 3:
                var rekeyed = Ok(Element.Of(Ok(Rename.Of(e.Key, e.Key.Name.Base + "~")).After, e.Properties, e.Relationships));
                return new Edit("a key", Replaced(set, e, rekeyed), new Change([rekeyed], [e], [], []), []);
            case 4:
                // The refactorlog's history may name the removed key, renamed to a key both reads hold: that changes nothing.
                Seq<Rename> history = set.FirstOrDefault(x => x != e) is { } other ? [new Rename(e.Key, other.Key)] : [];
                return new Edit("an element removed", Replaced(set, e), new Change([], [e], [], []), history);
            case 5:
                var added = New(stranger, [("Nullable", Bool(true))]);
                return new Edit("an element added", Seq.Of(set.Append(added)), new Change([added], [], [], []), []);
            default:
                // Two times in three the renamed element has children, which must move with it; half the time the
                // refactorlog renamed it twice, through a name neither read holds.
                e = pick % 3 == 0 ? e : set.FirstOrDefault(x => set.Any(c => c.Key.Parent == x.Key)) ?? e;
                var rename = Ok(Rename.Of(e.Key, e.Key.Name.Base + "~"));
                var through = Ok(Rename.Of(e.Key, e.Key.Name.Base + "~~")).After;
                Seq<Rename> entries = pick % 2 == 0 ? [rename] : [new Rename(e.Key, through), new Rename(through, rename.After)];
                return new Edit("a rename with its refactorlog entries", Moved(set, rename.Before, rename.After), new Change([], [], [rename], []), entries);
        }
    }
}
