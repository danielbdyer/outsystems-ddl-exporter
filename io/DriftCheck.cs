using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// check drift as a use case (contract C7, R3): whether a database has drifted from the repository at a ref (§1 fact 4, law 2′). Its steps,
/// each failure answered with the stamp as far as the work got: the committed DacFx, named once; the toolchain ledger's pin, with the
/// committed DacFx inside its window; the target resolved to a database; the pipeline's profile chosen; the target reached as this
/// identity with VIEW DEFINITION, so a denial arrives before anything builds, and a copy's SQL Server read; the ref built; the database
/// read once, extracted into a package; the ref's package planned against it, package to package; and the drift decided in the kernel,
/// with the provenance of the claim. A failed extract fails the whole check (VALUES.md S2, finding NFR-10).
/// </summary>
public static class DriftCheck
{
    /// <summary>What check drift is asked: the target, the ref, the profile the caller names for a copy, and the project, where the repository holds several.</summary>
    public sealed record Request(Target Target, GitRef At, string? Profile, string? Project);

    /// <summary>
    /// What check drift answers: the target and the ref; the ref's commit; the drift; the collation the target's names compare under; the
    /// provenance of the claim; the profile the plan ran under; and the notes the profile, the package, the two models and the plan raised.
    /// </summary>
    public sealed record Answer(Target Target, GitRef At, string Commit, Drift Drift, Collation Collation, Provenance Provenance, string Profile, IReadOnlyList<Finding> Notes);

    /// <summary>Where check drift runs: the estate's root, the working directory, the tool folder ESTATE_TOOL names, if any, and estate's own version, which the toolchain ledger's rows name.</summary>
    public sealed record Estate(string Root, string WorkingDirectory, string? Tool, string Version);

    /// <summary>check drift, the database read by io/DacFx.Extract.</summary>
    public static Stamped<Answer> Run(Estate estate, Request request, SqlServer.QueryLog log) => Run(estate, request, log, database => DacFx.Extract(database));

    /// <summary>check drift, the database read by <paramref name="extract"/>, as a test gives it.</summary>
    internal static Stamped<Answer> Run(Estate estate, Request request, SqlServer.QueryLog log, Func<SqlServer.Database, Result<Ssdt.Package>> extract)
    {
        if (DacFx.Version is not Result<DacFxVersion>.Ok { Value: var dacfx })
        {
            return new(null, ((Result<DacFxVersion>.Failed)DacFx.Version).Error);
        }

        var stamp = new Stamp(dacfx);
        var pinned = Doctor.Toolchain(estate.Root, estate.Version);
        if (pinned is not Result<Pin>.Ok { Value: var pin })
        {
            return new(stamp, ((Result<Pin>.Failed)pinned).Error);
        }

        stamp = stamp with { Pin = pin };
        if (pin.Rejects(dacfx) is { } outside)
        {
            return new(stamp, outside);
        }

        var posture = Posture.Environments(estate.Root);
        var reached = SqlServer.Resolve(request.Target, posture, estate.Root)
            .Bind(database => Profile(estate.Root, database, posture, request.Profile).Map(profile => (Database: database, Profile: profile)))
            .Bind(chosen => SqlServer.Reach(chosen.Database, log).Map(_ => chosen))
            .Bind(chosen => (chosen.Database is SqlServer.Copy copy ? SqlServer.ServerOf(copy, log).Map(server => (Server?)server) : Result.Ok<Server?>(null))
                .Map(server => (chosen.Database, chosen.Profile, Server: server)));
        if (reached is not Result<(SqlServer.Database Database, PublishProfile.Strict Profile, Server? Server)>.Ok { Value: var target })
        {
            return new(stamp, ((Result<(SqlServer.Database, PublishProfile.Strict, Server?)>.Failed)reached).Error);
        }

        stamp = stamp with { Server = target.Server };
        var decided = stamp;
        return new(stamp, Ssdt.Build(estate.Root, request.At.ToString(), request.Project, estate.Tool, estate.WorkingDirectory)
            .Bind(built => Ssdt.Open(built.Built.Path).Bind(package =>
            {
                using (package)
                {
                    return SqlServer.SqlCmdValues(target.Database).Bind(values => extract(target.Database).Bind(extracted =>
                    {
                        using (extracted)
                        {
                            return Decided(request, built.Commit, package, extracted, target.Database, target.Profile, values, decided);
                        }
                    }));
                }
            })));
    }

    /// <summary>The plan of the ref's package against the extracted database, the drift it shows, and the claim's provenance.</summary>
    private static Result<Answer> Decided(Request request, string commit, Ssdt.Package package, Ssdt.Package extracted, SqlServer.Database database, PublishProfile.Strict profile,
        IReadOnlyList<SqlCmdValue> values, Stamp stamp) =>
        package.Elements.Bind(source => extracted.Elements.Bind(target => DacFx.Plan(package, extracted, database.Catalog, profile, values).Bind(plan =>
            Ssdt.CollationOf(target.Elements).Bind(collation => Drift.Of(plan.Report, target.Elements, source.Elements, collation).Map(drift =>
                new Answer(request.Target, request.At, commit, drift, collation,
                    Provenance.Drift(Fingerprint.Of(target.Elements), Fingerprint.Of(plan.Report), stamp.DacFx, stamp.Server, profile.Fingerprint, request.Target, DateTimeOffset.UtcNow),
                    profile.Source, [.. profile.Notes, .. Notes(package), .. source.Notes(package.Source), .. target.Notes(request.Target.ToString()), .. plan.Notes]))))));

    /// <summary>What the ref's package says that a plan package to package leaves out: a pre-plan script, which a live deploy runs and this plan does not.</summary>
    private static IEnumerable<Finding> Notes(Ssdt.Package package) => package.PrePlan is null ? [] : [Finding.Note("package.pre-plan-script", package.Source,
        "The package carries a pre-plan script; DacFx runs it against a live target before planning, and estate plans package to package, so it does not run here.")];

    /// <summary>
    /// The pipeline's profile for the database: a named environment's own; for a copy, the one the caller names, from the estate's root, else
    /// the one profile every environment of the posture names.
    /// </summary>
    private static Result<PublishProfile.Strict> Profile(string root, SqlServer.Database database, Result<Environments> posture, string? named) =>
        database is SqlServer.EnvironmentDatabase environment ? PublishProfiles.Of(environment.Environment, root)
        : named is not null ? PublishProfiles.Load(Path.GetFullPath(Path.Combine(root, named)))
        : posture.Bind(environments => environments.SharedProfile is { } shared
            ? PublishProfiles.Load(Path.GetFullPath(Path.Combine(root, shared.ToString())))
            : new Error("arguments.missing-flag", database + " is a copy, and " + Posture.Json + " names no one profile its environments share.",
                "Name the profile to plan under with estate check drift --profile <the pipeline's .publish.xml>."));
}
