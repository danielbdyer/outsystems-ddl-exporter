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

    /// <summary>The rename back, as a method: a record prints and serializes every public property, and a property
    /// returning the rename back would recurse until the stack overflows.</summary>
    public Rename Inverted() => new(After, Before);

    public int CompareTo(Rename? other) =>
        other is null ? 1 : Before.CompareTo(other.Before) is var c and not 0 ? c : After.CompareTo(other.After);
}

/// <summary>
/// What changes between two models, for every element type io/Ssdt.ReadModel reads, the deploy scripts and the refactorlog
/// entries included: the elements created and dropped, the renames, and each element in both models that is altered,
/// property by property and relationship by relationship. An element continues under its own key, or under the key a
/// rename gives it or one of its ancestors; so a renamed table's columns and indexes move with it, a column renamed in
/// a renamed table is one rename, and a reference to a renamed element is unchanged when it names the new key. Under a
/// case-insensitive collation an element also continues under a key that differs from its own in letter case alone, which
/// the collation reads as one name and DacFx plans nothing for: such a pair is a <see cref="CaseOnlyRenamed"/> entry and no
/// change (decision 2.26), so <see cref="IsEmpty"/> leaves it out. Output keys are the after model's spellings.
/// </summary>
public sealed record Change(SortedArray<Element> Created, SortedArray<Element> Dropped, SortedArray<Rename> Renamed, SortedArray<Change.Alteration> Altered,
    SortedArray<Rename> CaseOnlyRenamed = default)
{
    public bool IsEmpty => Created.Count + Dropped.Count + Renamed.Count + Altered.Count == 0;

    /// <summary>The change from <paramref name="before"/> to <paramref name="after"/> with names compared ordinally, as the kernel compares them: the comparison that follows no database.</summary>
    public static Result<Change> Between(SortedArray<Element> before, SortedArray<Element> after, SortedArray<Rename> renames) =>
        Between(before, after, renames, Collation.CaseSensitive);

    /// <summary>
    /// The change from <paramref name="before"/> to <paramref name="after"/>, names compared under <paramref name="collation"/> as
    /// DacFx's deploy plan compares them, in three phases, each a rule of SSDT's. Images: each key of before is mapped to the key it holds
    /// after, resolved parent first, because SSDT records each refactorlog entry under the keys of its moment and a child's new key is read
    /// under its parent's image; a rename applies when an element's key, under its parent's key after, is gone after, and a rename of it
    /// leads, directly or through later renames, to a key that is new after. Continuations: an after key claimed by more than one before
    /// key keeps the element already at it under the collation with no rename, else the first claimant in before's order, and the rest
    /// continue nowhere, since a rename into a key a live element holds is a drop and a create. Partition: what is created, dropped,
    /// renamed, altered, and matched in letter case alone. A model in which two elements share a key under the collation is the error
    /// change.duplicate-key.
    /// </summary>
    public static Result<Change> Between(SortedArray<Element> before, SortedArray<Element> after, SortedArray<Rename> renames, Collation collation)
    {
        var comparer = ElementKey.Comparer(collation);
        if ((Duplicate(before, comparer, collation) ?? Duplicate(after, comparer, collation)) is { } error)
        {
            return error;
        }

        var images = new Images(before, after, renames, comparer);
        var continues = Continuations(before, images);
        return Partition(before, after, images, continues, comparer);
    }

    /// <summary>
    /// Phase 1: where each key is after. A before key holds its own key under its parent's image when the after model has it (a live
    /// continuation), else the key a chain of refactorlog renames from any key it held ends at, when that key is new after (a renamed
    /// continuation), else nowhere. A key outside before, a relationship's target, moves under its parent's image and nothing else.
    /// </summary>
    private sealed class Images
    {
        private readonly Dictionary<ElementKey, Element> was;
        private readonly Dictionary<ElementKey, Element> now;
        private readonly ILookup<ElementKey, ElementKey> next;
        private readonly Dictionary<ElementKey, Place> places = [];

        public Images(SortedArray<Element> before, SortedArray<Element> after, SortedArray<Rename> renames, IEqualityComparer<ElementKey> comparer)
        {
            was = before.ToDictionary(e => e.Key, comparer);
            now = after.ToDictionary(e => e.Key, comparer);
            next = renames.ToLookup(r => r.Before, r => r.After, comparer);
        }

        /// <summary>The key <paramref name="key"/> holds after, spelled as the after model spells it where the after model holds it.</summary>
        public ElementKey Image(ElementKey key) => Locate(key).Image;

        public Claim Claimed(ElementKey key) => Locate(key).Claim;

        /// <summary>The element after at the key <paramref name="key"/> holds after, if the after model holds one.</summary>
        public Element? At(ElementKey key) => now.GetValueOrDefault(key);

        private Place Locate(ElementKey key)
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
                (home, homes, _) = Locate(parent);
            }

            var held = new List<ElementKey> { key };

            // The keys an entry may name a key by: under each key its parent held, or as recorded when its parent is another element.
            IEnumerable<ElementKey> Recorded(ElementKey name) => homes.Contains(name.Parent) ? homes.Select(name.Under) : [name];

            // The key a chain of renames from a key the element held ends at: one new after, spelled as the after model spells it.
            ElementKey? Follow(ElementKey from, HashSet<ElementKey> seen)
            {
                foreach (var to in Recorded(from).SelectMany(name => next[name]).Where(seen.Add))
                {
                    var at = homes.Contains(to.Parent) ? to.Under(home) : to;
                    if ((now.TryGetValue(at, out var there) ? (was.ContainsKey(at) ? null : there.Key) : Follow(to, seen)) is { } end)
                    {
                        held.Add(to);
                        return end;
                    }
                }

                return null;
            }

            var moved = key.Under(home);
            var place = !was.ContainsKey(key) ? new Place(moved, [], Claim.None)
                : now.TryGetValue(moved, out var live) ? new Place(live.Key, [], Claim.Live)
                : Follow(key, []) is { } to ? new Place(to, [], Claim.Renamed)
                : new Place(moved, [], Claim.None);
            return places[key] = place with { Held = [.. held.SelectMany(Recorded).Distinct()] };
        }

        /// <summary>Where a key is after, every key the refactorlog may name it by (each key it held, under each key its parent held), and how it claims its image.</summary>
        private sealed record Place(ElementKey Image, ElementKey?[] Held, Claim Claim);
    }

    /// <summary>How a before key claims the after key it maps to: at its own key under the collation with no rename, through the refactorlog's renames, or not at all.</summary>
    private enum Claim
    {
        None,
        Live,
        Renamed,
    }

    /// <summary>
    /// Phase 2: which before key each after key continues. An after key claimed by more than one before key keeps the element already
    /// at it (a live claim), else the first claimant in before's sorted order; the rest continue nowhere, since a rename into a key a
    /// live element holds is a drop and a create.
    /// </summary>
    private static Dictionary<ElementKey, Element> Continuations(SortedArray<Element> before, Images images) => before
        .Where(e => images.Claimed(e.Key) != Claim.None)
        .GroupBy(e => images.Image(e.Key))
        .ToDictionary(g => g.Key, g => g.FirstOrDefault(e => images.Claimed(e.Key) == Claim.Live) ?? g.First());

    /// <summary>
    /// Phase 3: the elements created (after keys no before key continues into), dropped (before keys that continue nowhere), renamed
    /// (continued through the refactorlog), matched in letter case alone (continued live under a key that differs from their own
    /// ordinally at its last level), and altered (continued elements that differ property by property or relationship by
    /// relationship, their old targets read through the images).
    /// </summary>
    private static Change Partition(SortedArray<Element> before, SortedArray<Element> after, Images images, Dictionary<ElementKey, Element> continues, IEqualityComparer<ElementKey> comparer)
    {
        var continued = continues.Values.Select(e => e.Key).ToHashSet();
        var pairs = continues.Select(c => (Was: c.Value, Now: images.At(c.Key)!)).ToList();
        return new Change(
            SortedArray.Of(after.Where(e => !continues.ContainsKey(e.Key))),
            SortedArray.Of(before.Where(e => !continued.Contains(e.Key))),
            SortedArray.Of(pairs.Where(p => images.Claimed(p.Was.Key) == Claim.Renamed).Select(p => new Rename(p.Was.Key, p.Now.Key))),
            SortedArray.Of(pairs.Select(p => AlterationOf(p.Was, p.Now, images.Image, comparer)).OfType<Alteration>()),
            SortedArray.Of(pairs.Where(p => images.Claimed(p.Was.Key) == Claim.Live && p.Was.Key.Name != p.Now.Key.Name).Select(p => new Rename(p.Was.Key, p.Now.Key))));
    }

    // How an element is altered from the one it continues as, its old targets read through the renames and compared under the collation; null when alike.
    private static Alteration? AlterationOf(Element was, Element now, Func<ElementKey, ElementKey> image, IEqualityComparer<ElementKey> comparer)
    {
        var properties = SortedArray.Of(was.Properties.Select(p => p.Name).Union(now.Properties.Select(p => p.Name))
            .Select(name => new Property(name, was[name], now[name]))
            .Where(p => p.Before != p.After));
        var relationships = SortedArray.Of(was.Relationships.Select(r => r.Name).Union(now.Relationships.Select(r => r.Name))
            .Select(name => new Relationship(name, Targets(was, name), Targets(now, name)))
            .Where(r => !SameTargets(r.Before.Select(t => t with { Key = image(t.Key) }).ToList(), r.After, comparer)));
        return properties.Count + relationships.Count == 0 ? null : new Alteration(now.Key, properties, relationships);
    }

    /// <summary>Whether two target lists name the same objects in the same order under the collation.</summary>
    private static bool SameTargets(IReadOnlyList<Element.Relationship.Target> imaged, SortedArray<Element.Relationship.Target> after, IEqualityComparer<ElementKey> comparer) =>
        imaged.Count == after.Count && imaged.Zip(after).All(pair => pair.First.Position == pair.Second.Position && comparer.Equals(pair.First.Key, pair.Second.Key));

    private static SortedArray<Element.Relationship.Target> Targets(Element element, string name) =>
        element.Relationships.FirstOrDefault(r => r.Name == name)?.Targets ?? default;

    // Two elements of one model with one key under the collation: a SortedArray sorts elements by key, so two elements with one ordinal key sit side by side, and a case-only pair is found by the set.
    private static Error? Duplicate(SortedArray<Element> model, IEqualityComparer<ElementKey> comparer, Collation collation)
    {
        var seen = new HashSet<ElementKey>(comparer);
        return model.Where(e => !seen.Add(e.Key)).Select(e => new Error(
            "change.duplicate-key",
            "Two elements of one model have the key " + e.Key + (collation.IsCaseSensitive ? "." : " under " + collation.Name + ", which reads letter case as one name."),
            "Report the model's source: a key names one element, so what read this model into elements has a defect.")).FirstOrDefault();
    }

    /// <summary>An element in both models that is altered: its key after, and each property and relationship that differs.</summary>
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
