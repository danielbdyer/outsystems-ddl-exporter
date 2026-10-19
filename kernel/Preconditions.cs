using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// A place where existing rows decide the outcome (V3_MILESTONES.md §3): a table under BlockOnPossibleDataLoss, or a rule the change
/// asserts about the rows a table already holds. It names the table as the change's after model does, the columns the rule reads, and
/// what it asks; an environment counts it under the names it holds (<see cref="Coordinates"/>), so one precondition is the same value in
/// every environment's existing data. Preconditions sort by table, then kind, then columns, so one table's sit together, as WP 2.3 counts
/// them.
/// </summary>
public sealed record Precondition(Name Table, SortedArray<Name> Columns, PreconditionKind Kind) : IComparable<Precondition>
{
    /// <summary>The kind, the table and the columns, culture-free: <c>NotNull on [dbo].[Customer] ([Email])</c>.</summary>
    public override string ToString() =>
        Kind.Match(() => "TableEmpty", () => "NotNull", _ => "Fits", _ => "ForeignKey", _ => "Unique", _ => "Check") + " on " + Table
        + (Columns.Count == 0 ? "" : " (" + string.Join(", ", Columns) + ")");

    public int CompareTo(Precondition? other) =>
        other is null ? 1
        : Table.CompareTo(other.Table) is var t and not 0 ? t
        : Kind.CompareTo(other.Kind) is var k and not 0 ? k
        : SortedArray.Compare(Columns, other.Columns);
}

/// <summary>
/// What a precondition asks of the rows, by the precondition table of WP 2.1: TableEmpty, that a table BlockOnPossibleDataLoss checks holds
/// no row; NotNull, that no row holds NULL in a column made NOT NULL; Fits, that every value of a column whose type changes converts to the
/// new type; ForeignKey, that each child row whose key columns are all non-null has its parent; Unique, that no two rows share a key within
/// the key's filter; and Check, that no row makes the check false. The cases are closed, and sort in that order. The kinds only a plan's
/// statements show (KeyLength, Rebuild, Computed, Temporal, Scan, Authored and SeedDrift) arrive with the second slice of WP 2.1, which
/// matches statements by the Facsimile's vocabulary once spike S2 has measured them.
/// </summary>
public abstract record PreconditionKind : IComparable<PreconditionKind>
{
    private PreconditionKind()
    {
    }

    public T Match<T>(Func<T> tableEmpty, Func<T> notNull, Func<Fits, T> fits, Func<ForeignKey, T> foreignKey, Func<Unique, T> unique,
        Func<Check, T> check) => this switch
        {
            TableEmpty => tableEmpty(),
            NotNull => notNull(),
            Fits f => fits(f),
            ForeignKey k => foreignKey(k),
            Unique u => unique(u),
            Check c => check(c),
            _ => throw new UnreachableException(),
        };

    /// <summary>By the order the cases are declared in, then by what each holds; an order for sorting alone, which says nothing of which of
    /// two types is wider.</summary>
    public int CompareTo(PreconditionKind? other) => (this, other) switch
    {
        (_, null) => 1,
        (Fits a, Fits b) => Compare(a.Before, b.Before) is var before and not 0 ? before : Compare(a.After, b.After),
        (ForeignKey a, ForeignKey b) => a.Parent.CompareTo(b.Parent) is var p and not 0 ? p : SortedArray.Compare(a.Pairs, b.Pairs),
        (Unique a, Unique b) => string.CompareOrdinal(a.Filter, b.Filter) is var f and not 0 ? f
            : SortedArray.Compare(a.FilterColumns, b.FilterColumns),
        (Check a, Check b) => string.CompareOrdinal(a.Expression, b.Expression),
        _ => Rank(this).CompareTo(Rank(other)),
    };

    private static int Rank(PreconditionKind kind) => kind.Match(() => 0, () => 1, _ => 2, _ => 3, _ => 4, _ => 5);

    private static int Compare(ColumnType a, ColumnType b) =>
        a.Type.CompareTo(b.Type) is var t and not 0 ? t
        : a.Length.CompareTo(b.Length) is var l and not 0 ? l
        : a.IsMax.CompareTo(b.IsMax) is var m and not 0 ? m
        : a.Precision.CompareTo(b.Precision) is var p and not 0 ? p
        : a.Scale.CompareTo(b.Scale) is var s and not 0 ? s
        : string.CompareOrdinal(a.Collation, b.Collation);

    /// <summary>The table holds no row: what BlockOnPossibleDataLoss checks before the statement it protects.</summary>
    public sealed record TableEmpty : PreconditionKind;

