using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

public sealed partial record Witness
{
    /// <summary>
    /// A reference a table's rows meet or leave: its key, its parent and its legs, each a column and the parent's column it references;
    /// whether a row links each leg to a parent (a NOT NULL reference of the copy, one the change validates, PLT-125, or one with a leg
    /// a precondition reads); the copy's reference, null where only a precondition names it (PLT-56); and whether the change validates
    /// it.
    /// </summary>
    private sealed record Link(ElementKey Key, Name Parent, (Name Column, Name ParentColumn)[] Legs, bool Met, Reference? Copy,
        bool Validated);

    /// <summary>One plan's working state: the rows each table takes, the tables whose rows serve presence, and the needs found.</summary>
    private sealed partial class Planner(Schema schema, IReadOnlyList<Located> located, bool presenceMode, Declared declared)
    {
        private readonly Dictionary<Name, int> rows = [];
        private readonly HashSet<Name> presence = [];
        private readonly HashSet<Name> taken = [];
        private readonly List<NeedsRow> needs = [];
        private readonly HashSet<Name> owned = [.. declared.Plants];
        private readonly HashSet<Name> empty = [];
        private readonly Dictionary<Name, Demand[]> demands = [];
        private readonly Dictionary<Name, List<(Demand Demand, int Start)>> placed = [];
        private Dictionary<Name, Link[]> links = [];
        private Order order = new([], [], []);

        public Witness Plan()
        {
            links = schema.Tables.ToDictionary(t => t.Name, Links);
            order = Order.Of(new Schema(SortedArray.Of(schema.Tables.Select(t => t with
            {
                References = SortedArray.Of(t.References.Concat(links[t.Name].Where(l => l.Copy is null).Select(l => Ordering(t, l)))),
            }))));
            var at = located.GroupBy(l => l.Table.Name).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Precondition).ToArray());
            foreach (var table in schema.Tables.Where(t => !owned.Contains(t.Name)))
            {
                var states = at.GetValueOrDefault(table.Name) ?? [];
                if (presenceMode ? table.Versioning is not Versioning.History : states.Any(l => l.State!.Populated))
                {
                    rows[table.Name] = presenceMode ? PresenceRows : 1;
                    presence.Add(table.Name);
                }
                else if (!presenceMode && states.Length > 0)
                {
                    empty.Add(table.Name);
                }

                demands[table.Name] = [.. states.SelectMany(Demands)];
                foreach (var need in demands[table.Name].Select(d => d.Need).OfType<NeedsRow.Reason>().Distinct())
                {
                    Need(table.Name, need);
                }

                placed[table.Name] = Lanes([.. demands[table.Name].Where(d => d.Need is null)]);
                var height = placed[table.Name].Select(d => d.Start + d.Demand.Size - 1).DefaultIfEmpty(0).Max();
                rows[table.Name] = Math.Max(rows.GetValueOrDefault(table.Name), height);
            }

            Ancestors();
            foreach (var member in order.Cycles.SelectMany(c => c.Members.Select(m => (Cycle: c, Member: m))))
            {
                if (rows.GetValueOrDefault(member.Member) > 0)
                {
                    Need(member.Member, new NeedsRow.Reason.OnCycle(member.Cycle));
                }
            }

            foreach (var table in schema.Tables.Where(t => rows.GetValueOrDefault(t.Name) > 0))
            {
                if (table.Versioning is Versioning.History history)
                {
                    Need(table.Name, new NeedsRow.Reason.History(history.CurrentTable));
                }
            }

            var plantings = new List<Planting>();
            foreach (var step in order.Tables.Where(s => rows.GetValueOrDefault(s.Table) > 0 && !taken.Contains(s.Table)))
            {
                if (Build(schema.Find(step.Table)!) is { } built)
                {
                    plantings.Add(new Planting(step.Position, step.Table, built));
                }
            }

