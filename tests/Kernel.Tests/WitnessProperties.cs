using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Witness.Plan's laws as properties (V3_MILESTONES.md WP 3.8; VALUES.md D6, P1, P2 and P3; law 11′ in its plan's half): over the generated
/// schemas of Schema.Of's properties (SchemaProperties), each with preconditions drawn over its tables and columns (TableEmpty, NotNull on
/// a nullable column, Fits into a type of eleven, Unique on a column, a check, a foreign key to any table's Id) and a state drawn for each:
/// populated or answered empty, violating or clean, and the classes of a Fits or a Unique. The Facsimile's laws D1 (deterministic) and D3
/// (S-stable planting) are archive/v2/ssdt-agent/facsimile/INVARIANTS.md's.
/// </summary>
public sealed class WitnessProperties
{
    /// <summary>A plan's inputs: the copy's schema, the change's preconditions over it, and the environment's state of each.</summary>
    internal sealed record Inputs(Schema Schema, SortedArray<Precondition> Preconditions, SortedArray<PreconditionState> States)
    {
        public Witness Plan(bool presence = false) => Ok(Witness.Plan(Schema, Preconditions, presence ? null : new ExistingData(States)));

        public Witness Plan(ExistingData existing) => Ok(Witness.Plan(Schema, Preconditions, existing));

        /// <summary>The inputs and their plan as text, which CsCheck prints for a failing sample.</summary>
        public override string ToString() => string.Join("\n", Schema.Tables.Select(t => t.Name + ": "
                + string.Join(", ", t.Columns.Select(c => c.Match(
                    s => s.Name + " " + s.Type.Type + "(" + s.Type.Length + (s.Type.IsMax ? " max" : "") + "," + s.Type.Precision + ","
                        + s.Type.Scale + ")" + (s.Nullable ? " NULL" : "") + (s.Identity is null ? "" : " IDENTITY")
                        + (s.Default?.Sequence is null ? "" : " SEQUENCE") + (s.Sparse ? " SPARSE" : ""),
                    computed => computed.Name + " AS", generated => generated.Name + " " + generated.As)))
                + (t.Key is { } key ? "; key " + string.Join(", ", key.Columns.Select(k => k.Column)) : "")
                + string.Concat(t.Uniques.Select(u => "; unique " + string.Join(", ", u.Columns.Select(k => k.Column))
                    + (u.Disabled ? " disabled" : "")))
                + string.Concat(t.References.Select(r => "; " + r.Key + " " + string.Join(", ", r.Legs.Select(l => l.Column + "→"
                    + l.ParentColumn)) + " to " + r.Parent + (r.Optional ? " optional" : "") + (r.Disabled ? " disabled" : "")))
                + (t.Versioning is { } v ? "; " + v : ""))
            .Concat(States.Select(s => s.Precondition + (s.Populated ? " populated" : " empty") + (s.Violating ? " violating" : "")
                + (s.Classes == RowClasses.None ? "" : " " + s.Classes)))
            .Append("plan:").Append(Plan().ToString()).Append("presence plan:").Append(Plan(presence: true).ToString()));
    }

    private static readonly ColumnType[] Targets = [.. new[]
    {
        "int", "bigint", "smallint", "nvarchar(10)", "varchar(5)", "decimal(5,2)", "date", "datetime", "bit", "uniqueidentifier",
        "nvarchar(max)",
    }.Select(Model.Type)];

    internal static readonly Gen<Inputs> Plans = Gen.Select(SchemaProperties.Schemas, Gen.Int[0, 999].Array[96]).Select(Build);

