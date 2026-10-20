using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// Steps 2 to 8 of the copy build in their order, and steps 6 to 8 themselves. The copy as restored is read whole, its Schema and, with an
/// existing data, its own plan of the change: the plan of the change's package against the copy, its BlockOnPossibleDataLoss checks, the
/// change from the copy's model to the change's, and so the preconditions, each placed by the coordinate rules and counted by WP 2.3's
/// aggregate queries. WP 3.8's plan is taken from them, the declared witnesses' blocks standing in for the rows they own; a need the plan
/// knows stops the build before any row (PLT-120). Step 6 hashes every table that held a row as restored and every table a row or a block
/// was planted into, as SQL Server stores each value, order apart, a NULL apart from an empty value. Step 7 counts the copy again over the
/// same preconditions and requires the environment's answers, every no included (§4 row 21; the Facsimile's F1). Step 8 plans the base's
/// package against the copy and requires it empty (F2), reading a check written with IN as check drift does (kernel CheckWrittenWithIn).
/// </summary>
public static partial class SyntheticCopy
{
    /// <summary>
    /// The change as a database's own plan holds it: its preconditions, the change from the database's model, the counts the coordinate
    /// rules give a precondition on a table the change creates, and the aggregate queries of the rest.
    /// </summary>
    internal sealed record Asked(SortedArray<Precondition> Preconditions, Change Change, IReadOnlyList<PreconditionCount> Answered,
        AggregateQueries.Queries Queries);

    private static Result<Answer> Steps(Inputs inputs, GitRef at, string commit, string? head, bool restored, IReadOnlyList<Finding> made)
    {
        var (copy, log, notes) = (inputs.Copy, inputs.Log, new List<Finding>(made));
        if (Disabled(inputs).Failed(out _, out var error) || Read(inputs).Failed(out var read, out error))
        {
            return error;
        }

        var (schema, asked) = (read.Schema, read.Asked);
        if (Declare(inputs, schema, asked, notes).Failed(out var blocks, out error)
            || Witness.Plan(schema, asked?.Preconditions ?? [], inputs.Existing, asked?.Change, new Witness.Declared(
                    SortedArray.Of(blocks.Where(b => b.Block.Plants is not null).Select(b => b.Table)),
                    SortedArray.Of(blocks.SelectMany(b => b.Witnessed))))
                .Failed(out var plan, out error))
        {
            return error;
        }

        Answer Ended(Kind kind, IReadOnlyList<Planting> planted, IReadOnlyList<Hash> hashes, SortedArray<PreconditionState> answers,
            IReadOnlyList<Finding> findings) => new(kind, copy.Name, at, commit, head, restored, planted, hashes, answers, findings, notes);
        if (plan.Needs.Count > 0)
        {
            return Ended(Kind.NeedsInput, [], [], [], [.. plan.Needs.Select(Need)]);
        }

        if ((asked is null ? Result.Ok(SortedArray.Of<PreconditionState>([])) : Count(copy, asked, log)).Failed(out var before, out error))
        {
            return error;
        }

        var planter = new Planter(copy, schema, plan, log, (p, @class) => before.FirstOrDefault(s => s.Precondition == p)
            is { Violating: true } s && (@class == RowClasses.None || s.Classes.HasFlag(@class)));
        if (planter.Read().Failed(out _, out error)
            || Hashed(copy, schema, planter.Restored.Where(r => r.Value > 0).Select(r => r.Key), log).Failed(out var seeded, out error)
            || planter.Suspend(inputs.Existing?.Trust ?? [], blocks.Where(b => b.Block.Plants is not null).Select(b => b.Table), notes)
                .Failed(out var suspended, out error)
            || Result.All(Sequence(plan, blocks, schema, asked).Select(step => step.Block is { } block
                    ? planter.Declare(block, step.Table) : planter.Plant(step.Planting!)))
                .Bind(_ => planter.Defer()).Bind(_ => planter.Restore(suspended)).Failed(out _, out error))
        {
            return error;
        }

        var planted = plan.Tables.Select(t => new Planting(t.Table, planter.Tally.GetValueOrDefault(t.Table).Planted,
            planter.Tally.GetValueOrDefault(t.Table).Skipped)).ToList();
        var touched = seeded.Keys.Concat(planted.Where(p => p.Planted > 0).Select(p => p.Table)).Concat(blocks.Select(b => b.Table))
            .Distinct().Order().ToList();
        if (Hashed(copy, schema, touched, log).Failed(out var after, out error))
        {
            return error;
        }

        var hashes = touched.Select(t => new Hash(t, planter.Restored.GetValueOrDefault(t),
            seeded.TryGetValue(t, out var held) ? held.Hash : null, after[t].Rows, after[t].Hash)).ToList();
        if (planter.Needs.Count > 0)
        {
            return Ended(Kind.NeedsInput, planted, hashes, [], planter.Needs);
        }

        if ((asked is null ? Result.Ok(SortedArray.Of<PreconditionState>([])) : Count(copy, asked, log)).Failed(out var answers, out error))
        {
            return error;
        }

        if (inputs.Existing is { } existing && Accepted(existing, asked!.Preconditions, answers) is { Count: > 0 } refusals)
        {
            return Ended(Kind.NeedsInput, planted, hashes, answers, refusals);
        }

        var kept = suspended.Where(s => s.Restored.Disabled).Select(s => s.Key).ToHashSet();
        return Identical(inputs, kept, notes).Map(differences =>
            Ended(differences.Count > 0 ? Kind.Differs : Kind.Built, planted, hashes, answers, differences));
    }

