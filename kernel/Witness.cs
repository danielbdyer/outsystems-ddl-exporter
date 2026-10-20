using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// The existing-data tier's plan (V3_MILESTONES.md WP 3.8), ported from the Facsimile's planter
/// (archive/v2/ssdt-agent/facsimile/cases/20-planter.md): the rows WP 3.5 plants into a synthetic copy, table by table in
/// <see cref="Order"/>'s order, each row with a typed value per column, so that the copy answers every precondition as an environment's
/// existing data does, and holds nothing more. A table the environment answered populated takes one valid row; a precondition answered
/// violating takes one row that breaks it for each value or collation class the environment holds, a duplicate taking two; and a table
/// takes the largest of its needs, since a violating row is valid in every other column, serves as presence and shares its row with
/// the demands on other columns (Witness.Demands.cs). A row's NOT NULL foreign keys are met by a planted ancestor or by a row the copy
/// already holds (Witness.Rows.cs), and a foreign key the change validates links each child to a planted or seeded parent, so the copy
/// holds no orphan the environment lacks; a nullable column gets a value, so a populated but clean table stays clean. Literals are
/// ordinal (Witness.Literals.cs): a function of the column's type and width, the row's ordinal and its class, with no generator, no
/// clock and no seed, so two plans from the same inputs are equal and a schema edit changes only the touched tables' rows (VALUES.md
/// D6); none comes from an environment, whose existing data holds booleans. Without existing data each table takes
/// <see cref="PresenceRows"/> rows, for a developer's daily copy. What only a person can write is a <see cref="NeedsRow"/>, the
/// Facsimile's planter-needs-row, every one the plan can know of named in one pass. A check a planted literal makes false is not one:
/// the planter parses no predicate (PLT-44), so WP 3.5 names it, by SQL Server's Msg 547 at the INSERT for a check of the copy, and at
/// acceptance for a check the change adds (PLT-96).
/// </summary>
public sealed partial record Witness(SortedArray<Witness.Planting> Tables, SortedArray<Witness.NeedsRow> Needs)
{
    /// <summary>The rows each table takes when no existing data is given (the Facsimile's up.rowsPerTable).</summary>
    public const int PresenceRows = 3;

    /// <summary>
    /// The plan for a copy whose schema is <paramref name="schema"/>, for the change whose preconditions are given, at
    /// <paramref name="existing"/>, or with presence rows alone when it is null. A precondition names its objects as the change's model
    /// after it does; <paramref name="change"/>, the change from the copy's model, places each where the copy holds it by the coordinate
    /// rules of WP 2.1 (<see cref="Coordinates"/>): an object it renames or moves under the key it had, and one it creates nowhere, since
    /// a table the change creates holds no row and a column it creates holds its default. <paramref name="declared"/> is what the
    /// change's declared witnesses plant. A precondition on an object the copy does not hold and the change does not create is the error
    /// plan.missing-object, and one the existing data holds no state for, plan.uncounted, since neither is ever read as empty. Existing
    /// data that no rows hold is plan.contradictory-answers, the Facsimile's answers-invalid (PLT-60): a ForeignKey answered clean over a
    /// parent that holds no row, answered empty or created by the change, on a populated table where no column of the key admits NULL, or
    /// where the row of a class holds a value in every column of the key.
    /// </summary>
    public static Result<Witness> Plan(Schema schema, SortedArray<Precondition> preconditions, ExistingData? existing,
        Change? change = null, Declared? declared = null)
    {
        var created = new HashSet<ElementKey>(change?.Created.Select(e => e.Key) ?? []);
        var located = new List<Located>();
        foreach (var precondition in preconditions)
        {
            if (Locate(precondition, schema, change, created).Failed(out var here, out var error))
            {
                return error;
            }

            var state = existing?.State(precondition);
            if (existing is not null && state is null)
            {
                return new Error("plan.uncounted",
                    "The existing data holds no state for " + precondition + ", a precondition of the change; a precondition never counted"
                        + " is never planted as empty.",
                    "Predict again, so that every precondition of the change is counted in the environment.");
            }

            if (here is not null)
            {
                located.Add(here with { State = state });
            }
        }

        return new Planner(schema, located, existing is null, declared ?? Declared.None).Plan();
    }

