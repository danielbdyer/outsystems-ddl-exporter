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
    /// <summary>The kind, the table and the columns, culture-free, and what a kind only a plan's statements show adds:
    /// <c>NotNull on [dbo].[Customer] ([Email])</c>, <c>KeyLength on [dbo].[N] ([Big]) of [IX_N_Big] at most 1700 bytes</c>.</summary>
    public override string ToString() =>
        Kind.Word + " on " + Table + (Columns.Count == 0 ? "" : " (" + string.Join(", ", Columns) + ")") + Kind.Detail;

    public int CompareTo(Precondition? other) =>
        other is null ? 1
        : Table.CompareTo(other.Table) is var t and not 0 ? t
        : Kind.CompareTo(other.Kind) is var k and not 0 ? k
        : SortedArray.Compare(Columns, other.Columns);
}

/// <summary>
/// What a precondition asks of the rows. By the precondition table of WP 2.1, which the property read finds: TableEmpty, that a table
/// BlockOnPossibleDataLoss checks holds no row; NotNull, that no row holds NULL in a column made NOT NULL; Fits, that every value of a
/// column whose type changes converts to the new type; ForeignKey, that each child row whose key columns are all non-null has its parent;
/// Unique, that no two rows share a key within the key's filter; and Check, that no row makes the check false. Then the kinds only a plan's
/// statements show, which <see cref="Vocabulary"/>'s rows find (Preconditions.Kinds.cs). The cases are closed, and sort in that order.
/// </summary>
public abstract partial record PreconditionKind : IComparable<PreconditionKind>
{
    private PreconditionKind()
    {
    }

    public T Match<T>(Func<T> tableEmpty, Func<T> notNull, Func<Fits, T> fits, Func<ForeignKey, T> foreignKey, Func<Unique, T> unique,
        Func<Check, T> check, Func<KeyLength, T> keyLength, Func<T> rebuild, Func<Computed, T> computed, Func<Temporal, T> temporal,
        Func<T> scan, Func<Authored, T> authored, Func<SeedDrift, T> seedDrift) => this switch
        {
            TableEmpty => tableEmpty(),
            NotNull => notNull(),
            Fits f => fits(f),
            ForeignKey k => foreignKey(k),
            Unique u => unique(u),
            Check c => check(c),
            KeyLength l => keyLength(l),
            Rebuild => rebuild(),
            Computed c => computed(c),
            Temporal t => temporal(t),
            Scan => scan(),
            Authored a => authored(a),
            SeedDrift s => seedDrift(s),
            _ => throw new UnreachableException(),
        };

    /// <summary>The kind's name, as the Facsimile's vocabulary and v3's words write it: TableEmpty, KeyLength, SeedDrift.</summary>
    public string Word => Match(() => "TableEmpty", () => "NotNull", _ => "Fits", _ => "ForeignKey", _ => "Unique", _ => "Check",
        _ => "KeyLength", () => "Rebuild", _ => "Computed", _ => "Temporal", () => "Scan", _ => "Authored", _ => "SeedDrift");

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
        (KeyLength a, KeyLength b) => Nullable.Compare(a.Index, b.Index) is var i and not 0 ? i : a.Clustered.CompareTo(b.Clustered),
        (Computed a, Computed b) => string.CompareOrdinal(a.Expression, b.Expression),
        (Temporal a, Temporal b) => a.Start.CompareTo(b.Start) is var s and not 0 ? s
            : a.End.CompareTo(b.End) is var e and not 0 ? e : Nullable.Compare(a.Current, b.Current),
        (Authored a, Authored b) => a.Script.CompareTo(b.Script) is var s and not 0 ? s : a.Line.CompareTo(b.Line),
        (SeedDrift a, SeedDrift b) => a.Script.CompareTo(b.Script) is var s and not 0 ? s
            : a.Line.CompareTo(b.Line) is var l and not 0 ? l : SortedArray.Compare(a.Code, b.Code),
        _ => Rank(this).CompareTo(Rank(other)),
    };

    private static int Rank(PreconditionKind kind) =>
        kind.Match(() => 0, () => 1, _ => 2, _ => 3, _ => 4, _ => 5, _ => 6, () => 7, _ => 8, _ => 9, () => 10, _ => 11, _ => 12);

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

/// <summary>
/// A precondition's state in one environment: whether its table holds a row, whether a row breaks its rule, and, for Fits, Unique and
/// Rebuild, the classes whose rows the table holds (<see cref="RowClasses"/>), which WP 2.3's queries count beside the violating rows; none
/// for another kind, or where the counts named no class. A Scan or a Rebuild is never violating, and a Computed or an Authored state is a
/// person's declaration (<see cref="ExistingData.Of"/>).
/// </summary>
public sealed record PreconditionState(Precondition Precondition, bool Populated, bool Violating) : IComparable<PreconditionState>
{
    public RowClasses Classes { get; init; }