    /// <summary>
    /// Step 4's first half: each constraint the environment holds disabled and the base declares, disabled on the copy before the copy is
    /// read, so the copy's own plan of the change is the environment's; a schema read keeps a disabled constraint (spike S9), and the plan
    /// of the change drops and adds a disabled key on both. Step 4's second half suspends and restores the rest (SyntheticCopy.Plant.cs).
    /// </summary>
    private static Result<int> Disabled(Inputs inputs) =>
        inputs.Existing?.Trust.Where(t => t.Disabled).ToList() is not { Count: > 0 } disabled ? 0
        : inputs.Base.Elements.Bind(@base => Schema.Of(@base.Elements)).Bind(schema => Result.All(schema.Tables
                .SelectMany(t => t.References.Select(r => (r.Key, r.Constraint)).Concat(t.Checks.Select(c => (c.Key, c.Constraint)))
                    .Where(c => c.Constraint is not null && disabled.Any(d => d.Constraint == c.Key))
                    .Select(c => SqlServer.Query(inputs.Copy, new SqlServer.Statement(c.Key + ": disable", "ALTER TABLE " + t.Name
                        + " NOCHECK CONSTRAINT " + Named(c.Constraint!.Value.Base) + ";"), inputs.Log, _ => c.Key))))
            .Map(done => done.Count));

    /// <summary>The copy, read once by an extract: its Schema and, with an existing data, its own plan of the change.</summary>
    private static Result<(Schema Schema, Asked? Asked)> Read(Inputs inputs) => DacFx.Extract(inputs.Copy).Bind(extracted =>
    {
        using (extracted)
        {
            return extracted.Elements.Bind(model => Schema.Of(model.Elements).Bind(schema => Ssdt.CollationOf(model.Elements)
                .Bind(collation => (inputs.Existing is null ? Result.Ok<Asked?>(null)
                    : Ask(inputs.Copy, inputs.Head!, inputs.Profile, extracted, model.Elements, collation).Map(a => (Asked?)a))
                    .Map(asked => (schema, asked)))));
        }
    });

