using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// predict as a use case (V3_MILESTONES.md WP 2.5, M2): whether a change blocks or applies on each environment the caller names, and why,
/// read-only. The change is the one from the commit where its branch left the branch it merges into (the merge base of the head and the
/// base) to the head, both built once a run (M1's build). For each environment, alone, so one that cannot be read leaves the others
/// predicted: its connection reached as the caller, with VIEW DEFINITION, before anything else is read, so a denied login is one sentence;
/// its database extracted once; the head's package planned against it, package to package, under the environment's profile and SQLCMD
/// values; the plan's BlockOnPossibleDataLoss checks read (WP 2.2); the preconditions of the environment's own plan, the property read's
/// and the kinds only its statements show (kernel Vocabulary), each placed by the coordinate rules and counted by WP 2.3's aggregate
/// queries through SqlServer.Measure; each foreign key and check the environment holds read for its trust, the disabled and untrusted
/// ones kept in the existing data's trust list, from which synthetic-copy up reproduces them; and kernel Outcome.Predict over the
/// preconditions the counts answered, with the operations of the environment's plan the change does not make as baseline drift. A
/// precondition no read-only query answers, one a person declares, and a statement no row of the vocabulary classifies leave an
/// environment that does not block not whole: the change goes to proof.
/// </summary>
public static class Predict
{
    /// <summary>The environment predicted when none is named: the developer's, Dev (V3_MILESTONES.md §5).</summary>
    public static EnvironmentName Dev { get; } = ((Result<EnvironmentName>.Ok)EnvironmentName.Of("predict", "dev")).Value;

    /// <summary>The branches a change is taken to merge into when no base is named, in the order tried: the origin's default branch, as
    /// git clone records it, then main.</summary>
    private static readonly string[] Trunks = ["origin/HEAD", "main"];

    /// <summary>What predict is asked: the environments, the head, the branch the change merges into (none: <see cref="Trunks"/>) and
    /// the project, where the repository holds several.</summary>
    public sealed record Request(IReadOnlyList<EnvironmentName> Environments, GitRef At, GitRef? Base, string? Project);

    /// <summary>What predict answers: the head and its commit, the base's commit, the change's fingerprint, the time every claim records,
    /// each environment's prediction or why it has none, and the notes the two models raised.</summary>
    public sealed record Answer(GitRef At, string Commit, string Base, Fingerprint Change, DateTimeOffset Time,
        IReadOnlyList<Environment> Environments, IReadOnlyList<Finding> Notes);

    /// <summary>One environment, by its name, and its prediction or the error that left it unpredicted.</summary>
    public sealed record Environment(EnvironmentName Name, Result<Predicted> Predicted);

    /// <summary>
    /// What one environment predicts: what the machine block carries; the outcome over the preconditions the counts answered, with the
    /// counts' BlockOnPossibleDataLoss sites; the preconditions left unanswered, with why; the statements no row of the vocabulary
    /// classifies; and the notes the reading raised.
    /// </summary>
    public sealed record Predicted(Prediction Prediction, Outcome Outcome, SortedArray<AggregateQueries.Unanswered> Unanswered,
        SortedArray<PlanStatement> Unclassified, IReadOnlyList<Finding> Notes)
    {
        /// <summary>Whether the prediction is whole: every precondition answered and every statement of the plan classified.</summary>
        public bool Whole => Unanswered.Count == 0 && Unclassified.Count == 0;

        /// <summary>The count of a precondition, or none where no count gave its state.</summary>
        public PreconditionCount? Count(Precondition precondition) =>
            Prediction.Counts.FirstOrDefault(c => c.Precondition == precondition);
    }

    /// <summary>What every environment's plan reads of the run: the repository, its environments, the two packages and the head's model,
    /// the change, DacFx's release and the time.</summary>
    private sealed record Inputs(string Root, Result<Environments> EnvironmentsFile, Ssdt.Package Head, Ssdt.ModelElements HeadModel,
        Ssdt.Package Base, Change Change, DacFxVersion DacFx, DateTimeOffset Time);