    public int CompareTo(PreconditionState? other) =>
        other is null ? 1
        : Precondition.CompareTo(other.Precondition) is var p and not 0 ? p
        : Populated.CompareTo(other.Populated) is var r and not 0 ? r
        : Violating.CompareTo(other.Violating) is var v and not 0 ? v
        : ((int)Classes).CompareTo((int)other.Classes);
}

/// <summary>
/// The value classes of Fits, the collation classes of Unique and the gapped class of Rebuild (the Facsimile's ARCHITECTURE.md §4), one
/// flag each, so a set of them is one value: the kinds of row WP 2.3's aggregate queries count beside a precondition's violating rows,
/// named and ordered as io/AggregateQueries.cs names its Class, and gapped, which no query counts, since MIN and MAX of a column are values
/// no form of the allowlist returns, so a state holds it only where it is given. Of Fits: over length, a value longer than the new type
/// holds once its trailing spaces are gone; over bytes, longer only with them; not convertible, a value TRY_CONVERT gives NULL; an empty
/// string; overflow, a value of the same kind beyond the new type's range; and a lossy round trip, a value ALTER COLUMN writes without an
/// error and loses. Of Unique: exact, two keys of the same bytes; case, two that differ in letter case alone; accent, in accents alone;
/// trailing space, in trailing spaces alone; and nulls, two keys holding NULL, which a unique index holds equal. Of Rebuild: gapped,
/// identity values that skip a number or do not start at the seed, which a rebuild that keeps its ids carries into the new table (the
/// Facsimile's PLT-82).
/// </summary>
[Flags]
public enum RowClasses
{
    None = 0,
    OverLength = 1,
    OverBytes = 2,
    NotConvertible = 4,
    EmptyString = 8,
    Overflow = 16,
    LossyRoundTrip = 32,
    Exact = 64,
    Case = 128,
    Accent = 256,
    TrailingSpace = 512,
    Nulls = 1024,
    Gapped = 2048,
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
    /// The states the counts give, and the states a person declares: a precondition is populated when a count finds a row in its table and
    /// violating when one finds a row that breaks its rule; a TableEmpty precondition is violating exactly when it is populated, since
    /// BlockOnPossibleDataLoss reads row presence alone (law 9), and a Scan or a Rebuild never is, since no row fails its statement. Two
    /// counts of one precondition give one state, populated or violating when either says so. A Computed or an Authored precondition is
    /// declared and never counted, which Coordinates.Of places as <see cref="Coordinates.Declared"/> wherever its table is, so a count of
    /// one, or a declared state of another kind, is an argument no caller passes.
    /// </summary>
    public static ExistingData Of(IEnumerable<PreconditionCount> counts, IEnumerable<PreconditionState>? declared = null)
    {
        var (counted, given) = (counts.ToArray(), (declared ?? []).ToArray());
        if (counted.FirstOrDefault(c => c.Precondition.Kind.Declared) is { } count)
        {
            throw new ArgumentException(count.Precondition + " is declared by a person and never counted.", nameof(counts));
        }

        if (given.FirstOrDefault(s => !s.Precondition.Kind.Declared) is { } state)
        {
            throw new ArgumentException(state.Precondition + " is counted, and no person declares its state.", nameof(declared));
        }

        return new(SortedArray.Of(counted.GroupBy(c => c.Precondition).Select(g =>
        {
            var populated = g.Any(c => c.Rows > 0 || c.Violating > 0);
            return new PreconditionState(g.Key, populated,
                g.Key.Kind is PreconditionKind.TableEmpty ? populated : g.Key.Kind.Breakable && g.Any(c => c.Violating > 0));
        }).Concat(given)));
    }

    /// <summary>The state this existing data holds for <paramref name="precondition"/>, or null when it holds none.</summary>
    public PreconditionState? State(Precondition precondition) => States.FirstOrDefault(s => s.Precondition == precondition);
}