    /// <summary>
    /// A database's own plan of the change: the change's package planned against the database as extracted, under the pipeline's profile,
    /// its BlockOnPossibleDataLoss checks read, the change from the database's model, its preconditions, each placed by the coordinate
    /// rules, and WP 2.3's aggregate queries of those counted; as predict plans the change against an environment.
    /// </summary>
    internal static Result<Asked> Ask(SqlServer.Database target, Ssdt.Package head, PublishProfile.Strict profile, Ssdt.Package extracted,
        SortedArray<Element> model, Collation collation) =>
        head.Elements.Bind(after => DacFx.Plan(head, extracted, target.Catalog, profile, [])
        .Bind(plan => BlockOnPossibleDataLoss.Read(plan, target))
        .Bind(checks => Change.Between(model, after.Elements, after.Renames, collation)
            .Bind(change => Preconditions.Of(model, after.Elements, change, BlockOnPossibleDataLoss.Sites(checks))
            .Bind(preconditions => Result.All(preconditions.Select(p => Coordinates.Of(p, change, model, collation)))
            .Bind(places => AggregateQueries.Of(places.OfType<Coordinates.Counted>(), after.Elements, model, collation,
                    SqlServer.Session.Assumed)
                .Map(queries => new Asked(preconditions, change, [.. places.OfType<Coordinates.Answered>().Select(a => a.Count)],
                    queries)))))));

    /// <summary>A database's answers to the change's preconditions: each table's aggregate query counted through SqlServer.Measure, and
    /// the counts the coordinate rules give.</summary>
    internal static Result<SortedArray<PreconditionState>> Count(SqlServer.Database target, Asked asked, SqlServer.QueryLog log) =>
        Result.All(asked.Queries.Tables.Select(t => AggregateQueries.Count(target, t, log))).Map(counts =>
            Measured(asked.Answered.Concat(counts.SelectMany(c => c.Counted)), counts.SelectMany(c => c.Classes)));

    /// <summary>The states counts give, as ExistingData.Of reads them, each with the classes whose rows a count found.</summary>
    internal static SortedArray<PreconditionState> Measured(IEnumerable<PreconditionCount> counts,
        IEnumerable<AggregateQueries.ClassCount> classes)
    {
        var found = classes.Where(c => c.Rows > 0).ToList();
        return SortedArray.Of(ExistingData.Of(counts).States.Select(s => s with
        {
            Classes = found.Where(c => c.Precondition == s.Precondition)
                .Aggregate(RowClasses.None, (all, c) => all | ExistingDataFile.Flag(c.Class)),
        }));
    }

    /// <summary>
    /// Step 7 (the Facsimile's F1, PLT-91 to PLT-93, PLT-98): each precondition the environment answered, answered alike by the copy,
    /// whether its table holds a row, whether a row breaks it, and, where the environment names them, the classes of its rows; every no
    /// included. A precondition the environment answered and the copy's plan lacks is baseline drift, which no row plants. Each difference
    /// is a finding naming the precondition, at exit 8.
    /// </summary>
    internal static IReadOnlyList<Finding> Accepted(ExistingData environment, SortedArray<Precondition> asked,
        SortedArray<PreconditionState> copy)
    {
        var refused = new List<Finding>();
        foreach (var state in environment.States)
        {
            var held = copy.FirstOrDefault(s => s.Precondition == state.Precondition);
            if (!asked.Contains(state.Precondition) || held is null)
            {
                refused.Add(Finding.Error("witness.acceptance", state.Precondition.ToString(), "The environment answered "
                    + state.Precondition + " " + Answered(state) + ", and the copy's own plan of the change "
                    + (held is null && asked.Contains(state.Precondition)
                        ? "counts no answer to it, which no query the allowlist admits counts"
                        : "holds no such precondition, so the environment's plan holds an operation the repository at the ref does not "
                            + "make"),
                    "Build the copy at the ref the environment is deployed at, or read the environment's existing data again."));
            }
            else if (held.Populated != state.Populated || held.Violating != state.Violating
                || (state.Classes != RowClasses.None && held.Classes != state.Classes))
            {
                refused.Add(Finding.Error("witness.acceptance", state.Precondition.ToString(), "The copy answers " + state.Precondition
                        + " " + Answered(held) + ", where the environment answered it " + Answered(state) + ".",
                    state.Violating && !held.Violating
                        ? "Plant a row that breaks it, under -- witness: " + state.Precondition + " in " + Witnesses.Path + "."
                        : "Correct the rows " + Witnesses.Path + " declares, or the seed's, so the copy answers it as the environment "
                            + "does."));
            }
        }

        return refused;
    }

