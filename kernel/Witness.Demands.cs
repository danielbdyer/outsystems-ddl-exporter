using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

public sealed partial record Witness
{
    /// <summary>What a demand writes into one of its columns, from the column's clean value at a row (Planner.Clean), the row's ordinal
    /// and its place within the demand from 0; null where no value can be written, which is a need.</summary>
    private delegate Planted? Valuer(Func<Name, int, Planted?> clean, int row, int offset, Name column);

    /// <summary>
    /// The rows one precondition's state asks of its table beside presence: what they witness, the columns they set, how many rows they
    /// take (two for a duplicate), and the value each column takes; the copy's foreign key they break, which WP 3.5 suspends; or, in
    /// place of rows, the need only a person meets.
    /// </summary>
    private sealed record Demand(Purpose Purpose, Name[] Columns, int Size, Valuer Value, NeedsRow.Reason? Need = null,
        ElementKey? Suspends = null);

    /// <summary>The classes of Fits and of Unique.</summary>
    private const RowClasses FitsClasses = RowClasses.OverLength | RowClasses.OverBytes | RowClasses.NotConvertible
        | RowClasses.EmptyString | RowClasses.Overflow | RowClasses.LossyRoundTrip;

    private const RowClasses UniqueClasses =
        RowClasses.Exact | RowClasses.Case | RowClasses.Accent | RowClasses.TrailingSpace | RowClasses.Nulls;

    /// <summary>What each precondition's state asks of its table beside presence, and those demands laid out in rows.</summary>
    private sealed partial class Planner
    {
        /// <summary>
        /// The demands of one precondition's state, by its kind: the one place a kind of precondition is read, so a new kind is one case
        /// here. TableEmpty asks presence alone; NotNull, a row whose column is NULL (PLT-61); Fits, a row of each class the environment
        /// holds, or of the class that fails for the two types where it names none (PLT-62 to PLT-68); ForeignKey, a row whose every leg
        /// is absent from the parent, which MATCH SIMPLE checks (PLT-75 to PLT-77); Unique, two rows equal on the key, or alike in one way
        /// for its class (PLT-69 to PLT-73); Check, a row a person declares (PLT-78). A precondition a declared row witnesses asks none.
        /// </summary>
        private IEnumerable<Demand> Demands(Located l) =>
            l.State is not { } state || declared.Witnesses.Contains(l.Precondition) ? []
            : l.Here.Kind.Match<IEnumerable<Demand>>(
                () => [],
                () => state.Violating && l.Here.Columns.Count == 1
                    ? [new Demand(new Purpose(l.Precondition, RowClasses.None), [l.Here.Columns[0]], 1, (_, _, _, _) => new Planted.Null())]
                    : [],
                fits => Misfits(l, fits, state),
                foreign => state.Violating && foreign.Pairs.Count > 0 ? [Orphans(l, foreign)] : [],
                unique => Duplicates(l, unique, state),
                _ => state.Violating
                    ? [new Demand(new Purpose(l.Precondition, RowClasses.None), [], 0, (_, _, _, _) => null,
                        new NeedsRow.Reason.FalseCheck(l.Precondition))]
                    : []);

        private static IEnumerable<Demand> Misfits(Located l, PreconditionKind.Fits fits, PreconditionState state)
        {
            if (l.Here.Columns.Count != 1 || l.Table.Find(l.Here.Columns[0]) is not Column.Stored column)
            {
                yield break;
            }

            var classes = state.Classes & FitsClasses;
            if (state.Violating && classes == RowClasses.None)
            {
                var (from, to) = (Literals.FamilyOf(column.Type), Literals.FamilyOf(fits.After));
                classes = to is Literals.Family.Character or Literals.Family.Binary ? RowClasses.OverLength
                    : Literals.Numeric(from) && Literals.Numeric(to) ? RowClasses.Overflow
                    : RowClasses.NotConvertible;
            }

            foreach (var @class in Flags(classes))
            {
                yield return new Demand(new Purpose(l.Precondition, @class), [column.Name], 1,
                    (_, row, _, _) => Literals.Class(column.Type, fits.After, @class, row));
            }
        }

        private static Demand Orphans(Located l, PreconditionKind.ForeignKey foreign)
        {
            var copy = l.Table.References.FirstOrDefault(r => !r.Disabled && Same(r, foreign));
            return new Demand(new Purpose(l.Precondition, RowClasses.None), [.. foreign.Pairs.Select(p => p.Column)], 1,
                (_, row, _, column) => l.Table.Find(column) is Column.Stored leg
                    ? Orphan(leg, foreign.Parent, foreign.Pairs.First(p => p.Column == column).Referenced, row) : null,
                Suspends: copy?.Key);
        }