    /// <summary>The literal the planter writes into a column of <paramref name="type"/> at <paramref name="ordinal"/>, as the PLT cases of
    /// the Facsimile's catalog fix it; null for a type it writes none for (Witness.Literals.cs).</summary>
    public static Planted? Literal(ColumnType type, long ordinal) => Literals.Of(type, ordinal);

    /// <summary>The plan as text, culture-free: each table at its place, each row with what it witnesses and its values, then each
    /// need.</summary>
    public override string ToString() => string.Join("\n", Tables.SelectMany(t =>
            t.Rows.Select(r => "  row " + Number(r.Ordinal) + " (" + string.Join("; ", r.Serves) + "): "
                + string.Join(", ", r.Cells.Select(c => c.Column + " " + c.Value))
                + (r.Suspends.Count == 0 ? "" : "; suspends " + string.Join(", ", r.Suspends)))
            .Prepend(t.Table + " at " + Number(t.Position)))
        .Concat(Needs.Select(n => "needs a declared row of " + n.Table + ": " + n.Message)));

    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>A table's rows, at its place in the order, every parent before its children; tables sort by place.</summary>
    public sealed record Planting(int Position, Name Table, SortedArray<Row> Rows) : IComparable<Planting>
    {
        public int CompareTo(Planting? other) =>
            other is null ? 1 : Position.CompareTo(other.Position) is var p and not 0 ? p : Table.CompareTo(other.Table);
    }

    /// <summary>
    /// A row to plant: its ordinal in its table from 1, which its literals are a function of; what it witnesses; a value for each
    /// column, in column order; and the foreign keys of the copy it breaks, which WP 3.5 suspends to plant it and restores to the
    /// environment's trust state (PLT-59). WP 3.5 counts the copy before each row and skips one whose every purpose the copy already
    /// answers, as a seeded row answers a lookup's presence (PLT-97), so an ancestor reuses a seeded row before one is planted.
    /// </summary>
    public sealed record Row(int Ordinal, SortedArray<Purpose> Serves, SortedArray<Cell> Cells, SortedArray<ElementKey> Suspends)
        : IComparable<Row>
    {
        public int CompareTo(Row? other) => other is null ? 1 : Ordinal.CompareTo(other.Ordinal);
    }

    /// <summary>
    /// What a row witnesses: its table's presence, where <see cref="Precondition"/> is null; else rows of a precondition that break it, of
    /// the class a Fits or a Unique names, none for a NotNull's NULL or a ForeignKey's orphan. Presence sorts first.
    /// </summary>
    public sealed record Purpose(Precondition? Precondition, RowClasses Class) : IComparable<Purpose>
    {
        public int CompareTo(Purpose? other) => other is null ? 1
            : (Precondition, other.Precondition) switch
            {
                (null, null) => ((int)Class).CompareTo((int)other.Class),
                (null, _) => -1,
                (_, null) => 1,
                var (mine, theirs) => mine.CompareTo(theirs) is var c and not 0 ? c : ((int)Class).CompareTo((int)other.Class),
            };

        public override string ToString() =>
            Precondition is null ? "presence" : Precondition + (Class == RowClasses.None ? "" : " " + Class.ToString());
    }

    /// <summary>A column's value in a row: the column's ordinal and its one-part name, and the value. Cells sort by column.</summary>
    public sealed record Cell(int Ordinal, Name Column, Planted Value) : IComparable<Cell>
    {
        public int CompareTo(Cell? other) =>
            other is null ? 1 : Ordinal.CompareTo(other.Ordinal) is var c and not 0 ? c : Column.CompareTo(other.Column);
    }

    /// <summary>
    /// A cell's typed value, as WP 3.5 writes it into a ScriptDom INSERT: none, for a column SQL Server fills, which no INSERT names;
    /// NULL; a number, a string or bytes, as T-SQL writes the literal; or a value WP 3.5 reads from the copy as it plants, which keeps
    /// the plan a function of its inputs (the Facsimile's D3, with keys resolved from the copy inside D1): a parent's key, and a key or
    /// an orphan beyond the largest value a column holds. The cases are closed.
    /// </summary>
    public abstract record Planted
    {
        private Planted()
        {
        }

        public T Match<T>(Func<T> omitted, Func<T> none, Func<Number, T> number, Func<Text, T> text, Func<Bytes, T> bytes,
            Func<Linked, T> linked, Func<AfterMax, T> afterMax) => this switch
            {
                Omitted => omitted(),
                Null => none(),
                Number n => number(n),
                Text t => text(t),
                Bytes b => bytes(b),
                Linked l => linked(l),
                AfterMax m => afterMax(m),
                _ => throw new UnreachableException(),
            };