    /// <summary>A state as a sentence writes it: empty; populated and clean; populated and violating, with its classes.</summary>
    private static string Answered(PreconditionState state) =>
        !state.Populated ? "empty" : !state.Violating && state.Classes == RowClasses.None ? "populated and clean"
        : "populated and " + (state.Violating ? "violating" : "clean") + (state.Classes == RowClasses.None ? ""
            : " (" + string.Join(", ", Enum.GetValues<AggregateQueries.Class>().Where(c => state.Classes.HasFlag(ExistingDataFile.Flag(c)))
                .Select(AggregateQueries.Word)) + ")");

    /// <summary>
    /// Step 8: the base's package planned against the copy as it now stands; each operation a difference, but the drop and the create of a
    /// constraint the copy holds disabled as the environment does, which a deploy plans against the environment too (a note), and a check
    /// written with IN, named with its cause and its remedy as check drift names it.
    /// </summary>
    private static Result<IReadOnlyList<Finding>> Identical(Inputs inputs, IReadOnlySet<ElementKey> kept, List<Finding> notes) =>
        inputs.Base.Elements.Bind(@base => DacFx.Extract(inputs.Copy).Bind(extracted =>
        {
            using (extracted)
            {
                return extracted.Elements.Bind(model => Ssdt.CollationOf(model.Elements).Bind(collation =>
                    DacFx.Plan(inputs.Base, extracted, inputs.Copy.Catalog, inputs.Profile, [])
                        .Map(plan => Differences(plan.Report, model.Elements, @base.Elements, collation, kept, notes))));
            }
        }));

    /// <summary>What step 8 finds in a plan from the base to the copy, as the class says; the notes it adds are the disabled constraints
    /// the plan drops and adds again.</summary>
    internal static IReadOnlyList<Finding> Differences(DeployReport plan, SortedArray<Element> copy, SortedArray<Element> @base,
        Collation collation, IReadOnlySet<ElementKey> kept, List<Finding> notes)
    {
        var written = CheckWrittenWithIn.Of(plan, copy, @base, collation);
        notes.AddRange(plan.Operations.Where(o => kept.Contains(o.Key)).Select(o => o.Key).Distinct().Select(k => Finding.Note(
            "synthetic-copy.disabled", k.ToString(), "The plan from the base to the copy drops " + k + " and adds it again, since the copy"
                + " holds it disabled as the environment does; a deploy plans the same against the environment.")));
        return
        [
            .. plan.Operations.Where(o => !o.Kind.IsConsequence && !kept.Contains(o.Key) && !written.Any(w => w.Key == o.Key))
                .Select(o => Finding.Warning("synthetic-copy.identity", o.Key.ToString(),
                    "The plan from the base to the copy would " + o.Kind.Name.ToLowerInvariant() + " " + o.Key
                        + ", so the copy's schema differs from the repository at the ref.",
                    "Run dbchange check drift --target synthetic-copy --at <the ref> to see each object that differs.")),
            .. written.Select(check => Finding.Warning("synthetic-copy.check-written-with-in", check.Key.ToString(),
                check.Key + " is written with IN in the repository, " + check.Written + ", which SQL Server stores as the chain of ORs "
                    + check.Stored + "; DacFx compares the two texts, so every plan against the copy drops it and creates it again.",
                "Write the check in the OR form SQL Server stores, which sys.check_constraints.definition shows: CHECK (" + check.Stored
                    + ").")),
        ];
    }

    /// <summary>
    /// A table's rows and content hash: each row as SQL Server stores each column's value, converted to varbinary(max), a NULL apart from
    /// an empty value; the rows' digests sorted, so the order SQL Server returns them in changes nothing.
    /// </summary>
    private static Result<IReadOnlyDictionary<Name, (long Rows, Fingerprint Hash)>> Hashed(SqlServer.Copy copy, Schema schema,
        IEnumerable<Name> tables, SqlServer.QueryLog log) =>
        Result.All(tables.Select(name => schema.Find(name)!).Select(table => SqlServer.Query(copy, new SqlServer.Statement(
                table.Name + ": hash", "SELECT " + string.Join(", ", table.Columns.Select(c => "CONVERT(varbinary(max), " + c.Name + ")"))
                    + " FROM " + table.Name + ";"), log,
            rows => (table.Name, Rows: (long)rows.Count, Hash: Digest(rows)))))
        .Map(hashed => (IReadOnlyDictionary<Name, (long, Fingerprint)>)hashed.ToDictionary(h => h.Name, h => (h.Rows, h.Hash)));

