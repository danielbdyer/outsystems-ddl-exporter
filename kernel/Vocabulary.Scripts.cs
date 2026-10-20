using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

public static partial class Vocabulary
{
    /// <summary>A statement the change adds to a deploy script: the statement, the tables it and the statements it holds write into, the
    /// precondition it asks, none for one that asks nothing, and whether it executes a procedure no row reads, which leaves it
    /// unclassified.</summary>
    private sealed record Changed(PlanStatement Statement, SortedArray<Name> Writes, Precondition? Asks, bool Unread);

    /// <summary>
    /// The change's deploy scripts, read apart from the rows (the Facsimile's Authored and SeedDrift, QST-78 to QST-82): a batch's
    /// statement of the pre- or post-deploy script, read with the statements it holds, that the script before the change does not hold, by
    /// its text, is the change's; one the script held before has run in every environment, and asks nothing (QST-79). Authored text is
    /// never matched against the rows, so an ALTER … NOT NULL in it raises no NotNull (QST-82). A changed MERGE whose source is a list of
    /// rows is SeedDrift on its table and key; any other changed statement is Authored on the first table or view of either model it
    /// writes or reads, a name of one part read in dbo and any name matched ignoring case, with the columns of that table it names; one
    /// that names no such table, or only tables the change creates, which hold no row, asks nothing. A changed statement that executes a
    /// procedure no row of the vocabulary matches, as <paramref name="known"/> says, reads what the procedure's body reads, which no
    /// statement shows, so it is unclassified, as such an EXECUTE among DacFx's own is (QST-92).
    /// </summary>
    private static List<Changed> Authored(SortedArray<PlanStatement> plan, SortedArray<PlanStatement> scriptsBefore, Context context,
        Func<PlanStatement, bool> known)
    {
        var held = scriptsBefore.Where(s => s.Depth == 0).GroupBy(s => (s.Part, s.Text)).ToDictionary(g => g.Key, g => g.Count());
        var scripted = plan.Where(s => s.Part != ScriptPart.Plan).ToList();
        var changed = new List<Changed>();
        for (var i = 0; i < scripted.Count; i++)
        {
            var top = scripted[i];
            if (top.Depth != 0)
            {
                continue;
            }

            if (held.TryGetValue((top.Part, top.Text), out var times) && times > 0)
            {
                held[(top.Part, top.Text)] = times - 1;
                continue;
            }

            var statements = scripted.Skip(i).TakeWhile((s, at) => at == 0 || s.Depth > 0).ToList();
            IEnumerable<Name> Tables(PlanStatement s) => s.Objects
                .Where(o => o.Role is PlanRole.Target or PlanRole.Read && !o.OutsideTheRows).Select(o => context.Canonical(o.Name))
                .Where(context.Holds);
            var named = statements.SelectMany(Tables).Where(n => !context.Created(n)).Distinct().ToList();
            var writes = SortedArray.Of(statements.Select(s => s.Target).OfType<PlanObject>().Where(o => !o.OutsideTheRows)
                .Select(o => context.Canonical(o.Name)).Where(context.Holds).Distinct());
            SortedArray<Name> Columns(Name table, IEnumerable<Name> columns) =>
                SortedArray.Of(columns.Select(c => context.ColumnOf(table, c)).OfType<Name>().Distinct());
            var unread = statements.Any(s => s.Type == "ExecuteStatement" && !known(s));
            Precondition? asks = named.Count == 0 || unread ? null
                : top.Type == "MergeStatement" && top.Has("VALUES") && top.Target is { } target
                    && context.Canonical(target.Name) is var seeded && named.Contains(seeded)
                    ? new Precondition(seeded, Columns(seeded, top.Key), new PreconditionKind.SeedDrift(
                        context.Code(seeded, Columns(seeded, top.Key), Columns(seeded, top.Columns)), top.Part, top.Line))
                    : new Precondition(named[0],
                        Columns(named[0], statements.Where(s => Tables(s).Contains(named[0])).SelectMany(s => s.Columns)),
                        new PreconditionKind.Authored(top.Part, top.Line));
            changed.Add(new Changed(top, writes, asks, unread));
        }

        return changed;
    }

    /// <summary>
    /// What a program of releases asks (the Facsimile's F8, QST-83): what <paramref name="target"/> asks, the plan from the environment's
    /// schema straight to the last release's model, and the Authored and SeedDrift preconditions of each of <paramref name="releases"/>,
    /// each release's plan read with its own deploy scripts. A release's script meets the rows the final target's preconditions describe:
    /// a first release's pre-deploy ALTER … NOT NULL meets the NULLs the final target's NotNull counts, which its own plan, over an
    /// unchanged model, never asks.
    /// </summary>
    public static SortedArray<Precondition> Program(Reading target, IEnumerable<Reading> releases) =>
        SortedArray.Of(target.Preconditions.Concat(releases.SelectMany(r => r.Preconditions)
            .Where(p => p.Kind is PreconditionKind.Authored or PreconditionKind.SeedDrift)).Distinct());

    /// <summary>
    /// What a precondition of the property read stands as: a ForeignKey or a Check the plan validates after a statement the change adds to
    /// the post-deploy script that writes into its table, or into a ForeignKey's parent, is answered by rows a person authored, since DacFx
    /// validates its new constraints after the post-deploy script (spike S2, on both releases), so the Authored of that statement stands
    /// for it, on its table and columns (the Facsimile's QST-59); any other stands as it is.
    /// </summary>
    private static Precondition StandsFor(Precondition p, List<Changed> changed, Context context)
    {
        if (p.Kind is not (PreconditionKind.ForeignKey or PreconditionKind.Check) || !context.Validated.TryGetValue(p.Table, out var at))
        {
            return p;
        }

        var parent = (p.Kind as PreconditionKind.ForeignKey)?.Parent;
        return changed.FirstOrDefault(c => c.Statement.Part == ScriptPart.PostDeploy && c.Statement.Ordinal < at
                && (c.Writes.Contains(p.Table) || (parent is { } written && c.Writes.Contains(written)))) is { } filling
            ? new Precondition(p.Table, p.Columns, new PreconditionKind.Authored(ScriptPart.PostDeploy, filling.Statement.Line))
            : p;
    }
}
