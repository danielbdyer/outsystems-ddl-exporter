using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// Where a precondition's rows are in an environment, by the coordinate rules of WP 2.1 (the Facsimile's S3: a missing object is a refusal,
/// never zero rows). An object the change creates is answered from the change: a table it creates holds no row, so a precondition on one
/// is answered with no rows and none violating; a column it creates holds one value in every existing row of its table, its default when
/// it is NOT NULL and NULL when it allows NULL (<see cref="Place.Default"/>), which WP 2.3's queries read where the column would stand. An
/// object the change renames or moves, or one in a table it renames or moves, is counted under the key it had. Anything else is counted
/// where it is. An object the environment does not hold under that key is the error plan.missing-object. A Computed or an Authored, whose
/// state a person declares, is placed by the same rules and answered by neither a count nor the change (<see cref="Declared"/>). The cases
/// are closed.
/// </summary>
public abstract record Coordinates
{
    private Coordinates()
    {
    }

    public T Match<T>(Func<Answered, T> answered, Func<Counted, T> counted, Func<Declared, T> declared) => this switch
    {
        Answered a => answered(a),
        Counted c => counted(c),
        Declared d => declared(d),
        _ => throw new UnreachableException(),
    };

    /// <summary>Where <paramref name="precondition"/> is answered, with names compared under a case-sensitive collation; a caller
    /// planning against an environment of another collation passes it to the four-argument form.</summary>
    public static Result<Coordinates> Of(Precondition precondition, Change change, SortedArray<Element> environment) =>
        Of(precondition, change, environment, Collation.CaseSensitive);

    /// <summary>Where <paramref name="precondition"/> is answered: from <paramref name="change"/>, by a count in
    /// <paramref name="environment"/>, the model of the environment the change is planned against, or by a person for a Computed or an
    /// Authored; names are compared under <paramref name="collation"/> as Change.Between compares them, and a place names each object as
    /// the environment spells it.</summary>
    public static Result<Coordinates> Of(Precondition precondition, Change change, SortedArray<Element> environment, Collation collation)
    {
        var comparer = ElementKey.Comparer(collation);
        var made = change.Created.Select(e => e.Key).ToHashSet(comparer);
        var held = new Dictionary<ElementKey, ElementKey>(comparer);
        foreach (var element in environment)
        {
            held.TryAdd(element.Key, element.Key);
        }

        if (Table(precondition.Table, made, held, change).Failed(out var table, out var error))
        {
            return error;
        }

        if (made.Contains(table) && !precondition.Kind.Declared)
        {
            return new Answered(new PreconditionCount(precondition, 0, 0));
        }

        // The table, the columns the precondition reads, and a foreign key's parent and its columns or a filter's columns.
        IEnumerable<Result<ElementKey>> Columns(ElementKey on, IEnumerable<Name> names) =>
            names.Select(n => ElementKey.Of(on, "Column", n));
        var objects = new List<Result<ElementKey>> { table };
        objects.AddRange(Columns(table, precondition.Columns));
        if (precondition.Kind is PreconditionKind.ForeignKey foreign)
        {
            var parent = Table(foreign.Parent, made, held, change);
            objects.Add(parent);
            objects.AddRange(parent is Result<ElementKey>.Ok(var referenced)
                ? Columns(referenced, foreign.Pairs.Select(p => p.Referenced))
                : []);
        }
        else if (precondition.Kind is PreconditionKind.Unique unique)
        {
            objects.AddRange(Columns(table, unique.FilterColumns));
        }

        if (Result.All(objects).Failed(out var keys, out error))
        {
            return error;
        }

        var places = new List<Place>();
        foreach (var key in keys.Distinct())
        {
            var was = change.KeyBefore(key);
            if (made.Contains(key) || (key.Parent is { } parent && made.Contains(parent)))
            {
                places.Add(new Place(key, null, key.Parent is { } owner && !made.Contains(owner) ? Default(key, change) : null));
            }
            else if (held.TryGetValue(was, out var spelled))
            {
                places.Add(new Place(key, spelled, null));
            }
            else
            {
                return new Error("plan.missing-object",
                    "The environment holds no " + was
                        + (was == key ? "" : ", the key " + key + " had before the change renamed it or its table")
                        + ", which a precondition of the change reads and the change does not create; a missing object is never counted"
                        + " as empty.",
                    "Predict against the environment the change was planned for, or record that environment as not checked.");
            }
        }

        var placed = SortedArray.Of(places);
        return precondition.Kind.Declared ? new Declared(precondition, placed) : new Counted(precondition, placed);
    }