    private static Fingerprint Digest(IReadOnlyList<IReadOnlyList<object?>> rows) =>
        Fingerprint.Of(string.Concat(rows.Select(row => Fingerprint.Of(string.Concat(row.Select(v =>
                v is byte[] bytes ? "+" + bytes.Length.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(bytes) : "-")))
            .ToString()).Order(StringComparer.Ordinal).Prepend(rows.Count.ToString(CultureInfo.InvariantCulture) + "\n")));

    /// <summary>A declared block, the table it plants or whose precondition it witnesses, and the preconditions it witnesses.</summary>
    private sealed record Placed(Witnesses.Block Block, Name Table, SortedArray<Precondition> Witnessed);

    /// <summary>
    /// The declared witnesses at HEAD read into blocks, each placed: a block that plants a table the copy holds; a block that witnesses
    /// one of the change's preconditions, by the text dbchange writes it in; witness.header otherwise. Without an existing data the copy
    /// holds presence rows alone, so a block that witnesses a precondition is left, with a note.
    /// </summary>
    private static Result<IReadOnlyList<Placed>> Declare(Inputs inputs, Schema schema, Asked? asked, List<Finding> notes)
    {
        var placed = new List<Placed>();
        foreach (var block in inputs.Blocks)
        {
            if (block.Plants is { } table)
            {
                if (schema.Find(table) is null)
                {
                    return Witnesses.Header(block.Line, "plants " + table + ", a table the copy does not hold",
                        "Name a table of the repository at the ref after -- plants:.");
                }

                placed.Add(new Placed(block, table, []));
            }
            else if (asked is null)
            {
                notes.Add(Finding.Note("witness.unplaced", Witnesses.Path + " line " + Number(block.Line), "The block witnesses "
                    + block.Witness + ", and the copy holds presence rows alone, with no existing data whose precondition it answers."));
            }
            else if (asked.Preconditions.Where(p => p.ToString() == block.Witness).ToArray() is { Length: > 0 } witnessed)
            {
                placed.Add(new Placed(block, witnessed[0].Table, SortedArray.Of(witnessed)));
            }
            else
            {
                return Witnesses.Header(block.Line, "witnesses " + block.Witness + ", which is no precondition of the change",
                    "Write a precondition of the change after -- witness:, as dbchange names it in the existing data.");
            }
        }

        return Result.Ok<IReadOnlyList<Placed>>(placed);
    }

    /// <summary>
    /// The order rows and blocks are planted in: the plan's tables in Order's order; a block that plants a table just before the first of
    /// them that references it, or at the end; a block that witnesses a precondition just after its table's rows, or at the end.
    /// </summary>
    private static List<(Witness.Planting? Planting, Witnesses.Block? Block, Name Table)> Sequence(Witness plan,
        IReadOnlyList<Placed> blocks, Schema schema, Asked? asked)
    {
        var steps = plan.Tables.Select(t => (Planting: (Witness.Planting?)t, Block: (Witnesses.Block?)null, t.Table)).ToList();
        bool References(Name child, Name parent) => schema.Find(child)?.References.Any(r => r.Parent == parent) == true
            || asked?.Preconditions.Any(p => p.Table == child && p.Kind is PreconditionKind.ForeignKey f && f.Parent == parent) == true;
        foreach (var placed in blocks)
        {
            var at = placed.Block.Plants is not null
                ? steps.FindIndex(s => s.Planting is not null && References(s.Table, placed.Table))
                : steps.FindIndex(s => s.Planting is not null && s.Table == placed.Table) is var own and >= 0 ? own + 1 : -1;
            steps.Insert(at < 0 ? steps.Count : at, (null, placed.Block, placed.Table));
        }

        return steps;
    }
}
