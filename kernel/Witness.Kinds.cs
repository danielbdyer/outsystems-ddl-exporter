using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DbChange.Kernel;

public sealed partial record Witness
{
    /// <summary>The demands of the kinds only a plan's statements show (WP 2.1's second slice), from the Facsimile's planter cases
    /// (cases/20-planter.md) where it has them.</summary>
    private sealed partial class Planner
    {
        /// <summary>
        /// A key just past its limit (PLT-80, PLT-81): its strings and binaries of a varying width, in order, take as many of the bytes the
        /// limit asks beyond the other columns' fixed sizes as each holds, w and the row's ordinal padded with w, or 0x77 bytes, so the
        /// first wide enough carries the excess and the others keep their clean values; a key whose columns cannot pass the limit together,
        /// or that holds a column of another type no size is known for, is a need for a declared row.
        /// </summary>
        private static Demand LongKey(Located l, PreconditionKind.KeyLength key)
        {
            var columns = l.Here.Columns.Select(c => l.Table.Find(c)).OfType<Column.Stored>().ToArray();
            var varying = columns.Where(c => Literals.FamilyOf(c.Type) is Literals.Family.Character or Literals.Family.Binary
                && !Literals.Fixed(c.Type) && Literals.Width(c.Type) is not null).ToArray();
            var fixedSizes = columns.Except(varying).Select(c => c.Type.KeyBytes).ToArray();
            var remaining = key.Limit + 1 - fixedSizes.Sum(b => b ?? 0);
            var lengths = new Dictionary<Name, long>();
            foreach (var column in varying.TakeWhile(_ => remaining > 0))
            {
                var unit = Literals.Unicode(column.Type) ? 2 : 1;
                lengths[column.Name] = Math.Min(Literals.Width(column.Type)!.Value, (remaining + unit - 1) / unit);
                remaining -= lengths[column.Name] * unit;
            }

            var purpose = new Purpose(l.Precondition, RowClasses.None);
            return remaining > 0 || columns.Length != l.Here.Columns.Count || fixedSizes.Any(b => b is null or long.MaxValue)
                ? new Demand(purpose, [], 0, (_, _, _, _) => null,
                    new NeedsRow.Reason.NoClassValue(l.Here.Columns.FirstOrDefault(l.Table.Columns[0].Name), RowClasses.None))
                : new Demand(purpose, [.. lengths.Keys], 1, (_, row, _, column) => l.Table.Find(column) is Column.Stored carrying
                    ? Literals.FamilyOf(carrying.Type) == Literals.Family.Binary
                        ? new Planted.Bytes(string.Concat(Enumerable.Repeat("77", (int)lengths[column])))
                        : new Planted.Text(Padded("w" + row.ToString(CultureInfo.InvariantCulture), lengths[column]),
                            Literals.Unicode(carrying.Type))
                    : null);
        }

        /// <summary>Two rows whose integer key leaves a gap and does not follow the largest the copy holds (PLT-82): past it by 2 and by 5,
        /// which WP 3.5 writes under SET IDENTITY_INSERT ON into an identity column, so a rebuild that numbers its rows anew is told from
        /// one that keeps their ids. A table whose rebuild keeps no identity column, or keeps one the copy holds as no integer, asks
        /// none.</summary>
        private static IEnumerable<Demand> Gaps(Located l)
        {
            if (l.Here.Columns.Count == 1 && l.Table.Find(l.Here.Columns[0]) is Column.Stored id
                && Literals.FamilyOf(id.Type) == Literals.Family.Integer)
            {
                yield return new Demand(new Purpose(l.Precondition, RowClasses.Gapped), [id.Name], 2,
                    (_, _, offset, _) => new Planted.AfterMax(l.Table.Name, id.Name, offset == 0 ? 2 : 5));
            }
        }

        /// <summary>A row whose period ends before it starts: the end holds the date at the row's ordinal and the start the day after. A
        /// period column the copy generates, which no INSERT writes, or one of a type the planter writes no date for, is a need for a
        /// declared row.</summary>
        private static IEnumerable<Demand> Ended(Located l)
        {
            var period = (PreconditionKind.Temporal)l.Here.Kind;
            var purpose = new Purpose(l.Precondition, RowClasses.None);
            if (l.Table.Find(period.Start) is not Column.Stored start || l.Table.Find(period.End) is not Column.Stored end
                || Literals.FamilyOf(start.Type) is not (Literals.Family.DateTime or Literals.Family.DateTime2 or Literals.Family.Date)
                || Literals.FamilyOf(end.Type) != Literals.FamilyOf(start.Type))
            {
                yield return new Demand(purpose, [], 0, (_, _, _, _) => null,
                    new NeedsRow.Reason.NoClassValue(period.Start, RowClasses.None));
                yield break;
            }

            yield return new Demand(purpose, [start.Name, end.Name], 1,
                (_, row, _, column) => column == start.Name ? Literals.Of(start.Type, row + 1) : Literals.Of(end.Type, row));
        }

        /// <summary>A need for one declared row that witnesses the precondition, the table's other rows planted beside it.</summary>
        private static Demand Declares(Located l) => new(new Purpose(l.Precondition, RowClasses.None), [], 0, (_, _, _, _) => null,
            new NeedsRow.Reason.Declares(l.Precondition));

        private static string Padded(string text, long length) =>
            text.Length >= length ? text[..(int)length] : text + new string('w', (int)length - text.Length);
    }

    /// <summary>Why only a declared row witnesses a precondition: a Computed's expression, which the planter does not read; an Authored
    /// statement's shapes, which a person looks at; a SeedDrift's rows, which hold the seed's own code under a key it does not
    /// use.</summary>
    private static string Witnessed(Name table, Precondition witness)
    {
        string Other() => table + " holds rows that only a declared row witnesses for " + witness;
        return witness.Kind.Match(Other, Other, _ => Other(), _ => Other(), _ => Other(), _ => Other(), _ => Other(), Other,
            c => table + "." + witness.Columns.FirstOrDefault() + " is computed as " + c.Expression + " over every row of the table, and"
                + " the planter reads no expression, so it writes no value the expression fails on",
            _ => Other(), Other,
            a => "The change's " + (a.Script == ScriptPart.PreDeploy ? "pre-deploy" : "post-deploy") + " script reads " + table
                + " at line " + a.Line.ToString(CultureInfo.InvariantCulture) + ", and only a person who looks at the rows it reads in the"
                + " environment can declare the shapes it meets",
            s => table + " holds rows in the environment whose key or code disagrees with the rows the change's seed at line "
                + s.Line.ToString(CultureInfo.InvariantCulture) + " writes, and the planter writes no value of the seed's under another"
                + " key");
    }
}