        public sealed override string ToString() => Match(() => "omitted", () => "NULL", n => n.Literal,
            t => (t.National ? "N'" : "'") + t.Content.Replace("'", "''", StringComparison.Ordinal) + "'", b => "0x" + b.Hex,
            l => "the " + l.Column + " of row " + Number(l.Row) + " of " + l.Parent + (l.FirstPass is { } first ? ", " + first
                + " in the first pass" : ""),
            m => "the largest " + m.Column + " of " + m.Table + " plus " + Number(m.Step));

        /// <summary>A column SQL Server fills: an identity, a computed or a GENERATED ALWAYS column, a rowversion, a default drawing from a
        /// sequence (PLT-33), or a nullable xml column no precondition reads, which may be a column set (PLT-36).</summary>
        public sealed record Omitted : Planted;

        public sealed record Null : Planted;

        /// <summary>An integer, decimal, money, float or bit literal, as T-SQL writes it: 1, 1.00, 1.0000, 1E0.</summary>
        public sealed record Number(string Literal) : Planted;

        /// <summary>A string literal, national (N'…') for a Unicode type: a string, and the form SQL Server converts a date and time,
        /// a uniqueidentifier, an xml, a hierarchyid or a spatial value from.</summary>
        public sealed record Text(string Content, bool National) : Planted;

        /// <summary>A binary literal: its bytes as hexadecimal digits, without 0x.</summary>
        public sealed record Bytes(string Hex) : Planted;

        /// <summary>
        /// A parent's key: <see cref="Column"/> of the parent's row at <see cref="Row"/>, from 1 and cycling, in the order of the columns
        /// the reference names, among the rows the parent holds when the child is planted, seeded or planted; every leg of one reference
        /// names one parent row. <see cref="FirstPass"/> is what a first pass writes where the reference is deferred inside a cycle
        /// (Order.Deferred), the key being set by a second; null where the first pass writes the key.
        /// </summary>
        public sealed record Linked(Name Parent, Name Column, int Row, Planted? FirstPass) : Planted;

        /// <summary>The largest value <see cref="Column"/> of <see cref="Table"/> holds in the copy as restored, before any row is planted,
        /// 0 when it holds none, plus <see cref="Step"/>: a key beyond the seeded rows (PLT-41), or an orphan beyond every parent
        /// (PLT-75).</summary>
        public sealed record AfterMax(Name Table, Name Column, long Step) : Planted;
    }

    /// <summary>
    /// What the change's declared witnesses plant, as WP 3.5 reads their blocks: the tables whose rows they plant whole, for which the plan
    /// plants none and names no need; and the preconditions a declared row witnesses, for which the plan plants no row that breaks it and
    /// names no need.
    /// </summary>
    public sealed record Declared(SortedArray<Name> Plants, SortedArray<Precondition> Witnesses)
    {
        public static readonly Declared None = new([], []);
    }

    /// <summary>
    /// A table the planter writes no row for, and why (the Facsimile's planter-needs-row), which a person answers by declaring the rows in
    /// the change's declared witnesses (WP 3.5). A check the environment answered violating is answered by one declared row that makes it
    /// false, and the plan keeps the table's other rows; any other reason makes the declared witnesses plant the table whole
    /// (<see cref="Whole"/>). Its exit, 8 needs-input, joins cli/exits.frozen with the verb that first raises it (the operator's ruling of
    /// 2026-10-19). Needs sort by table, then by message.
    /// </summary>
    public sealed record NeedsRow(Name Table, NeedsRow.Reason Why) : IComparable<NeedsRow>
    {
        /// <summary>Whether the declared witnesses plant the table whole; false for a check that a declared row makes false.</summary>
        public bool Whole => Why is not Reason.FalseCheck;

