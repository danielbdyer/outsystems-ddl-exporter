using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// classify as a use case (WP 2.5): the change from the project at a ref to the project as the working tree holds it, classified by
/// kernel Classify against the operations' recognize: blocks, with the tables dbchange/ledgers/cdc-tracked.md names and the rows the
/// working tree's pre-deploy script modifies. With an environment, the working tree's package is planned against the environment's
/// database, read once by DacFx.Extract as the identity its connection names, so a rebuild of a tracked table in that plan is refused
/// too, and the ledger's rows are that environment's alone. Nothing is published, and no aggregate query runs.
/// </summary>
public static class Classifier
{
    /// <summary>What classify is asked: the ref the change starts from, the environment to plan against or none, the project where the
    /// repository holds several, and the folder of the recognize: blocks.</summary>
    public sealed record Request(GitRef At, EnvironmentName? Environment, string? Project, string Patterns);

    /// <summary>
    /// What classify answers: the ref and its commit; the environment planned against, or none; the classification; the tables the ledger
    /// names for it; and the notes the two models, the profile and the plan raised.
    /// </summary>
    public sealed record Answer(GitRef At, string Commit, Target? Environment, ChangeClassification Classified, SortedArray<Name> Tracked,
        IReadOnlyList<Finding> Notes);

    /// <summary>An environment reached: its database, the pipeline's profile for it, and what this identity reads there.</summary>
    private sealed record Reached(SqlServer.Database Database, PublishProfile.Strict Profile, SqlServer.Readable Readable);

    /// <summary>
    /// classify: the standing; the recognize: blocks and the ledger read, then the environment reached, so a malformed input or a denied
    /// login is refused before anything builds; the ref built; the working tree's project built; and the change classified.
    /// </summary>
    public static Stamped<Answer> Run(Checkout checkout, Request request, SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var refused })
        {
            return new(standing.Stamp, refused);
        }

        Target? target = request.Environment is { } name ? new Target.Environment(name) : null;
        return new(standing.Stamp, Patterns.Read(request.Patterns).Bind(patterns => CdcLedger.Read(checkout.Root).Bind(rows =>
            Reach(checkout, target, log).Bind(reached =>
            Ssdt.Build(checkout.Root, request.At.ToString(), request.Project, checkout.Tool, checkout.WorkingDirectory).Bind(based =>
            Head(checkout, request.Project).Bind(head =>
            Opened(based.Built.Path, before => Opened(head.Path, after =>
            {
                var tracked = SortedArray.Of(rows.Where(r => request.Environment is not { } only || r.Environment == only)
                    .Select(r => r.Table).Distinct());
                return Classified(before, after, patterns, tracked, reached).Map(classified =>
                    new Answer(request.At, based.Commit, target, classified.Classification, tracked,
                    [
                        .. classified.Notes,
                        .. reached is null && tracked.Count > 0
                            ? new[]
                            {
                                Finding.Warning("classify.no-plan", "dbchange classify", "No environment was named, so no deploy"
                                    + " plan was read, and a rebuild of a table " + CdcLedger.File + " names is not checked.",
                                    "Run dbchange classify --env <environment> to plan the change against that environment."),
                            }
                            : [],
                    ]));
            }))))))));
    }

    /// <summary>The environment resolved, its profile chosen and VIEW DEFINITION there found, or null where none is named.</summary>
    private static Result<Reached?> Reach(Checkout checkout, Target? target, SqlServer.QueryLog log) => target is null
        ? Result.Ok<Reached?>(null)
        : SqlServer.Resolve(target, checkout.Root).Bind(database => (database is SqlServer.EnvironmentDatabase environment
                ? PublishProfiles.Of(environment.Environment, checkout.Root)
                : new Error("internal.unexpected", target + " resolved to no environment's database.",
                    "Report this answer and the command that produced it to dbchange's maintainers."))
            .Bind(profile => SqlServer.Reach(database, log).Map(readable => (Reached?)new Reached(database, profile, readable))));

    /// <summary>The project as the working tree holds it, found from the top of the working tree, built under .dbchange/build/.</summary>
    private static Result<Ssdt.Built> Head(Checkout checkout, string? project) =>
        Git.TopLevel(checkout.Root).Bind(top => Ssdt.Project(top, project).Bind(file =>
            Ssdt.Tool(AppContext.BaseDirectory, checkout.Tool, checkout.WorkingDirectory).Bind(tool =>
                Ssdt.Build(Path.Combine(top, file), tool, new LocalState(checkout.Root).Builds))));

    /// <summary>
    /// The change between the two packages' models, with the renames both refactorlogs record, names compared under the ref's collation,
    /// classified with the rows the working tree's pre-deploy script modifies and, where an environment was reached, the rebuilds of the
    /// working tree's plan against it.
    /// </summary>
    private static Result<(ChangeClassification Classification, IReadOnlyList<Finding> Notes)> Classified(Ssdt.Package before,
        Ssdt.Package after, SortedArray<Pattern> patterns, SortedArray<Name> tracked, Reached? reached) =>
        before.Elements.Bind(was => after.Elements.Bind(now => Ssdt.CollationOf(was.Elements).Bind(collation =>
            Change.Between(was.Elements, now.Elements, SortedArray.Of(was.Renames.Concat(now.Renames).Distinct()), collation)
                .Bind(change => PlanStatements.RowsModified(after.PreDeploy).Bind(preDeploy => Rebuilds(after, reached)
                .Map(plan => (Classify.Of(was.Elements, now.Elements, change, patterns,
                        new Classify.Inputs(tracked, plan.Rebuilds, preDeploy, collation)),
                    (IReadOnlyList<Finding>)[.. was.Notes(before.Source), .. now.Notes(after.Source), .. plan.Notes])))))));

    /// <summary>
    /// The tables the working tree's plan against the environment rebuilds (DacFx's TableRebuild), and the notes the profile, the
    /// identity's reach and the plan raised; none where no environment was reached.
    /// </summary>
    private static Result<(SortedArray<ScriptStatement> Rebuilds, IReadOnlyList<Finding> Notes)> Rebuilds(Ssdt.Package after,
        Reached? reached) => reached is null ? Result.Ok<(SortedArray<ScriptStatement>, IReadOnlyList<Finding>)>(([], []))
        : SqlServer.SqlCmdValues(reached.Database).Bind(values => DacFx.Extract(reached.Database).Bind(extracted =>
        {
            using (extracted)
            {
                return DacFx.Plan(after, extracted, reached.Readable.Database.Catalog, reached.Profile, values).Map(plan =>
                    (SortedArray.Of(plan.Report.Operations.Where(o => o.Kind is PlanOperationKind.TableRebuild && o.Key.Type == "Table")
                        .Select(o => (ScriptStatement)new ScriptStatement.Rebuilds(o.Key.Name))),
                    (IReadOnlyList<Finding>)[.. reached.Profile.Notes, .. reached.Readable.Notes, .. plan.Notes]));
            }
        }));

    /// <summary>A package opened, used and released.</summary>
    private static Result<T> Opened<T>(string dacpac, Func<Ssdt.Package, Result<T>> use) => Ssdt.Open(dacpac).Bind(package =>
    {
        using (package)
        {
            return use(package);
        }
    });
}