        private static IEnumerable<Demand> Duplicates(Located l, PreconditionKind.Unique unique, PreconditionState state)
        {
            var key = l.Here.Columns.Select(c => l.Table.Find(c)).OfType<Column.Stored>().ToArray();
            var classes = state.Classes & UniqueClasses;
            if (state.Violating && classes == RowClasses.None)
            {
                classes = RowClasses.Exact;
            }

            foreach (var @class in Flags(classes))
            {
                // The column the pair's second row varies: a string for case, a Unicode string for accent, a varying one for a trailing
                // space; none for exact, and for nulls each nullable key column is NULL in both rows.
                var varied = key.FirstOrDefault(c => @class switch
                {
                    RowClasses.Case => Literals.FamilyOf(c.Type) == Literals.Family.Character,
                    RowClasses.Accent => Literals.Unicode(c.Type),
                    RowClasses.TrailingSpace => Literals.FamilyOf(c.Type) == Literals.Family.Character && !Literals.Fixed(c.Type),
                    _ => false,
                });
                var purpose = new Purpose(l.Precondition, @class);
                if ((@class is RowClasses.Case or RowClasses.Accent or RowClasses.TrailingSpace && varied is null)
                    || (@class == RowClasses.Nulls && !key.Any(c => c.Nullable)))
                {
                    yield return new Demand(purpose, [], 0, (_, _, _, _) => null,
                        new NeedsRow.Reason.NoClassValue(key.Select(c => c.Name).FirstOrDefault(l.Table.Columns[0].Name), @class));
                    continue;
                }

                yield return new Demand(purpose, [.. key.Select(c => c.Name).Concat(unique.FilterColumns).Distinct()], 2,
                    (clean, row, offset, column) =>
                    {
                        var start = row - offset;
                        var keyed = key.FirstOrDefault(c => c.Name == column);
                        if (keyed is null || (@class == RowClasses.Nulls && !keyed.Nullable))
                        {
                            return clean(column, start);
                        }

                        if (@class == RowClasses.Nulls)
                        {
                            return new Planted.Null();
                        }

                        if (keyed != varied)
                        {
                            return clean(column, start);
                        }

                        var first = @class == RowClasses.TrailingSpace
                            ? new Planted.Text(Literals.Marker(start, Literals.Width(keyed.Type) - 1), Literals.Unicode(keyed.Type))
                            : clean(column, start);
                        return offset == 0 ? first : first is not Planted.Text text ? null : @class switch
                        {
                            RowClasses.Case => text with { Content = text.Content.ToUpperInvariant() },
                            RowClasses.Accent when text.Content.StartsWith('w') => text with { Content = "ŵ" + text.Content[1..] },
                            RowClasses.TrailingSpace => text with { Content = text.Content + " " },
                            _ => null,
                        };
                    });
            }
        }

        /// <summary>
        /// The demands laid out in rows (the maximum of a table's needs, M2): demands whose columns overlap share a lane and take
        /// successive rows, in the precondition order and the class order; a lane starts at row 1, so demands on different columns
        /// share rows (PLT-86).
        /// </summary>
        private static List<(Demand Demand, int Start)> Lanes(IReadOnlyList<Demand> demands)
        {
            var lanes = new List<(HashSet<Name> Columns, List<int> Demands)>();
            for (var i = 0; i < demands.Count; i++)
            {
                var lane = (Columns: new HashSet<Name>(demands[i].Columns), Demands: new List<int>());
                foreach (var touching in lanes.Where(l => demands[i].Columns.Any(l.Columns.Contains)).ToArray())
                {
                    lane.Columns.UnionWith(touching.Columns);
                    lane.Demands.AddRange(touching.Demands);
                    lanes.Remove(touching);
                }

                lane.Demands.Add(i);
                lanes.Add(lane);
            }

            var placed = new List<(Demand, int)>();
            foreach (var lane in lanes)
            {
                var start = 1;
                foreach (var i in lane.Demands.Order())
                {
                    placed.Add((demands[i], start));
                    start += demands[i].Size;
                }
            }

            return placed;
        }

        private static IEnumerable<RowClasses> Flags(RowClasses classes) =>
            Enum.GetValues<RowClasses>().Where(c => c != RowClasses.None && classes.HasFlag(c));
    }
}