        public string Message => Why.Match(
            c => Table + " holds rows in the environment that make the check " + (c.Check.Kind as PreconditionKind.Check)?.Expression
                + " false, and the planter writes no literal to make a check false, since a check's expression is free-form",
            r => Table + " rejects the planted literals: SQL Server refused the planted row by the check "
                + (r.Check.Constraint?.ToString() ?? r.Check.Expression.Content),
            c => c.Cycle.Error.Message.TrimEnd('.'),
            k => Table + "." + k.Column + " is a key with room for " + (k.Room == 1 ? "one planted value" : Number(k.Room) + " planted"
                + " values") + ", fewer than the rows the plan plants",
            l => Table + "." + l.Column + " is " + (l.Reader is { } reader ? "read by " + reader : "NOT NULL") + " and of the type "
                + Literals.Written(l.Type) + ", which the planter writes no literal for",
            v => "The planter writes no value " + (v.Class == RowClasses.None ? "that breaks the precondition" : "of the class " + v.Class)
                + " into " + Table + "." + v.Column
                + (v.Parent is { } parent ? " that is also a key of " + parent + ", the table the column references" : ""),
            c => "No ordinal value of " + Table + "." + c.Column + " (" + Literals.Written(c.Before) + ") converts to "
                + Literals.Written(c.After) + " and fits both",
            h => Table + " is the history table of " + h.Current + ", whose rows SQL Server writes only from updates of "
                + h.Current,
            r => Table + "." + r.Column + " is a key" + (r.Value is Planted.Null ? " without a filter, which holds one NULL at most," : ",")
                + " and the plan plants " + r.Value + " in it in " + Number(r.Rows) + " rows") + ".";

        public string Remedy => Why is Reason.FalseCheck c
            ? "Declare one row of " + Table + " that makes the check false, as a witness of " + c.Check + "."
            : Why is Reason.OnCycle cycle ? cycle.Cycle.Error.Remedy
            : "Declare the rows of " + Table + " in the change's declared witnesses, which then plant the table whole.";

        public int CompareTo(NeedsRow? other) =>
            other is null ? 1 : Table.CompareTo(other.Table) is var t and not 0 ? t : string.CompareOrdinal(Message, other.Message);

        /// <summary>Why only a person can write the rows. The cases are closed.</summary>
        public abstract record Reason
        {
            private Reason()
            {
            }

            public T Match<T>(Func<FalseCheck, T> falseCheck, Func<Rejects, T> rejects, Func<OnCycle, T> onCycle,
                Func<NarrowKey, T> narrowKey, Func<NoLiteral, T> noLiteral, Func<NoClassValue, T> noClassValue,
                Func<NoCleanValue, T> noCleanValue, Func<History, T> history, Func<Repeated, T> repeated) => this switch
                {
                    FalseCheck c => falseCheck(c),
                    Rejects r => rejects(r),
                    OnCycle c => onCycle(c),
                    NarrowKey k => narrowKey(k),
                    NoLiteral l => noLiteral(l),
                    NoClassValue v => noClassValue(v),
                    NoCleanValue c => noCleanValue(c),
                    History h => history(h),
                    Repeated r => repeated(r),
                    _ => throw new UnreachableException(),
                };

            /// <summary>A Check precondition the environment answered violating (PLT-78).</summary>
            public sealed record FalseCheck(Precondition Check) : Reason;

            /// <summary>A check of the copy that refused a planted row, SQL Server's Msg 547 at the INSERT, which WP 3.5 names when it
            /// meets one (PLT-44): the planter reads no check's expression.</summary>
            public sealed record Rejects(Check Check) : Reason;

            /// <summary>The table is on a cycle of NOT NULL foreign keys, which no order of INSERTs satisfies (PLT-57, PLT-58).</summary>
            public sealed record OnCycle(Order.Cycle Cycle) : Reason;

            /// <summary>A one-column key whose column's planted values have room for fewer distinct values than the rows, as its type or
            /// the type the change retypes it to holds them (PLT-42, PLT-114).</summary>
            public sealed record NarrowKey(Name Column, long Room) : Reason;

            /// <summary>A column of a type the planter writes no literal for: NOT NULL (PLT-21, PLT-28), or nullable and read by
            /// <see cref="Reader"/>, the first precondition that reads it, whose answer a NULL could change (PLT-22).</summary>
            public sealed record NoLiteral(Name Column, ColumnType Type, Precondition? Reader = null) : Reason;

            /// <summary>A class the environment holds whose witness value the column cannot hold: one its type cannot hold, such as an
            /// accent on a code page's string (PLT-71), or any value of a class in a column of a foreign key the row meets, which must
            /// hold a key of <see cref="Parent"/>, a value WP 3.5 reads from the copy as it plants the row.</summary>
            public sealed record NoClassValue(Name Column, RowClasses Class, Name? Parent = null) : Reason;