            return new Witness(SortedArray.Of(plantings), SortedArray.Of(needs.Distinct()));
        }

        /// <summary>
        /// The tables whose rows serve as ancestors: every parent a planted row links to, transitively, takes the rows the link asks,
        /// one, or three without existing data, or one for each child row where the leg is in a key of the child; unless the declared
        /// witnesses plant it, it is a history table, or the environment answered it empty, since the answers win over the ancestor rule
        /// (PLT-59). WP 3.5 skips an ancestor row where the parent already holds one, a seeded lookup's (PLT-50, PLT-53).
        /// </summary>
        private void Ancestors()
        {
            var queue = new Queue<Name>(schema.Tables.Select(t => t.Name).Where(t => rows.GetValueOrDefault(t) > 0));
            while (queue.TryDequeue(out var table))
            {
                foreach (var link in links[table].Where(l => l.Met && Linking(table, l)))
                {
                    // A leg in a key of the child takes a parent row of its own for each child row, so the keys stay distinct.
                    var asked = Math.Max(presenceMode ? PresenceRows : 1,
                        link.Legs.Any(g => Keyed(schema.Find(table)!, g.Column)) ? rows[table] : 1);
                    var parent = link.Parent;
                    if (parent != table && links.ContainsKey(parent) && !owned.Contains(parent) && !empty.Contains(parent)
                        && rows.GetValueOrDefault(parent) < asked && schema.Find(parent)?.Versioning is not Versioning.History)
                    {
                        rows[parent] = asked;
                        presence.Add(parent);
                        queue.Enqueue(parent);
                    }
                }
            }
        }

        /// <summary>Whether a column is in a key of the copy's table, or of a Unique precondition on it.</summary>
        private bool Keyed(Table table, Name column) => Keys(table).Any(k => k.Contains(column))
            || located.Any(l => l.Table.Name == table.Name && l.Here.Kind is PreconditionKind.Unique && l.Here.Columns.Contains(column));

        /// <summary>Whether a row of the table links the reference's legs to a parent: a row no orphan of those legs takes (PLT-75, whose
        /// one row is the orphan and plants no parent).</summary>
        private bool Linking(Name table, Link link) => rows.GetValueOrDefault(table) > (placed.GetValueOrDefault(table) ?? [])
            .Where(d => d.Demand.Purpose.Precondition?.Kind is PreconditionKind.ForeignKey
                && link.Legs.All(g => d.Demand.Columns.Contains(g.Column)))
            .Sum(d => d.Demand.Size);

        /// <summary>
        /// A table's rows: row r takes, in each column, the value of the demand that covers the row and sets the column, else the column's
        /// clean value at r; it serves presence where the table's rows do, and every demand that covers it. A value no literal can write is
        /// a need, and so are a demand's literal in a column of a foreign key the row meets (<see cref="Unmet"/>) and a one-column key
        /// whose planted values repeat where no demand asks it to (PLT-42, PLT-114); each leaves the table's rows to the declared
        /// witnesses, and null is returned.
        /// </summary>
        private SortedArray<Row>? Build(Table table)
        {
            var reasons = new List<NeedsRow.Reason>();
            var built = new List<Row>();
            for (var r = 1; r <= rows[table.Name]; r++)
            {
                var suspends = new List<ElementKey>();
                Planted? CleanAt(Name column, int row) => table.Find(column) is { } c ? Clean(table, c, row, suspends, reasons) : null;
                var covering = placed[table.Name].Where(d => d.Start <= r && r < d.Start + d.Demand.Size).ToArray();
                var cells = new List<Cell>();
                foreach (var column in table.Columns)
                {
                    var by = covering.FirstOrDefault(d => d.Demand.Columns.Contains(column.Name));
                    var value = by.Demand is { } demand ? demand.Value(CleanAt, r, r - by.Start, column.Name) : CleanAt(column.Name, r);
                    var parent = by.Demand is { Purpose.Precondition.Kind: not PreconditionKind.ForeignKey }
                        && value is not (null or Planted.Null or Planted.Linked) ? Unmet(table, column.Name, suspends) : null;
                    if ((value is null || parent is not null) && by.Demand is { } unwritten)
                    {
                        reasons.Add(new NeedsRow.Reason.NoClassValue(column.Name, unwritten.Purpose.Class, parent));
                    }

                    cells.Add(new Cell(column.Ordinal, column.Name, value ?? new Planted.Null()));
                }

                suspends.AddRange(covering.Select(d => d.Demand.Suspends).OfType<ElementKey>());
                var serves = covering.Select(d => d.Demand.Purpose)
                    .Concat(presence.Contains(table.Name) ? [new Purpose(null, RowClasses.None)] : []);
                built.Add(new Row(r, SortedArray.Of(serves.Distinct()), SortedArray.Of(cells), SortedArray.Of(suspends.Distinct())));
            }

            reasons.AddRange(Repeats(table, built, demands[table.Name]));
            if (reasons.Count == 0)
            {
                return SortedArray.Of(built);
            }

            foreach (var reason in reasons.Distinct())
            {
                Need(table.Name, reason);
            }

            return null;
        }

        /// <summary>
        /// The value of a column at row r that breaks nothing (PLT-01 to PLT-52, PLT-94, PLT-95): none for a column SQL Server fills; a
        /// leg's parent key, or NULL where a nullable leg's reference is left; a nullable xml column no precondition reads, left to its
        /// default, since a column set and a typed xml column both read as one (PLT-36, PLT-22); for an integer key of the copy without
        /// identity, a key past the seeded rows (PLT-41); for a column the change retypes, a value of no class; else the ordinal literal,
        /// or NULL in a nullable column of a type the planter writes none for, and a need in a NOT NULL one.
        /// </summary>
        private Planted? Clean(Table table, Column column, int r, List<ElementKey> suspends, List<NeedsRow.Reason> reasons)
        {
            if (column is not Column.Stored stored || stored.Identity is not null || stored.Default?.Sequence is not null
                || Literals.FamilyOf(stored.Type) == Literals.Family.Version)
            {
                return new Planted.Omitted();
            }

            if (Leg(table, stored.Name) is { } leg)
            {
                return Linked(table, stored, leg.Link, leg.ParentColumn, r, suspends, reasons);
            }

            if (Literals.FamilyOf(stored.Type) == Literals.Family.Xml && stored.Nullable && !Read(table, stored.Name))
            {
                return new Planted.Omitted();
            }

            if (Literals.FamilyOf(stored.Type) == Literals.Family.Integer && Keys(table).Any(k => k.Contains(stored.Name)))
            {
                return new Planted.AfterMax(table.Name, stored.Name, r);
            }

            if (located.FirstOrDefault(l => l.Table.Name == table.Name && l.Here.Kind is PreconditionKind.Fits
                && l.Here.Columns.Contains(stored.Name))?.Here.Kind is PreconditionKind.Fits fits)
            {
                var clean = Literals.Clean(stored.Type, fits.After, r);
                if (clean is null)
                {
                    reasons.Add(new NeedsRow.Reason.NoCleanValue(stored.Name, stored.Type, fits.After));
                }

                return clean;
            }

            return Plain(stored, r, reasons);
        }

        /// <summary>A leg's value: the parent's key at row r, written in a second pass where Order defers the reference; in a leg of a
        /// reference no row meets, or of a parent the schema lacks, NULL where it admits one and else the ordinal literal; and where the
        /// environment answered the parent empty, NULL in a nullable leg no precondition reads, and else an orphan, its reference suspended
        /// (PLT-59), since a NULL there would read violating.</summary>
        private Planted? Linked(Table table, Column.Stored leg, Link link, Name parentColumn, int r, List<ElementKey> suspends,
            List<NeedsRow.Reason> reasons)
        {
            if (!link.Met || !links.ContainsKey(link.Parent))
            {
                return leg.Nullable ? new Planted.Null() : Plain(leg, r, reasons);
            }

            if (empty.Contains(link.Parent))
            {
                if (leg.Nullable && !Read(table, leg.Name))
                {
                    return new Planted.Null();
                }

                suspends.AddRange(link.Copy is { Disabled: false } copy ? [copy.Key] : []);
                return Orphan(leg, link.Parent, parentColumn, r);
            }

            var deferred = order.Deferred.Contains(new Order.Deferral(table.Name, link.Key));
            return new Planted.Linked(link.Parent, parentColumn, r,
                deferred ? leg.Nullable ? new Planted.Null() : Plain(leg, r, reasons) : null);
        }

        /// <summary>
        /// The parent of a reference the row meets that a demand's literal in one of its columns leaves: no value of a class is a key of
        /// the parent, so the copy would hold an orphan its existing data lacks. A reference the row meets is an enabled reference of the
        /// copy, or one the change validates; null where the column is a leg of none. Where the environment answered the parent empty,
        /// the environment's rows hold orphans there too, so the literal stands and the copy's reference is suspended (PLT-59).
        /// </summary>
        private Name? Unmet(Table table, Name column, List<ElementKey> suspends)
        {
            foreach (var link in links[table.Name].Where(l => l.Met && links.ContainsKey(l.Parent) && l.Legs.Any(g => g.Column == column)
                && (l.Validated || l.Copy is { Disabled: false })))
            {
                if (!empty.Contains(link.Parent))
                {
                    return link.Parent;
                }

                suspends.AddRange(link.Copy is { Disabled: false } copy ? [copy.Key] : []);
            }

            return null;
        }

        /// <summary>The ordinal literal, NULL in a nullable column of a type the planter writes none for, and a need in a NOT NULL
        /// one.</summary>
        private static Planted? Plain(Column.Stored column, int r, List<NeedsRow.Reason> reasons)
        {
            if (Literals.Of(column.Type, r) is { } literal)
            {
                return literal;
            }

            if (!column.Nullable)
            {
                reasons.Add(new NeedsRow.Reason.NoLiteral(column.Name, column.Type));
            }

            return column.Nullable ? new Planted.Null() : null;
        }

        /// <summary>A value of the leg no parent row holds (PLT-75, PLT-76): past the parent's largest key for a number, the reserved
        /// FFFFFFFF-FFFF-4FFF-BFFF- pattern for a uniqueidentifier, else the literal at ordinal 0, which no planted row holds.</summary>
        private static Planted? Orphan(Column.Stored leg, Name parent, Name parentColumn, int r) =>
            Literals.Numeric(Literals.FamilyOf(leg.Type)) ? new Planted.AfterMax(parent, parentColumn, 1000)
            : Literals.FamilyOf(leg.Type) == Literals.Family.Guid
                ? new Planted.Text("FFFFFFFF-FFFF-4FFF-BFFF-" + r.ToString("X12", System.Globalization.CultureInfo.InvariantCulture), false)
            : Literals.Of(leg.Type, 0) is { } zero && zero != Literals.Of(leg.Type, 1) ? zero
            : null;

        /// <summary>The planted values of a one-column key that repeat where no demand asks for it: a key of the copy, or one the change
        /// adds that the environment answered clean.</summary>
        private IEnumerable<NeedsRow.Reason> Repeats(Table table, List<Row> built, Demand[] demands)
        {
            var asked = demands.Where(d => d.Size == 2).Select(d => d.Columns).ToArray();
            var clean = located.Where(l => l.Table.Name == table.Name && l.Here.Kind is PreconditionKind.Unique
                && l.State is not { Violating: true }).Select(l => l.Here.Columns);
            foreach (var key in Keys(table).Concat(clean).Where(k => k.Count == 1 && !asked.Any(a => a.Contains(k[0]))).Distinct())
            {
                var values = built.Select(r => r.Cells.First(c => c.Column == key[0]).Value)
                    .Where(v => v is Planted.Number or Planted.Text or Planted.Bytes or Planted.Null).ToArray();
                if (values.Distinct().Count() < values.Length && table.Find(key[0]) is Column.Stored column)
                {
                    yield return new NeedsRow.Reason.NarrowKey(key[0], Literals.Room(column.Type));
                }
            }
        }

        /// <summary>The copy's enabled keys of a table, each its columns: the primary key and every unique constraint and index.</summary>
        private static IEnumerable<SortedArray<Name>> Keys(Table table) =>
            (table.Key is { } pk ? [SortedArray.Of(pk.Columns.Select(c => c.Column))] : Array.Empty<SortedArray<Name>>())
            .Concat(table.Uniques.Where(u => !u.Disabled).Select(u => SortedArray.Of(u.Columns.Select(c => c.Column))));

        /// <summary>The link a column is a leg of, and the parent column it references: one the change validates first, then one a row
        /// meets, then any; none for a column of no reference.</summary>
        private (Link Link, Name ParentColumn)? Leg(Table table, Name column) =>
            links[table.Name].OrderBy(l => l.Validated ? 0 : l.Met ? 1 : 2)
                .SelectMany(l => l.Legs.Where(g => g.Column == column).Select(g => ((Link, Name)?)(l, g.ParentColumn)))
                .FirstOrDefault();

        /// <summary>Whether a precondition reads the column: its columns, a foreign key's legs, a filter's columns.</summary>
        private bool Read(Table table, Name column) => located.Any(l => l.Table.Name == table.Name
            && (l.Here.Columns.Contains(column) || l.Here.Kind is PreconditionKind.Unique u && u.FilterColumns.Contains(column)));

        /// <summary>A table's links: each reference of the copy, met where it is NOT NULL, where the change validates it, or where a
        /// precondition reads a leg, whose value a NULL would make violating; and each foreign key a precondition validates that the copy
        /// lacks, keyed under the table by its place among them.</summary>
        private Link[] Links(Table table)
        {
            var validated = located.Where(l => l.Table.Name == table.Name && l.Parent is not null)
                .Select(l => l.Here.Kind).OfType<PreconditionKind.ForeignKey>().Where(f => f.Pairs.Count > 0).Distinct().ToArray();
            var copy = table.References.Select(r => new Link(r.Key, r.Parent, [.. r.Legs.Select(g => (g.Column, g.ParentColumn))],
                !r.Optional || validated.Any(v => Same(r, v)) || r.Legs.Any(g => Read(table, g.Column)), r,
                validated.Any(v => Same(r, v))));
            var added = validated.Where(v => !table.References.Any(r => Same(r, v))).Select((v, i) => new Link(
                Key(Key(null, "Table", table.Name), "ForeignKeyConstraint", Known(Name.Of("link " + i))),
                v.Parent, [.. v.Pairs.Select(p => (p.Column, p.Referenced))], true, null, true));
            return [.. copy.Concat(added)];
        }

        /// <summary>A reference for the order alone, from a link only a precondition names: optional where one of its legs admits
        /// NULL.</summary>
        private static Reference Ordering(Table table, Link link) => new(link.Key, null, link.Parent,
            SortedArray.Of(link.Legs.Select((g, i) => new Reference.Leg(i, g.Column, g.ParentColumn))), ReferentialAction.NoAction,
            ReferentialAction.NoAction, false, link.Legs.Any(g => table.Find(g.Column)?.Nullable ?? true));

        private void Need(Name table, NeedsRow.Reason reason)
        {
            needs.Add(new NeedsRow(table, reason));
            if (reason is not NeedsRow.Reason.FalseCheck)
            {
                taken.Add(table);
            }
        }

        private static bool Same(Reference reference, PreconditionKind.ForeignKey foreign) =>
            reference.Parent == foreign.Parent && reference.Legs.Count == foreign.Pairs.Count
            && foreign.Pairs.All(p => reference.Legs.Any(g => g.Column == p.Column && g.ParentColumn == p.Referenced));
    }
}
