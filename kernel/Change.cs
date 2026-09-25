using System;
using System.Collections.Generic;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// A rename the refactorlog records: the key an element had, and the key it has now.
/// </summary>
public sealed record Rename(ElementKey Before, ElementKey After) : IComparable<Rename>
{
    /// <summary>The rename a refactorlog entry's NewName makes: the same parent, type and schema, and the new name.</summary>
    public static Result<Rename> Of(ElementKey before, string newName) =>
        (before.Name.Schema is { } schema ? Name.Of(schema, newName) : Name.Of(newName))
            .Bind(name => before.Parent is { } parent ? ElementKey.Of(parent, before.Type, name) : ElementKey.Of(before.Type, name))
            .Map(after => new Rename(before, after));

    /// <summary>The rename back. A method, not a property: a record prints and serializes every public property, and
    /// a property returning the rename back would be walked back and forth without end.</summary>
    public Rename Inverted() => new(After, Before);

    public int CompareTo(Rename? other) =>
        other is null ? 1 : Before.CompareTo(other.Before) is var c and not 0 ? c : After.CompareTo(other.After);
}

/// <summary>
/// What changes between two reads, for every element type the walk reads, the deploy scripts and the refactorlog
/// entries included: the elements created and dropped, the renames, and each element in both reads that is altered,
/// property by property and relationship by relationship. An element continues under its own key, or under the key a
/// rename gives it or one of its ancestors; so a renamed table's columns and indexes move with it, a column renamed in
/// a renamed table is one rename, and a reference to a renamed element is unchanged when it names the new key.
/// </summary>
public sealed record Change(SortedArray<Element> Created, SortedArray<Element> Dropped, SortedArray<Rename> Renamed, SortedArray<Change.Alteration> Altered)
{
    public bool IsEmpty => Created.Count + Dropped.Count + Renamed.Count + Altered.Count == 0;

    /// <summary>
    /// The change from <paramref name="before"/> to <paramref name="after"/>. A rename applies when an element's key,
    /// under its parent's key after, is gone after, and a rename of it leads, directly or through later renames, to a
    /// key that is new after and continues no other element; the rest of the refactorlog's history changes nothing.
    /// SSDT records each entry under the keys of its moment, so an entry may name the element under any key its parent
    /// held, old, new or between, and its new key is read under the parent's key after. A read in which two
    /// elements share a key is the error change.duplicate-key.
    /// </summary>
    public static Result<Change> Between(SortedArray<Element> before, SortedArray<Element> after, SortedArray<Rename> renames)
    {
        if ((Duplicate(before) ?? Duplicate(after)) is { } error)
        {
            return error;
        }

        var (was, now) = (before.ToDictionary(e => e.Key), after.ToDictionary(e => e.Key));
        var next = renames.ToLookup(r => r.Before, r => r.After);
        var places = new Dictionary<ElementKey, Place>();           // every key reached: where it is after, and the keys it held
        var continues = new Dictionary<ElementKey, ElementKey>();   // a key after, and the key before it continues
        var renamed = new List<Rename>();

        Place Locate(ElementKey key)
        {
            if (places.TryGetValue(key, out var known))
            {
                return known;
            }

            // The parent's key after, and the keys it held; a top-level key's parent is none, held as null.
            ElementKey? home = null;
            ElementKey?[] homes = [null];
            if (key.Parent is { } parent)
            {
                (home, homes) = Locate(parent);
            }

            var held = new List<ElementKey> { key };

            // The keys an entry may name a key by: under each key its parent held, or as recorded when its parent is another element.
            IEnumerable<ElementKey> Recorded(ElementKey name) => homes.Contains(name.Parent) ? homes.Select(name.Under) : [name];

            // The key a chain of renames from a key the element held ends at: one new after, continuing no element yet.
            ElementKey? Follow(ElementKey from, HashSet<ElementKey> seen)
            {
                foreach (var to in Recorded(from).SelectMany(name => next[name]).Where(seen.Add))
                {
                    var at = homes.Contains(to.Parent) ? to.Under(home) : to;
                    if ((now.ContainsKey(at) ? (was.ContainsKey(at) || continues.ContainsKey(at) ? null : at) : Follow(to, seen)) is { } end)
                    {
                        held.Add(to);
                        return end;
                    }
                }

                return null;
            }

            var moved = key.Under(home);
            if (was.ContainsKey(key) && !(now.ContainsKey(moved) && continues.TryAdd(moved, key)) && Follow(key, []) is { } to)
            {
                continues.Add(to, key);
                renamed.Add(new Rename(key, to));
                moved = to;
            }

            return places[key] = new Place(moved, [.. held.SelectMany(Recorded).Distinct()]);
        }

        ElementKey Image(ElementKey key) => Locate(key).Image;

        var pairs = before.Where(e => continues.GetValueOrDefault(Image(e.Key)) == e.Key).Select(e => (Was: e, Now: now[Image(e.Key)])).ToList();
        return new Change(
            SortedArray.Of(after.Where(e => !continues.ContainsKey(e.Key))),
            SortedArray.Of(before.Where(e => continues.GetValueOrDefault(Image(e.Key)) != e.Key)),
            SortedArray.Of(renamed),
            SortedArray.Of(pairs.Select(p => AlterationOf(p.Was, p.Now, Image)).OfType<Alteration>()));
    }