            /// <summary>A column the change retypes where no ordinal value converts cleanly and fits both types (PLT-95).</summary>
            public sealed record NoCleanValue(Name Column, ColumnType Before, ColumnType After) : Reason;

            /// <summary>A history table the environment answered populated (PLT-35): SQL Server refuses an INSERT into one.</summary>
            public sealed record History(Name Current) : Reason;

            /// <summary>A one-column key in which the plan plants one value in <see cref="Rows"/> rows that no type's room explains: NULL,
            /// which a key without a filter holds once at most, SQL Server refusing a second as a duplicate key (Msg 2627 for a constraint,
            /// 2601 for an index), or the one orphan literal each child row of a parent the environment answered empty takes.</summary>
            public sealed record Repeated(Name Column, Planted Value, int Rows) : Reason;
        }
    }

    /// <summary>
    /// A precondition placed in the copy: as the change names it, which its state and every purpose keep; as the copy holds it, with each
    /// name the copy holds and every column the change creates left out (<see cref="Here"/>); its state; and the copy's table, and a
    /// ForeignKey's parent, null where the change creates it.
    /// </summary>
    private sealed record Located(Precondition Precondition, Precondition Here, Table Table, Table? Parent)
    {
        public PreconditionState? State { get; init; }
    }

    /// <summary>The precondition placed in the copy, or null where the change creates its table; plan.missing-object for an object the
    /// copy lacks and the change does not create.</summary>
    private static Result<Located?> Locate(Precondition p, Schema schema, Change? change, HashSet<ElementKey> created)
    {
        ElementKey Before(ElementKey after) => change?.KeyBefore(after) ?? after;
        var tableKey = Key(null, "Table", p.Table);
        if (created.Contains(tableKey))
        {
            return (Located?)null;
        }

        if (schema.Find(Before(tableKey).Name) is not { } table)
        {
            return Missing(Before(tableKey), p);
        }

        // A column as the copy holds it, none for one the change creates; the first object the copy lacks, for the error.
        ElementKey? lacking = null;
        Name? Place(Table? on, ElementKey owner, Name name)
        {
            var key = Key(owner, "Column", name);
            if (on is null || created.Contains(key))
            {
                return on is null ? name : null;
            }

            var was = Before(key);
            lacking ??= on.Find(was.Name) is null ? was : null;
            return was.Name;
        }

        SortedArray<Name> Places(IEnumerable<Name> names) => SortedArray.Of(names.Select(n => Place(table, tableKey, n)).OfType<Name>());
        Table? parent = null;
        var kind = p.Kind;
        if (p.Kind is PreconditionKind.ForeignKey foreign)
        {
            var parentKey = Key(null, "Table", foreign.Parent);
            parent = created.Contains(parentKey) ? null : schema.Find(Before(parentKey).Name);
            lacking ??= parent is null && !created.Contains(parentKey) ? Before(parentKey) : null;
            kind = foreign with
            {
                Parent = parent?.Name ?? foreign.Parent,
                Pairs = SortedArray.Of(foreign.Pairs.Select(pair => (Child: Place(table, tableKey, pair.Column),
                        Referenced: Place(parent, parentKey, pair.Referenced)))
                    .Where(pair => pair is (not null, not null))
                    .Select(pair => new PreconditionKind.ForeignKey.Pair(pair.Child!.Value, pair.Referenced!.Value))),
            };
        }
        else if (p.Kind is PreconditionKind.Unique unique)
        {
            kind = unique with { FilterColumns = Places(unique.FilterColumns) };
        }

        var here = new Precondition(table.Name, Places(p.Columns), kind);
        if (lacking is not null)
        {
            return Missing(lacking, p);
        }

        return new Located(p, here, table, parent);
    }

    private static ElementKey Key(ElementKey? parent, string type, Name name) =>
        Known(parent is null ? ElementKey.Of(type, name) : ElementKey.Of(parent, type, name));

    /// <summary>The value of a result no input can fail: a key of a type and a valid name, a name of a fixed text.</summary>
    private static T Known<T>(Result<T> result) => result.Match(value => value, error => throw new UnreachableException(error.Message));

    private static Error Missing(ElementKey key, Precondition p) => new("plan.missing-object",
        "The copy holds no " + key + ", which " + p + " reads and the change does not create; a missing object is never planted as"
            + " empty.",
        "Build the copy at the model the change was planned from, or plan the change from the copy's model.");
}