    /// <summary>No row holds NULL in the column, which the change makes NOT NULL.</summary>
    public sealed record NotNull : PreconditionKind;

    /// <summary>Every value of the column converts from the type it has before the change to the type it has after.</summary>
    public sealed record Fits(ColumnType Before, ColumnType After) : PreconditionKind;

    /// <summary>Every child row whose key columns are all non-null has a parent row: the parent table, and for each child column the
    /// parent column it references.</summary>
    public sealed record ForeignKey(Name Parent, SortedArray<ForeignKey.Pair> Pairs) : PreconditionKind
    {
        /// <summary>A child column and the parent column it references.</summary>
        public sealed record Pair(Name Column, Name Referenced) : IComparable<Pair>
        {
            public int CompareTo(Pair? other) =>
                other is null ? 1 : Column.CompareTo(other.Column) is var c and not 0 ? c : Referenced.CompareTo(other.Referenced);
        }
    }

    /// <summary>No two rows within the filter share the key: the filter of a filtered index as the model writes it, and the columns it
    /// reads; null and none for a key with no filter.</summary>
    public sealed record Unique(string? Filter, SortedArray<Name> FilterColumns) : PreconditionKind;

    /// <summary>No row makes the check false: its expression as the model writes it.</summary>
    public sealed record Check(string Expression) : PreconditionKind;
}

/// <summary>A precondition's state in one environment: whether its table holds a row, and whether a row breaks its rule.</summary>
public sealed record PreconditionState(Precondition Precondition, bool Populated, bool Violating) : IComparable<PreconditionState>
{
    public int CompareTo(PreconditionState? other) =>
        other is null ? 1
        : Precondition.CompareTo(other.Precondition) is var p and not 0 ? p
        : Populated.CompareTo(other.Populated) is var r and not 0 ? r
        : Violating.CompareTo(other.Violating);
}

/// <summary>
/// What a count says of one precondition in an environment: the rows its table holds and the rows that break its rule. WP 2.3's aggregate
/// queries count them; a precondition the change answers (<see cref="Coordinates.Answered"/>) comes with zero of each.
/// </summary>
public sealed record PreconditionCount(Precondition Precondition, long Rows, long Violating) : IComparable<PreconditionCount>
{
    public int CompareTo(PreconditionCount? other) =>
        other is null ? 1
        : Precondition.CompareTo(other.Precondition) is var p and not 0 ? p
        : Rows.CompareTo(other.Rows) is var r and not 0 ? r
        : Violating.CompareTo(other.Violating);
}

/// <summary>
/// An environment's data as the outcome depends on it (decision N5): the state of each precondition its plan holds, derived from counts,
/// which the privacy closure admits, so it carries no value of any row. Two are equal when their states are, one by one.
/// </summary>
public sealed record ExistingData(SortedArray<PreconditionState> States)
{
    /// <summary>
    /// The states the counts give: a precondition is populated when a count finds a row in its table and violating when one finds a row
    /// that breaks its rule; a TableEmpty precondition is violating exactly when it is populated, since BlockOnPossibleDataLoss reads row
    /// presence alone (law 9). Two counts of one precondition give one state, populated or violating when either says so.
    /// </summary>
    public static ExistingData Of(IEnumerable<PreconditionCount> counts) => new(SortedArray.Of(counts.GroupBy(c => c.Precondition)
        .Select(g =>
        {
            var populated = g.Any(c => c.Rows > 0 || c.Violating > 0);
            return new PreconditionState(g.Key, populated,
                g.Key.Kind is PreconditionKind.TableEmpty ? populated : g.Any(c => c.Violating > 0));
        })));

    /// <summary>The state this existing data holds for <paramref name="precondition"/>, or null when it holds none.</summary>
    public PreconditionState? State(Precondition precondition) => States.FirstOrDefault(s => s.Precondition == precondition);
}

/// <summary>
/// A BlockOnPossibleDataLoss check in an environment's plan, as WP 2.2's reader finds it in DacFx's script (§1 fact 3: an IF EXISTS over
/// the table that raises at state 127): the table whose rows stop the publish, and the statement the check protects, as DacFx writes it.
/// </summary>
public sealed record BlockOnPossibleDataLossSite(Name Table, string Statement) : IComparable<BlockOnPossibleDataLossSite>
{
    public int CompareTo(BlockOnPossibleDataLossSite? other) =>
        other is null ? 1 : Table.CompareTo(other.Table) is var t and not 0 ? t : string.CompareOrdinal(Statement, other.Statement);
}