    // Where a key is after, and every key the refactorlog may name it by: each key it held, under each key its parent held.
    private sealed record Place(ElementKey Image, ElementKey?[] Held);

    // How an element is altered from the one it continues as, its old targets read through the renames; null when alike.
    private static Alteration? AlterationOf(Element was, Element now, Func<ElementKey, ElementKey> image)
    {
        var properties = SortedArray.Of(was.Properties.Select(p => p.Name).Union(now.Properties.Select(p => p.Name))
            .Select(name => new Property(name, was[name], now[name]))
            .Where(p => p.Before != p.After));
        var relationships = SortedArray.Of(was.Relationships.Select(r => r.Name).Union(now.Relationships.Select(r => r.Name))
            .Select(name => new Relationship(name, Targets(was, name), Targets(now, name)))
            .Where(r => SortedArray.Of(r.Before.Select(t => t with { Key = image(t.Key) })) != r.After));
        return properties.Count + relationships.Count == 0 ? null : new Alteration(now.Key, properties, relationships);
    }

    private static SortedArray<Element.Relationship.Target> Targets(Element element, string name) =>
        element.Relationships.FirstOrDefault(r => r.Name == name)?.Targets ?? default;

    // A SortedArray sorts elements by key first, so two elements with one key sit side by side.
    private static Error? Duplicate(SortedArray<Element> read) => Enumerable.Range(1, Math.Max(0, read.Count - 1))
        .Where(i => read[i].Key == read[i - 1].Key)
        .Select(i => new Error(
            "change.duplicate-key",
            $"Two elements of one read have the key {read[i].Key}.",
            "Report the read's source: a key names one element, so the walk that produced this read has a defect."))
        .FirstOrDefault();

    /// <summary>An element in both reads that is altered: its key after, and each property and relationship that differs.</summary>
    public sealed record Alteration(ElementKey Key, SortedArray<Property> Properties, SortedArray<Relationship> Relationships) : IComparable<Alteration>
    {
        public int CompareTo(Alteration? other) =>
            other is null ? 1
            : Key.CompareTo(other.Key) is var k and not 0 ? k
            : SortedArray.Compare(Properties, other.Properties) is var p and not 0 ? p
            : SortedArray.Compare(Relationships, other.Relationships);
    }

    /// <summary>A property that differs: its value before and after, null where the element does not carry it.</summary>
    public sealed record Property(string Name, Value? Before, Value? After) : IComparable<Property>
    {
        public int CompareTo(Property? other) =>
            other is null ? 1
            : string.CompareOrdinal(Name, other.Name) is var n and not 0 ? n
            : Compare(Before, other.Before) is var b and not 0 ? b
            : Compare(After, other.After);

        private static int Compare(Value? mine, Value? theirs) => mine is null ? (theirs is null ? 0 : -1) : mine.CompareTo(theirs);
    }

    /// <summary>A relationship that differs: its targets before and after, empty where the element has none.</summary>
    public sealed record Relationship(string Name, SortedArray<Element.Relationship.Target> Before, SortedArray<Element.Relationship.Target> After)
        : IComparable<Relationship>
    {
        public int CompareTo(Relationship? other) =>
            other is null ? 1
            : string.CompareOrdinal(Name, other.Name) is var n and not 0 ? n
            : SortedArray.Compare(Before, other.Before) is var b and not 0 ? b
            : SortedArray.Compare(After, other.After);
    }
}