/// <summary>
/// A BlockOnPossibleDataLoss check in an environment's plan, as WP 2.2's reader finds it in DacFx's script (§1 fact 3: an IF EXISTS over
/// the table that raises at state 127): the table whose rows stop the publish, and the statement the check protects, as DacFx describes
/// it in the block comment it writes before the check (io/BlockOnPossibleDataLoss), empty where DacFx wrote none.
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
    /// computed column declares no type, and is Computed where the plan computes it (<see cref="Vocabulary"/>). A primary key, a unique
    /// constraint or a unique index is Unique, a foreign key is ForeignKey and a check is Check, each when the after model holds it enabled
    /// and the change creates or alters it, or when it reads a column that is Fits. ALTER COLUMN refuses a column a primary key, a foreign
    /// key or a check reads, and most changes to one an index reads, so DacFx drops the rule and creates it again around the ALTER COLUMN
    /// (its scripts in the Facsimile's QST-33 and QST-76 do so for a unique index), and the new one is validated against the rows; where
    /// SQL Server alters the column in place, as it widens a varchar under a unique index, the count finds no row the change could break. A
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
    /// Baseline drift: the operations of an environment's plan that the change, from a model to <paramref name="after"/>, the package's,
    /// does not make, by the object each names. An operation is the change's when its object is, under <paramref name="collation"/>,
    /// one the change creates, drops, renames or alters, or holds one, as DacFx's report names a column's alteration by its table's. So
    /// are the operations DacFx 170.5.96 adds around the change's own, as measured package to package: a Refresh, UnbindSchemabinding or
    /// RebindSchemabinding of a module over a changed object; a Drop and a Create of one object the after model holds that reads a column
    /// the change alters, or a computed column over one, since DacFx drops an index, a statistic, a key, a foreign key, a check or a
    /// default before an ALTER COLUMN of a column it reads and creates it after; and a Drop or a Create of an object the after model holds
    /// on a table the change's own TableRebuild creates again, or over a column of one. A Create alone of an object over an altered column
    /// is drift: the environment lacks that object, and the plan creates it whatever the change does.
    /// </summary>
    public static SortedArray<PlanOperation> BaselineDrift(DeployReport plan, SortedArray<Element> after, Change change,
        Collation collation)
    {
        var comparer = ElementKey.Comparer(collation);
        var made = new HashSet<ElementKey>(comparer);
        foreach (var key in change.Created.Concat(change.Dropped).Select(e => e.Key)
            .Concat(change.Renamed.Concat(change.CaseOnlyRenamed).SelectMany(r => new[] { r.Before, r.After }))
            .Concat(change.Altered.SelectMany(a => new[] { a.Key, change.KeyBefore(a.Key) })))
        {
            for (ElementKey? at = key; at is not null && made.Add(at); at = at.Parent)
            {
            }
        }

        var held = new Dictionary<ElementKey, Element>(comparer);
        foreach (var e in after)
        {
            held.TryAdd(e.Key, e);
        }

        // The columns DacFx alters: each the change alters, then each computed column over one, which DacFx drops and adds again.
        var altered = new HashSet<ElementKey>(change.Altered.Select(a => a.Key).Where(k => k.Type == "Column"), comparer);
        while (after.FirstOrDefault(e => e.Key.Type == "Column" && !altered.Contains(e.Key)
            && Targets(e, "ExpressionDependencies").Any(altered.Contains)) is { } computed)
        {
            altered.Add(computed.Key);
        }

        HashSet<ElementKey> Named(Func<PlanOperationKind, bool> kind) =>
            plan.Operations.Where(o => kind(o.Kind)).Select(o => o.Key).ToHashSet(comparer);
        var rebuilt = Named(k => k is PlanOperationKind.TableRebuild);
        rebuilt.IntersectWith(made);
        var (dropped, created) = (Named(k => k is PlanOperationKind.Drop), Named(k => k is PlanOperationKind.Create));
        bool Recreated(PlanOperation o) => o.Kind is PlanOperationKind.Drop or PlanOperationKind.Create
            && held.TryGetValue(o.Key, out var e)
            && ((dropped.Contains(o.Key) && created.Contains(o.Key) && ColumnsRead(e).Any(altered.Contains))
                || ColumnsRead(e).Select(c => c.Parent).Concat(Hosts(e)).Any(t => t is not null && rebuilt.Contains(t)));
        return SortedArray.Of(plan.Operations.Where(o => !o.Kind.IsConsequence && !made.Contains(o.Key) && !Recreated(o)));
    }

    /// <summary>The columns an object reads, so that DacFx drops it before an ALTER COLUMN of one and creates it after: an index's or a
    /// statistic's, a key's or a foreign key's Columns, an index's IncludedColumns and its filter's BodyDependencies, a foreign key's
    /// ForeignColumns, a check's or a computed column's ExpressionDependencies, and a default's TargetColumn.</summary>
    private static IEnumerable<ElementKey> ColumnsRead(Element e) =>
        ((string[])["Columns", "IncludedColumns", "ForeignColumns", "ExpressionDependencies", "BodyDependencies", "TargetColumn"])
        .SelectMany(name => Targets(e, name));

    /// <summary>The table an object is on: a key's, a check's or a default's Host, an index's IndexedObject, a statistic's
    /// OnObject.</summary>
    private static IEnumerable<ElementKey?> Hosts(Element e) => e.Relationships
        .Where(r => r.Name is "Host" or "IndexedObject" or "OnObject").SelectMany(r => r.Targets).Select(t => (ElementKey?)t.Key);

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
    internal static Result<Precondition>? Rule(Element e)
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