/// <summary>The preconditions of a change, and the operations of an environment's plan the change does not make.</summary>
public static class Preconditions
{
    /// <summary>
    /// The preconditions a change between two models asserts, by the precondition table of WP 2.1, and TableEmpty on the table each
    /// BlockOnPossibleDataLoss site of the plan checks. A column whose Nullable goes from true to false is NotNull. A column whose
    /// DataType relationship, Length, IsMax, Precision, Scale or Collation differs is Fits, whichever way it moves (decision 2.9); a
    /// computed column declares no type, and its kind arrives with WP 2.1's second slice. A primary key, a unique constraint or a unique
    /// index is Unique, a foreign key is ForeignKey and a check is Check, each when the after model holds it enabled and the change
    /// creates or alters it, or when it reads a column that is Fits. ALTER COLUMN refuses a column a primary key, a foreign key or a
    /// check reads, and most changes to one an index reads, so DacFx drops the rule and creates it again around the ALTER COLUMN (its
    /// scripts in the Facsimile's QST-33 and QST-76 do so for a unique index), and the new one is validated against the rows; where SQL
    /// Server alters the column in place, as it widens a varchar under a unique index, the count finds no row the change could break. A
    /// model that lacks an element the change names, or a relationship the table reads, is the error model.unresolved.
    /// </summary>
    public static Result<SortedArray<Precondition>> Of(SortedArray<Element> before, SortedArray<Element> after, Change change,
        SortedArray<BlockOnPossibleDataLossSite> sites)
    {
        var (was, now) = (Keyed(before), Keyed(after));
        var found = new List<Precondition>(sites.Select(s => new Precondition(s.Table, [], new PreconditionKind.TableEmpty())));
        var retyped = new HashSet<ElementKey>();
        foreach (var column in change.Altered.Where(a => a.Key.Type == "Column" && a.Key.Parent is not null))
        {
            var (table, name) = (column.Key.Parent!.Name, SortedArray.Of(column.Key.Name));
            if (column.Properties.Any(p =>
                p is { Name: "Nullable", Before: Value.Boolean { IsTrue: true }, After: Value.Boolean { IsTrue: false } }))
            {
                found.Add(new Precondition(table, name, new PreconditionKind.NotNull()));
            }

            if (!column.Relationships.Any(r => r.Name == "DataType") && !column.Properties.Any(p => Facets.Contains(p.Name)))
            {
                continue;
            }

            if (!was.TryGetValue(change.KeyBefore(column.Key), out var old) || !now.TryGetValue(column.Key, out var current))
            {
                return Unresolved(column.Key + " is altered by the change, and the model before it or after it holds no such column.");
            }

            if (Declares(old) && Declares(current))
            {
                if (ColumnType.Of(old).Bind(b => ColumnType.Of(current).Map(a => new PreconditionKind.Fits(b, a)))
                    .Failed(out var fits, out var error))
                {
                    return error;
                }

                found.Add(new Precondition(table, name, fits));
                retyped.Add(column.Key);
            }
        }

        var touched = change.Created.Select(e => e.Key).Concat(change.Altered.Select(a => a.Key)).ToHashSet();
        foreach (var rule in after.Where(e => e["Disabled"] is not Value.Boolean { IsTrue: true }
            && (touched.Contains(e.Key) || Reads(e).Any(retyped.Contains))))
        {
            if (Rule(rule) is { } made)
            {
                if (made.Failed(out var precondition, out var error))
                {
                    return error;
                }

                found.Add(precondition);
            }
        }

        return SortedArray.Of(found.Distinct());
    }

    /// <summary>
    /// Baseline drift: the operations of an environment's plan that the change itself does not make, by the object each names. An
    /// operation is the change's when its object is, under <paramref name="collation"/>, one the change creates, drops, renames or
    /// alters, or holds one, as DacFx's report names a column's alteration by its table's. A Refresh, UnbindSchemabinding or
    /// RebindSchemabinding is DacFx's consequence of another operation and is left out.
    /// </summary>
    public static SortedArray<PlanOperation> BaselineDrift(DeployReport plan, Change change, Collation collation)
    {
        var made = new HashSet<ElementKey>(ElementKey.Comparer(collation));
        foreach (var key in change.Created.Concat(change.Dropped).Select(e => e.Key)
            .Concat(change.Renamed.Concat(change.CaseOnlyRenamed).SelectMany(r => new[] { r.Before, r.After }))
            .Concat(change.Altered.SelectMany(a => new[] { a.Key, change.KeyBefore(a.Key) })))
        {
            for (ElementKey? at = key; at is not null && made.Add(at); at = at.Parent)
            {
            }
        }

        return SortedArray.Of(plan.Operations.Where(o => !o.Kind.IsConsequence && !made.Contains(o.Key)));
    }

