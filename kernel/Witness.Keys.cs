using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

public sealed partial record Witness
{
    /// <summary>A table's keys, and what they ask of its planted values: no value twice in a one-column key where no demand asks for
    /// it, and a parent row of its own for each child row where a leg is in a key.</summary>
    private sealed partial class Planner
    {
        /// <summary>The copy's enabled keys of a table, each its columns: the primary key and every unique constraint and index.</summary>
        private static IEnumerable<SortedArray<Name>> Keys(Table table) =>
            (table.Key is { } pk ? [SortedArray.Of(pk.Columns.Select(c => c.Column))] : Array.Empty<SortedArray<Name>>())
            .Concat(table.Uniques.Where(u => !u.Disabled).Select(u => SortedArray.Of(u.Columns.Select(c => c.Column))));

        /// <summary>Whether a column is in a key of the copy's table, or of a Unique precondition on it.</summary>
        private bool Keyed(Table table, Name column) => Keys(table).Any(k => k.Contains(column))
            || located.Any(l => l.Table.Name == table.Name && l.Here.Kind is PreconditionKind.Unique && l.Here.Columns.Contains(column));

        /// <summary>Whether a nullable column is by itself an enabled unique key of the copy without a filter, which holds one NULL at
        /// most.</summary>
        private static bool Alone(Table table, Name column) => table.Find(column) is Column.Stored { Nullable: true }
            && table.Uniques.Any(u => !u.Disabled && u.Filter is null && u.Columns.Count == 1 && u.Columns[0].Column == column);

        /// <summary>
        /// The values a one-column key holds in more than one planted row where no demand asks for it, a key of the copy or one the change
        /// adds that the environment answered clean: each as the INSERT writes it, a deferred reference's first pass included, and none of
        /// a column no value is written in, which is a need already. Two NULLs are equal in a key without a filter, which holds one NULL
        /// at most (Msg 2627, 2601). No NULL is counted in a filtered unique index, which WHERE [c] IS NOT NULL leaves NULLs out of: the
        /// planter reads no filter, so a filter over other columns alone, which leaves those NULLs in, is not checked. A literal that
        /// repeats because the column's literals have room for fewer values than the rows is a narrow key (PLT-42, PLT-114); NULL, or any
        /// other value, is named with the rows that hold it.
        /// </summary>
        private IEnumerable<NeedsRow.Reason> Repeats(Table table, List<Row> built, Demand[] demands, HashSet<Name> unwritten)
        {
            var asked = demands.Where(d => d.Size == 2).SelectMany(d => d.Columns).ToHashSet();
            foreach (var (column, filtered) in Singles(table).Where(k => !unwritten.Contains(k.Column) && !asked.Contains(k.Column)))
            {
                var values = built.Select(r => r.Cells.First(c => c.Column == column).Value)
                    .Select(v => v is Planted.Linked { FirstPass: { } first } ? first : v)
                    .Where(v => v is Planted.Number or Planted.Text or Planted.Bytes || (v is Planted.Null && !filtered));
                foreach (var repeat in values.GroupBy(v => v).Where(g => g.Count() > 1))
                {
                    var room = repeat.Key is Planted.Null ? built.Count : Room(table, column, built.Count);
                    yield return room < built.Count ? new NeedsRow.Reason.NarrowKey(column, room)
                        : new NeedsRow.Reason.Repeated(column, repeat.Key, repeat.Count());
                }
            }
        }

        /// <summary>The one-column keys whose planted values must differ, each with whether it has a filter: the copy's enabled keys,
        /// and each Unique the change adds that the environment answered clean, or that a plan without existing data meets.</summary>
        private IEnumerable<(Name Column, bool Filtered)> Singles(Table table) =>
            (table.Key is { Columns.Count: 1 } key ? [(key.Columns[0].Column, false)] : Array.Empty<(Name, bool)>())
            .Concat(table.Uniques.Where(u => !u.Disabled && u.Columns.Count == 1).Select(u => (u.Columns[0].Column, u.Filter is not null)))
            .Concat(located.Where(l => l.Table.Name == table.Name && l.State is not { Violating: true } && l.Here.Columns.Count == 1)
                .Select(l => (Column: l.Here.Columns[0], Unique: l.Here.Kind as PreconditionKind.Unique)).Where(l => l.Unique is not null)
                .Select(l => (l.Column, l.Unique!.Filter is not null)))
            .Distinct();

        /// <summary>How many distinct literals the planter writes into a column at the ordinals 1 to <paramref name="rows"/>: of its
        /// type, or, where the change retypes it, the clean values both types hold (PLT-94); fewer than the rows where the literals have
        /// room for fewer, as bit's 1 and 0.</summary>
        private long Room(Table table, Name column, int rows) => table.Find(column) is not Column.Stored stored ? rows
            : Enumerable.Range(1, rows).Select(n => Retyped(table, column) is { } fits ? Literals.Clean(stored.Type, fits.After, n)
                : Literals.Of(stored.Type, n)).Distinct().LongCount();
    }
}