    /// <summary>
    /// The key of a table a precondition names: a Table, or a View where the change creates or the environment holds a view of that name
    /// (an indexed view's unique index); a Table when neither holds one, for the error that names it.
    /// </summary>
    private static Result<ElementKey> Table(Name name, HashSet<ElementKey> made, Dictionary<ElementKey, ElementKey> held, Change change) =>
        ElementKey.Of("Table", name).Bind(table => ElementKey.Of("View", name).Map(view =>
            !Holds(table, made, held, change) && Holds(view, made, held, change) ? view : table));

    private static bool Holds(ElementKey key, HashSet<ElementKey> made, Dictionary<ElementKey, ElementKey> held, Change change) =>
        made.Contains(key) || held.ContainsKey(change.KeyBefore(key));

    /// <summary>The value a column the change creates gives every existing row: its default's expression when the column is NOT NULL,
    /// else null for NULL.</summary>
    private static string? Default(ElementKey column, Change change) =>
        change.Created.FirstOrDefault(e => e.Key == column)?["Nullable"] is not Value.Boolean { IsTrue: false } ? null
        : change.Created.FirstOrDefault(e => e.Key.Type == "DefaultConstraint"
            && e.Relationships.Any(r => r.Name == "TargetColumn" && r.Targets.Any(t => t.Key == column)))?["Expression"] switch
        {
            Value.Text t => t.Content,
            Value.Script s => s.Content,
            _ => null,
        };

    /// <summary>The change creates the table of a precondition a count answers, which holds no row: the count is known, no rows and none
    /// violating.</summary>
    public sealed record Answered(PreconditionCount Count) : Coordinates;

    /// <summary>Counted in the environment: each table and column the precondition reads, and where the environment holds it.</summary>
    public sealed record Counted(Precondition Precondition, SortedArray<Place> Places) : Coordinates;

    /// <summary>
    /// A Computed or an Authored, whose state a person declares (<see cref="ExistingData.Of"/>'s declared states) and no count gives: each
    /// table and column it reads, where the environment holds it or nowhere for one the change creates, which the person looks at. It is
    /// never answered from the change, even on a table the change creates: a deploy script can fill that table before the statement that
    /// reads it, as a post-deploy INSERT fills a new table before DacFx validates its foreign key (the Facsimile's QST-59), so an empty
    /// table is no answer.
    /// </summary>
    public sealed record Declared(Precondition Precondition, SortedArray<Place> Places) : Coordinates;
}

/// <summary>
/// One table or column a precondition reads, and where an environment holds it: <see cref="After"/>, its key after the change;
/// <see cref="Environment"/>, the key the environment holds it under, its own or the one it had before the change renamed or moved it or
/// its table, or null for an object the change creates; and, for a column the change creates in a table it does not, the
/// <see cref="Default"/> every existing row of the table holds: its default's expression as the model writes it when the column is NOT
/// NULL, else null for NULL. DacFx 170.5.96 adds a column that allows NULL with no WITH VALUES, so its existing rows hold NULL whatever
/// its default; and it adds a NOT NULL column with no default to an empty table alone (Msg 4901), which a BlockOnPossibleDataLoss site
/// asks. WP 2.3's queries read this value where the column's name would stand, and a table the change creates as a table with no row.
/// </summary>
public sealed record Place(ElementKey After, ElementKey? Environment, string? Default) : IComparable<Place>
{
    public int CompareTo(Place? other) =>
        other is null ? 1
        : After.CompareTo(other.After) is var a and not 0 ? a
        : (Environment, other.Environment) switch
        {
            (null, null) => string.CompareOrdinal(Default, other.Default),
            (null, _) => -1,
            (_, null) => 1,
            var (mine, theirs) => mine.CompareTo(theirs) is var e and not 0 ? e : string.CompareOrdinal(Default, other.Default),
        };
}