    /// <summary>The properties of a column that, with its DataType relationship, make up its type (decision 2.9).</summary>
    private static readonly string[] Facets = ["Length", "IsMax", "Precision", "Scale", "Collation"];

    /// <summary>Whether a column declares a type: a computed column has an expression and no DataType.</summary>
    private static bool Declares(Element column) => column.Relationships.Any(r => r.Name == "DataType");

    /// <summary>
    /// The precondition a primary key, a unique constraint, a unique index, a foreign key or a check asserts, or null for any other
    /// element. A key or an index names its table as Host or IndexedObject and its key as Columns, an index its filter as
    /// FilterPredicate and the columns the filter reads as BodyDependencies; a foreign key its table, columns, parent and parent's
    /// columns as Host, Columns, ForeignTable and ForeignColumns, paired by position; a check its table, the columns it reads and its
    /// expression as Host, ExpressionDependencies and Expression.
    /// </summary>
    private static Result<Precondition>? Rule(Element e)
    {
        var columns = Targets(e, "Columns");
        switch (e.Key.Type)
        {
            case "PrimaryKeyConstraint" or "UniqueConstraint":
                return One(e, "Host").Map(host => new Precondition(host.Name, Names(columns), new PreconditionKind.Unique(null, [])));
            case "Index" when e["Unique"] is Value.Boolean { IsTrue: true }:
                var filter = Text(e["FilterPredicate"]);
                var filtered = filter is null ? [] : Names(Targets(e, "BodyDependencies"));
                return One(e, "IndexedObject")
                    .Map(host => new Precondition(host.Name, Names(columns), new PreconditionKind.Unique(filter, filtered)));
            case "ForeignKeyConstraint":
                var parents = Targets(e, "ForeignColumns");
                return columns.Count == 0 || columns.Count != parents.Count
                    ? Unresolved(e.Key + " pairs its columns with its parent table's by position, and its Columns and ForeignColumns hold "
                        + columns.Count.ToString(CultureInfo.InvariantCulture) + " and "
                        + parents.Count.ToString(CultureInfo.InvariantCulture) + " targets.")
                    : One(e, "Host").Bind(host => One(e, "ForeignTable").Map(parent => new Precondition(host.Name, Names(columns),
                        new PreconditionKind.ForeignKey(parent.Name,
                            SortedArray.Of(columns.Zip(parents, (c, p) => new PreconditionKind.ForeignKey.Pair(c.Name, p.Name)))))));
            case "CheckConstraint":
                return Text(e["Expression"]) is not { } expression ? Unresolved(e.Key + " holds no Expression.")
                    : One(e, "Host").Map(host => new Precondition(host.Name, Names(Targets(e, "ExpressionDependencies")),
                        new PreconditionKind.Check(expression)));
            default:
                return null;
        }
    }

    /// <summary>The columns a rule reads, whose change of type makes DacFx drop the rule and create it again.</summary>
    private static IEnumerable<ElementKey> Reads(Element e) =>
        ((string[])["Columns", "ForeignColumns", "ExpressionDependencies", "BodyDependencies"]).SelectMany(name => Targets(e, name));

    /// <summary>The targets of a relationship that are columns, in DacFx's order.</summary>
    private static IReadOnlyList<ElementKey> Targets(Element e, string relationship) =>
        [.. (e.Relationships.FirstOrDefault(r => r.Name == relationship)?.Targets ?? []).Select(t => t.Key).Where(k => k.Type == "Column")];

    /// <summary>The one target of a relationship, or model.unresolved.</summary>
    private static Result<ElementKey> One(Element e, string relationship) =>
        e.Relationships.FirstOrDefault(r => r.Name == relationship)?.Targets is [var target] ? target.Key
            : Unresolved(e.Key + " has no one " + relationship + " target.");

    private static SortedArray<Name> Names(IEnumerable<ElementKey> keys) => SortedArray.Of(keys.Select(k => k.Name));

    private static string? Text(Value? value) => value switch
    {
        Value.Text t => t.Content,
        Value.Script s => s.Content,
        _ => null,
    };

    /// <summary>A model's elements by key, the first of any key given twice, which Change.Between refuses before this reads it.</summary>
    private static Dictionary<ElementKey, Element> Keyed(SortedArray<Element> model)
    {
        var keyed = new Dictionary<ElementKey, Element>();
        foreach (var e in model)
        {
            keyed.TryAdd(e.Key, e);
        }

        return keyed;
    }

    private static Error Unresolved(string message) => new("model.unresolved", message, ColumnType.ReadModelDefect);
}