    /// <summary>predict, the answer stamped with DacFx and the pin.</summary>
    public static Stamped<Answer> Run(Checkout checkout, Request request, SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var refused })
        {
            return new(standing.Stamp, refused);
        }

        var (stamp, root) = (standing.Stamp!, checkout.Root);
        var time = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var environmentsFile = EnvironmentsFile.Read(root);
        if (environmentsFile.Bind(environments => Named(environments, request.Environments)).Failed(out var named, out var error)
            || Ssdt.Build(root, request.At.ToString(), request.Project, checkout.Tool, checkout.WorkingDirectory)
                .Failed(out var head, out error)
            || BaseOf(root, head.Commit, request.Base).Failed(out var @base, out error)
            || Ssdt.Build(root, @base, request.Project, checkout.Tool, checkout.WorkingDirectory).Failed(out var baseBuilt, out error)
            || Ssdt.Open(head.Built.Path).Failed(out var headPackage, out error))
        {
            return new(stamp, error);
        }

        using (headPackage)
        {
            if (Ssdt.Open(baseBuilt.Built.Path).Failed(out var basePackage, out error))
            {
                return new(stamp, error);
            }

            using (basePackage)
            {
                return new(stamp, headPackage.Elements.Bind(headModel => basePackage.Elements.Bind(baseModel =>
                    Ssdt.CollationOf(headModel.Elements).Bind(collation =>
                    Change.Between(baseModel.Elements, headModel.Elements, headModel.Renames, collation).Map(change =>
                    {
                        var inputs = new Inputs(root, environmentsFile, headPackage, headModel, basePackage, change, stamp.DacFx, time);
                        return new Answer(request.At, head.Commit, @base, Fingerprint.Of(change), time,
                            [.. named.Select(e => new Environment(e.Name, One(inputs, e, log)))],
                            [.. headModel.Notes(headPackage.Source), .. baseModel.Notes(basePackage.Source)]);
                    })))));
            }
        }
    }

    /// <summary>Each environment asked for, as the environments file names it; target.unnamed for one it does not, before anything is
    /// built.</summary>
    private static Result<IReadOnlyList<NamedEnvironment>> Named(Environments environments, IReadOnlyList<EnvironmentName> asked) =>
        Result.All(asked.Distinct().Select(name => environments.Named(name) is { } named ? Result.Ok(named)
            : new Error("target.unnamed", "env:" + name + " names no environment of " + EnvironmentsFile.Json + ".",
                environments.All.Count == 0
                    ? "Add the environment to " + EnvironmentsFile.Json + " with its host, connection reference and profile."
                    : "Name one it holds: " + string.Join(", ", environments.All.Select(e => e.Target)) + ".")));

    /// <summary>
    /// The commit the change is read from: the merge base of the head and the branch named, or else of the first of
    /// <see cref="Trunks"/> the repository holds; ref.no-base where it holds none.
    /// </summary>
    internal static Result<string> BaseOf(string root, string head, GitRef? named) =>
        named is { } given ? Git.MergeBase(root, head, given.ToString())
        : Trunks.Aggregate(Result.Fail<string>(new Error("ref.no-base",
                "predict reads the change from the commit where its branch left the branch it merges into, and this repository holds "
                    + "neither " + string.Join(" nor ", Trunks) + " to find that commit by.",
                "Name the branch the change merges into with --base, such as --base origin/develop.")),
            (found, trunk) => found is Result<string>.Ok ? found
                : Git.MergeBase(root, head, trunk).Match(Result.Ok, e => e.Code == "ref.unresolved" ? found : e));

    /// <summary>One environment's prediction: its database reached, extracted and planned, or the first error on the way.</summary>
    private static Result<Predicted> One(Inputs inputs, NamedEnvironment named, SqlServer.QueryLog log) =>
        SqlServer.Resolve(named.Target, inputs.EnvironmentsFile, inputs.Root).Bind(database => PublishProfiles.Of(named, inputs.Root)
            .Bind(profile => SqlServer.Reach(database, log).Bind(readable => SqlServer.SqlCmdValues(database).Bind(values =>
            DacFx.Extract(database).Bind(extracted =>
            {
                using (extracted)
                {
                    return extracted.Elements.Bind(environment => Ssdt.CollationOf(environment.Elements).Bind(collation =>
                        DacFx.Plan(inputs.Head, extracted, database.Catalog, profile, values).Bind(plan =>
                        Planned(inputs, database, environment.Elements, collation, plan, log).Map(counted => new Predicted(
                            new Prediction(named.Name, counted.Existing, counted.Counts, Provenance.Prediction(
                                Fingerprint.Of(inputs.Change), Fingerprint.Of(environment.Elements),
                                ExistingDataFile.FingerprintOf(counted.Existing), inputs.DacFx, null, profile.Fingerprint, named.Target,
                                inputs.Time)),
                            counted.Outcome, counted.Unanswered, counted.Unclassified,
                            [.. profile.Notes, .. readable.Notes, .. environment.Notes(named.Target.ToString()), .. plan.Notes,
                                .. counted.Notes])))));
                }
            })))));

    /// <summary>What an environment's own plan counted: its existing data and counts, the outcome over what the counts answered, the
    /// preconditions left unanswered, the statements no vocabulary row classifies, and the notes.</summary>
    private sealed record Counted(ExistingData Existing, SortedArray<PreconditionCount> Counts, Outcome Outcome,
        SortedArray<AggregateQueries.Unanswered> Unanswered, SortedArray<PlanStatement> Unclassified, IReadOnlyList<Finding> Notes);

    /// <summary>
    /// The environment's own plan read and counted: its checks' sites, the change from the environment's model to the head's, the
    /// preconditions the property read and the vocabulary find, each placed and counted, the trust list, and the outcome over what the
    /// counts answered.
    /// </summary>
    private static Result<Counted> Planned(Inputs inputs, SqlServer.Database database, SortedArray<Element> environment,
        Collation collation, Plan plan, SqlServer.QueryLog log)
    {
        var head = inputs.HeadModel.Elements;
        if (BlockOnPossibleDataLoss.Read(plan, database).Failed(out var checks, out var error)
            || Change.Between(environment, head, inputs.HeadModel.Renames, collation).Failed(out var own, out error)
            || Preconditions.Of(environment, head, own, BlockOnPossibleDataLoss.Sites(checks)).Failed(out var property, out error)
            || PlanStatements.Read(plan, database, Scripts(inputs.Head), Scripts(inputs.Base)).Failed(out var listing, out error))
        {
            return error;
        }

        var reading = Vocabulary.Read(listing.Plan, listing.ScriptsBefore, property, environment, head, own);
        if (Result.All(reading.Preconditions.Select(p => Coordinates.Of(p, own, environment, collation))).Failed(out var placed, out error)
            || AggregateQueries.Of(placed.OfType<Coordinates.Counted>(), head, environment, collation, SqlServer.Session.Assumed)
                .Failed(out var queries, out error)
            || Result.All(queries.Tables.Select(t => AggregateQueries.Count(database, t, log))).Failed(out var counted, out error)
            || Trust(database, environment, log).Failed(out var trust, out error))
        {
            return error;
        }

        var counts = placed.OfType<Coordinates.Answered>().Select(a => a.Count).Concat(counted.SelectMany(c => c.Counted))
            .GroupBy(c => c.Precondition).Select(g => new PreconditionCount(g.Key, g.Max(c => c.Rows), g.Max(c => c.Violating))).ToList();
        var existing = new ExistingData(SyntheticCopy.Measured(counts, counted.SelectMany(c => c.Classes)), trust);
        var unanswered = SortedArray.Of(queries.NotPredictable.Concat(counted.SelectMany(c => c.Unanswered))
            .Concat(placed.OfType<Coordinates.Declared>().Select(d => new AggregateQueries.Unanswered(d.Precondition,
                "a person declares its state, and no read-only query counts it"))));
        var answered = SortedArray.Of(reading.Preconditions.Where(p => existing.State(p) is not null));
        var sites = SortedArray.Of(BlockOnPossibleDataLoss.Sites(checks).Where(s => answered.Contains(TableEmpty(s.Table))));
        return Outcome.Predict(existing, sites, answered, Preconditions.BaselineDrift(plan.Report, head, inputs.Change, collation))
            .Map(outcome => new Counted(existing, SortedArray.Of(counts), outcome, unanswered, reading.Unclassified,
                [.. queries.Notes, .. reading.Findings]));
    }

    /// <summary>
    /// The trust state of each foreign key and check the environment holds under a name of its own, read from its catalog through
    /// SqlServer.Measure (AggregateQueries.TrustOf); those it holds disabled or untrusted, which an existing data lists.
    /// </summary>
    private static Result<SortedArray<TrustState>> Trust(SqlServer.Database database, SortedArray<Element> environment,
        SqlServer.QueryLog log) =>
        Result.All(environment.Where(e => e.Key.Type is "ForeignKeyConstraint" or "CheckConstraint")
                .Select(e => AggregateQueries.Trust(database, e.Key, log)))
            .Map(states => SortedArray.Of(states.OfType<TrustState>().Where(t => t.Disabled || t.Untrusted)));

    private static Precondition TableEmpty(Name table) => new(table, [], new PreconditionKind.TableEmpty());

    private static PlanStatements.Scripts Scripts(Ssdt.Package package) => new(package.PreDeploy, package.PostDeploy);
}