    /// <summary>D1: the plan reads its inputs and nothing else, so inputs built again from their parts given in the opposite order plan
    /// alike, to the same text, with existing data and without.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D6")]
    [Trait("Law", "11′ a planted set repeats itself, replants only the touched tables and holds no orphan its existing data lacks")]
    public void Two_plans_from_the_same_inputs_are_identical() =>
        Plans.Sample(i =>
        {
            var again = new Inputs(new Schema(SortedArray.Of(i.Schema.Tables.Reverse().Select(t => t with
            {
                Columns = SortedArray.Of(t.Columns.Reverse()), Uniques = SortedArray.Of(t.Uniques.Reverse()),
                References = SortedArray.Of(t.References.Reverse()), Checks = SortedArray.Of(t.Checks.Reverse()),
            }))), SortedArray.Of(i.Preconditions.Reverse()), SortedArray.Of(i.States.Reverse()));
            var (plan, other) = (i.Plan(), again.Plan());
            return plan == other && plan.ToString() == other.ToString() && i.Plan(presence: true) == again.Plan(presence: true);
        });

    /// <summary>
    /// D3: a column added to a table, or a column no key, reference or precondition reads dropped or retyped, changes no row of any other
    /// table and, in its own table, no value of any other column: a literal is keyed by its column's name and its row's ordinal, never by
    /// a column's place or a stream shared across columns.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D6")]
    [Trait("Law", "11′ a planted set repeats itself, replants only the touched tables and holds no orphan its existing data lacks")]
    public void A_schema_edit_changes_only_the_rows_of_the_tables_it_touches() =>
        Gen.Select(Plans, Gen.Int[0, 999], Gen.Int[0, 2], Gen.Int[0, Targets.Length - 1], Gen.Bool)
            .Sample((i, pick, kind, type, nullable) =>
            {
                var table = i.Schema.Tables[pick % i.Schema.Tables.Count];
                var plain = Plain(i, table);
                var columns = table.Columns.ToList();
                var column = Ok(Name.Of("zz"));
                if (kind == 0 || plain.Length == 0)
                {
                    columns.Add(new Column.Stored(columns.Count + 1, column, nullable, Targets[type], null, null, null, false, false));
                }
                else
                {
                    var at = columns.FindIndex(c => c.Name == plain[pick % plain.Length].Name);
                    column = columns[at].Name;
                    columns[at] = ((Column.Stored)columns[at]) with { Type = Targets[type] };
                    if (kind == 1)
                    {
                        columns.RemoveAt(at);
                    }
                }

                var edited = i with
                {
                    Schema = new Schema(SortedArray.Of(i.Schema.Tables.Select(t => t.Name != table.Name ? t
                        : t with { Columns = SortedArray.Of(columns.Select((c, n) => c with { Ordinal = n + 1 })) }))),
                };
                return Stable(i.Plan(), edited.Plan(), table.Name, column)
                    && Stable(i.Plan(presence: true), edited.Plan(presence: true), table.Name, column);
            });

    /// <summary>
    /// The copy holds no orphan its existing data lacks: each leg of a NOT NULL reference of the copy, and of a foreign key the change
    /// validates, takes its parent's key in every planted row, but a row that is the orphan an environment's violating ForeignKey holds,
    /// a row whose parent the environment answered empty, which suspends the reference, a NULL in a nullable leg, and a leg another of
    /// the row's purposes sets.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Law", "11′ a planted set repeats itself, replants only the touched tables and holds no orphan its existing data lacks")]
    public void A_planted_row_links_each_reference_it_meets_to_a_parent_or_holds_an_orphan_its_existing_data_holds() =>
        Plans.Sample(i =>
        {
            var plan = i.Plan();
            return plan.Tables.All(p => p.Rows.All(row => Meets(i, i.Schema.Find(p.Table)!, row)));
        });

    /// <summary>
    /// A populated but clean table stays clean, and a violating one breaks its rule: a row answers each class an environment holds, or the
    /// plan names a need for its table; a NotNull answered clean finds no NULL in a row no other purpose of which sets the column; and a
    /// Unique answered clean finds no two rows of equal literals over its key.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_state_answered_violating_plants_a_row_of_each_class_and_one_answered_clean_plants_none() =>
        Plans.Sample(i =>
        {
            var plan = i.Plan();
            var rows = plan.Tables.ToDictionary(t => t.Table, t => t.Rows);
            return i.States.All(s =>
            {
                var table = s.Precondition.Table;
                var held = rows.TryGetValue(table, out var planted) ? planted : [];
                var needed = plan.Needs.Any(n => n.Table == table);
                return s.Precondition.Kind switch
                {
                    PreconditionKind.Check => !s.Violating || plan.Needs.Any(n => n.Why is Witness.NeedsRow.Reason.FalseCheck f
                        && f.Check == s.Precondition),
                    PreconditionKind.TableEmpty => !s.Populated || held.Count > 0 || needed,
                    _ when Asked(s).Any() => needed || Asked(s).All(c => held.Any(r => r.Serves.Contains(new Witness.Purpose(s.Precondition,
                        c)))),
                    PreconditionKind.NotNull => held.Where(r => !Others(r, s)).All(r => Value(r, s.Precondition.Columns[0]) is not
                        Witness.Planted.Null),
                    PreconditionKind.Unique => s.Violating || Distinct(held.Where(r => !Others(r, s)), s.Precondition.Columns),
                    _ => true,
                };
            });
        });

    /// <summary>P2: the plan reads states, never the counts they come from, so two sets of counts that give the same states plant the
    /// same rows.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void Counts_that_give_the_same_states_plant_the_same_rows() =>
        Gen.Select(Plans, Gen.Long[2, long.MaxValue / 2]).Sample((i, many) =>
        {
            ExistingData Counted(long n) => ExistingData.Of(i.States.Select(s =>
                new PreconditionCount(s.Precondition, s.Populated ? n : 0, s.Violating ? n : 0)));
            return i.Plan(Counted(1)) == i.Plan(Counted(many));
        });

    /// <summary>
    /// P1 and P3: a planted literal comes from the copy's schema, the row's ordinal and what the row witnesses alone, and no environment's
    /// value reaches it: two environments' existing data plant the same literal into a row that witnesses the same in both, and no literal
    /// carries a count an environment gave.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    [Trait("Value", "P3")]
    public void A_planted_literal_comes_from_no_environment() =>
        Gen.Select(Plans, Gen.Int[0, 999].Array[96]).Sample((i, numbers) =>
        {
            var other = i with { States = States(new Draw(numbers), i.Preconditions) };
            var (plan, theirs) = (i.Plan(), other.Plan());
            var rows = theirs.Tables.SelectMany(t => t.Rows.Select(r => (t.Table, Row: r))).ToLookup(x => (x.Table, x.Row.Ordinal));
            const long counted = 739_517_284_613;
            var counts = i.Plan(ExistingData.Of(i.States.Select(s =>
                new PreconditionCount(s.Precondition, s.Populated ? counted : 0, s.Violating ? counted : 0))));
            return plan.Tables.SelectMany(t => t.Rows.Select(r => (t.Table, Row: r))).All(mine => rows[(mine.Table, mine.Row.Ordinal)]
                    .Where(their => their.Row.Serves == mine.Row.Serves && their.Row.Suspends == mine.Row.Suspends)
                    .All(their => mine.Row.Cells.Zip(their.Row.Cells).All(c => !Literal(c.First.Value) || !Literal(c.Second.Value)
                        || c.First == c.Second)))
                && !counts.ToString().Contains(counted.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        });

    /// <summary>Without existing data every table but a history table takes three presence rows, or names why only a person can write
    /// its rows.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Without_existing_data_each_table_takes_three_rows_or_names_a_need() =>
        Plans.Sample(i =>
        {
            var plan = i.Plan(presence: true);
            return i.Schema.Tables.Where(t => t.Versioning is not Versioning.History).All(t =>
                plan.Tables.Any(p => p.Table == t.Name && p.Rows.Select(r => r.Ordinal).SequenceEqual([1, 2, 3]))
                || plan.Needs.Any(n => n.Table == t.Name));
        });

    /// <summary>Whether two plans agree but on one column of one table: the same needs, the same tables and rows, and in the touched table
    /// the same rows with the same values in every other column.</summary>
    private static bool Stable(Witness before, Witness after, Name table, Name column)
    {
        static IEnumerable<(Name, Witness.Planted)> Kept(Witness.Row r, Name column) =>
            r.Cells.Where(c => c.Column != column).Select(c => (c.Column, c.Value)).OrderBy(c => c.Column);
        var kept = after.Tables.ToDictionary(t => t.Table);
        return before.Needs == after.Needs && before.Tables.Count == after.Tables.Count && before.Tables.All(t =>
            kept.TryGetValue(t.Table, out var k) && (t.Table != table ? t.Rows == k.Rows
                : t.Rows.Count == k.Rows.Count && t.Rows.Zip(k.Rows).All(r => r.First.Serves == r.Second.Serves
                    && r.First.Suspends == r.Second.Suspends && Kept(r.First, column).SequenceEqual(Kept(r.Second, column)))));
    }

    /// <summary>Whether each leg of a reference the row meets takes the key of a parent the leg references, or the row is one of the
    /// exceptions the property names: a NULL in a nullable leg, a leg another purpose of the row sets, or a leg one of whose parents the
    /// environment answered empty. A column in two foreign keys to two parents takes one parent's key.</summary>
    private static bool Meets(Inputs i, Table table, Witness.Row row)
    {
        bool Empty(Name parent) => i.States.Where(s => s.Precondition.Table == parent).ToArray() is { Length: > 0 } states
            && states.All(s => !s.Populated);
        var validated = i.States.Where(s => s.Precondition.Table == table.Name).Select(s => s.Precondition.Kind)
            .OfType<PreconditionKind.ForeignKey>().SelectMany(f => f.Pairs.Select(p => (f.Parent, p.Column, Referenced: p.Referenced)));
        var mandatory = table.References.Where(r => !r.Optional).SelectMany(r => r.Legs.Select(l => (r.Parent, l.Column,
            Referenced: l.ParentColumn)));
        var legs = mandatory.Concat(validated).ToLookup(l => l.Column);
        return legs.All(leg => leg.Any(l => Empty(l.Parent))
            || leg.Any(l => Value(row, leg.Key) is Witness.Planted.Linked linked && linked.Parent == l.Parent
                && linked.Column == l.Referenced)
            || Value(row, leg.Key) is Witness.Planted.Null
            || row.Serves.Any(p => p.Precondition is { } other && other.Columns.Contains(leg.Key)));
    }

    /// <summary>The classes a violating or class-holding state asks a row of, in the order of the flags; none for a clean NotNull,
    /// ForeignKey or Unique.</summary>
    private static IEnumerable<RowClasses> Asked(PreconditionState s) => s.Precondition.Kind switch
    {
        PreconditionKind.NotNull or PreconditionKind.ForeignKey when s.Violating => [RowClasses.None],
        PreconditionKind.Fits or PreconditionKind.Unique => Enum.GetValues<RowClasses>().Where(c => c != RowClasses.None
            && s.Classes.HasFlag(c) && (s.Precondition.Kind is PreconditionKind.Fits ? (int)c < 64 : (int)c >= 64)),
        _ => [],
    };

    /// <summary>Whether another purpose of the row reads one of the state's columns, setting it to what that purpose asks.</summary>
    private static bool Others(Witness.Row row, PreconditionState s) => row.Serves.Any(p => p.Precondition is { } other
        && other != s.Precondition && other.Columns.Any(s.Precondition.Columns.Contains));

    private static bool Distinct(IEnumerable<Witness.Row> rows, SortedArray<Name> key)
    {
        var tuples = rows.Select(r => key.Select(c => Value(r, c)).ToArray()).Where(t => t.All(Literal)).ToArray();
        return tuples.Select(t => string.Join("\u0001", t.Select(v => v!.ToString()))).Distinct().Count() == tuples.Length;
    }

    private static Witness.Planted? Value(Witness.Row row, Name column) => row.Cells.FirstOrDefault(c => c.Column == column)?.Value;

    private static bool Literal(Witness.Planted? value) => value is Witness.Planted.Number or Witness.Planted.Text or Witness.Planted.Bytes;

    /// <summary>The columns of a table no key, reference, history or precondition reads: a column of the copy's own, none of whose
    /// values another row or table depends on.</summary>
    private static Column.Stored[] Plain(Inputs i, Table table)
    {
        var read = i.Preconditions.Where(p => p.Table == table.Name).SelectMany(p => p.Columns.Concat(p.Kind switch
        {
            PreconditionKind.Unique u => u.FilterColumns,
            _ => [],
        })).Concat(i.Preconditions.Select(p => p.Kind).OfType<PreconditionKind.ForeignKey>().Where(f => f.Parent == table.Name)
            .SelectMany(f => f.Pairs.Select(p => p.Referenced)))
            .Concat(i.Schema.Tables.SelectMany(t => t.References).Where(r => r.Parent == table.Name).SelectMany(r =>
                r.Legs.Select(l => l.ParentColumn)))
            .Concat(table.References.SelectMany(r => r.Legs.Select(l => l.Column)))
            .Concat(table.Key?.Columns.Select(c => c.Column) ?? [])
            .Concat(table.Uniques.SelectMany(u => u.Columns.Select(c => c.Column)))
            .ToHashSet();
        return [.. table.Columns.OfType<Column.Stored>().Where(c => c.Identity is null && c.Default?.Sequence is null
            && !read.Contains(c.Name) && c.Name != Ok(Name.Of("Id")))];
    }

    private static Inputs Build(Schema schema, int[] numbers)
    {
        var d = new Draw(numbers);
        var preconditions = new List<Precondition>();
        foreach (var table in schema.Tables)
        {
            if (d.OneIn(2))
            {
                preconditions.Add(new Precondition(table.Name, [], new PreconditionKind.TableEmpty()));
            }

            foreach (var c in table.Columns.OfType<Column.Stored>().Where(c => c.Identity is null && c.Default?.Sequence is null))
            {
                PreconditionKind? kind = d.Of(10) switch
                {
                    0 when c.Nullable => new PreconditionKind.NotNull(),
                    1 => new PreconditionKind.Fits(c.Type, d.Pick(Targets)),
                    2 => new PreconditionKind.Unique(null, []),
                    3 => new PreconditionKind.Check("([" + c.Name.Base + "]>(0))"),
                    4 when d.Pick(schema.Tables) is var parent => new PreconditionKind.ForeignKey(parent.Name,
                        [new PreconditionKind.ForeignKey.Pair(c.Name, parent.Columns[0].Name)]),
                    _ => null,
                };
                if (kind is not null)
                {
                    preconditions.Add(new Precondition(table.Name, [c.Name], kind));
                }
            }
        }

        var all = SortedArray.Of(preconditions.Distinct());
        return new Inputs(schema, all, States(d, all));
    }

    /// <summary>A state for each precondition: populated two times in three, violating one time in three of those, a TableEmpty's
    /// violating exactly when populated, and a Fits's or a Unique's classes drawn from its own.</summary>
    private static SortedArray<PreconditionState> States(Draw d, SortedArray<Precondition> preconditions) =>
        SortedArray.Of(preconditions.Select(p =>
        {
            var populated = !d.OneIn(3);
            var violating = p.Kind is PreconditionKind.TableEmpty ? populated : populated && d.OneIn(3);
            var classes = !populated ? RowClasses.None : p.Kind switch
            {
                PreconditionKind.Fits => (RowClasses)(d.Of(64) & d.Of(64)),
                PreconditionKind.Unique => (RowClasses)((d.Of(32) & d.Of(32)) << 6),
                _ => RowClasses.None,
            };
            return new PreconditionState(p, populated, violating) { Classes = classes };
        }));

    /// <summary>Draws in order from a fixed array of generated numbers, so a failing sample shrinks.</summary>
    private sealed class Draw(int[] numbers)
    {
        private int next;

        public int Of(int choices) => numbers[next++ % numbers.Length] % choices;

        public bool OneIn(int n) => Of(n) == 0;

        public T Pick<T>(IReadOnlyList<T> items) => items[Of(items.Count)];
    }
}
