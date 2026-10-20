using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// The synthetic copy (V3_MILESTONES.md WP 3.5; the Facsimile's copy build, its ARCHITECTURE.md §5, steps 1 to 8), synthetic-copy up as a
/// use case. Step 1: the repository built at the ref; the base's fingerprint taken over the package's elements, the pipeline's profile,
/// DacFx and the local server; the copy named from it and from the existing data's fingerprint (CopyName.Synthetic, §4 row 13); and, under
/// .dbchange/synthetic-copy.lock, a copy the registry holds of the same inputs, built to its end, answered as current, with nothing
/// applied, one of other inputs or left half built dropped, and a fresh copy restored from the base's backup in the copy cache, or
/// published under the pipeline's profile and backed up (LocalServer.FromBase), so the post-deploy seeds run as in every environment; on a
/// fresh database the profile's DropObjectsNotInSource has nothing to drop. With an existing data the change is the one from the ref to
/// the checkout's HEAD, the change's branch, built too; the declared witnesses, dbchange/witnesses.sql at HEAD, are read before anything
/// connects (io/Witnesses). Steps 2 to 8 are SyntheticCopy.Plant.cs's and SyntheticCopy.Check.cs's. The copy
/// carries no state table: its inputs are its name and its registry row, so its schema is the repository's and check drift
/// --target synthetic-copy is its health check. The base is never published again once a row is planted.
/// </summary>
public static partial class SyntheticCopy
{
    /// <summary>How long up waits for another dbchange process's up in this checkout: a base published from scratch, many times
    /// over.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(30);

    /// <summary>What up is asked: the ref the copy is built at, the existing data's file, the profile and the project, where
    /// named.</summary>
    public sealed record Request(GitRef At, string? ExistingData, string? Profile, string? Project);

    /// <summary>How up ended: the copy built; the copy of these inputs up already; rows only a person can write, or a copy whose answers
    /// differ from the environment's; or a copy whose plan from the base holds operations.</summary>
    public enum Kind
    {
        Built,
        Current,
        NeedsInput,
        Differs,
    }

    /// <summary>
    /// What up answers: how it ended; the copy's name; the ref, its commit and the change's commit, where an existing data is planted;
    /// whether the base was restored from the cache; each table's planted and skipped rows; each touched or seeded table's rows and hash;
    /// the copy's own answers to the change's preconditions; the findings that decide the exit; and the notes.
    /// </summary>
    public sealed record Answer(Kind Kind, CopyName Copy, GitRef At, string Commit, string? Head, bool Restored,
        IReadOnlyList<Planting> Planted, IReadOnlyList<Hash> Hashes, SortedArray<PreconditionState> Answers,
        IReadOnlyList<Finding> Findings, IReadOnlyList<Finding> Notes);

    /// <summary>A table's rows the plan asked for: planted, and skipped since the copy already answered every purpose of the row.</summary>
    public sealed record Planting(Name Table, int Planted, int Skipped);

    /// <summary>A table's rows and content hash as restored, none where it held no row, and after the last step.</summary>
    public sealed record Hash(Name Table, long RowsBefore, Fingerprint? Before, long Rows, Fingerprint After);

    /// <summary>
    /// What steps 2 to 8 read: the copy and its log; the base package; the profile; the existing data, none for presence rows; the change's
    /// package, with it; and the declared witnesses' blocks at HEAD, none where the file is absent.
    /// </summary>
    private sealed record Inputs(SqlServer.Copy Copy, SqlServer.QueryLog Log, Ssdt.Package Base, PublishProfile.Strict Profile,
        ExistingData? Existing, Ssdt.Package? Head, IReadOnlyList<Witnesses.Block> Blocks);

    /// <summary>synthetic-copy up: steps 1 to 8, the answer stamped with DacFx, the pin and, once the copy is reached, its SQL Server.
    /// </summary>
    public static Stamped<Answer> Up(Checkout checkout, Request request, SqlServer.QueryLog log)
    {
        var standing = Standing.Of(checkout);
        if (standing.Result is Result<Pin>.Failed { Error: var refused })
        {
            return new(standing.Stamp, refused);
        }

        var (stamp, root) = (standing.Stamp!, checkout.Root);
        if ((request.ExistingData is { } file
                ? ExistingDataFile.Read(Path.GetFullPath(Path.Combine(checkout.WorkingDirectory, file))).Map(e => (ExistingData?)e)
                : Result.Ok<ExistingData?>(null))
            .Bind(existing => LocalServer.Server().Bind(server => LocalServer.ServerName(server)
                .Bind(name => Profile(root, request.Profile).Map(profile => (existing, server, name, profile)))))
            .Failed(out var asked, out var error))
        {
            return new(stamp, error);
        }

        if (Ssdt.Build(root, request.At.ToString(), request.Project, checkout.Tool, checkout.WorkingDirectory)
                .Failed(out var @base, out error)
            || Ssdt.Open(@base.Built.Path).Failed(out var basePackage, out error))
        {
            return new(stamp, error);
        }

        using (basePackage)
        {
            var head = asked.existing is null ? Result.Ok<(Ssdt.Built Built, string Commit)?>(null)
                : Ssdt.Build(root, "HEAD", request.Project, checkout.Tool, checkout.WorkingDirectory)
                    .Map(h => ((Ssdt.Built Built, string Commit)?)h);
            if (head.Failed(out var change, out error) || Git.At(root, "HEAD").Failed(out var at, out error)
                || basePackage.Elements.Failed(out var baseModel, out error)
                || (change is { } built ? Ssdt.Open(built.Built.Path).Map(p => (Ssdt.Package?)p) : Result.Ok<Ssdt.Package?>(null))
                    .Failed(out var headPackage, out error))
            {
                return new(stamp, error);
            }

            using (headPackage)
            {
                if ((headPackage?.Elements.Map(m => m.Elements) ?? Result.Ok(SortedArray.Of<Element>([])))
                    .Failed(out var headModel, out error))
                {
                    return new(stamp, error);
                }

                var witnessFile = Path.Combine(at.Path, Witnesses.Path);
                var witnesses = File.Exists(witnessFile) ? File.ReadAllText(witnessFile) : null;
                if ((witnesses is null ? Result.Ok<IReadOnlyList<Witnesses.Block>>([])
                        : Witnesses.Parse(witnesses, Witnesses.Literals(baseModel.Elements.Concat(headModel))))
                    .Failed(out var blocks, out error))
                {
                    return new(stamp, error);
                }

                var baseFingerprint = Fingerprint.Of("package " + Fingerprint.Of(baseModel.Elements) + "\nprofile "
                    + asked.profile.Fingerprint + "\ndacfx " + stamp.DacFx + "\nserver " + asked.name);
                var existingFingerprint = asked.existing is { } existing ? ExistingDataFile.FingerprintOf(existing) : (Fingerprint?)null;
                var inputs = new JsonObject
                {
                    ["base"] = baseFingerprint.ToString(), ["existingData"] = existingFingerprint?.ToString(),
                    ["at"] = request.At.ToString(), ["commit"] = @base.Commit, ["head"] = change?.Commit,
                    ["witnesses"] = witnesses is null ? null : Fingerprint.Of(witnesses).ToString(), ["version"] = checkout.Version,
                };
                var name = CopyName.Synthetic(baseFingerprint, existingFingerprint);
                var state = new LocalState(root);
                if (state.Made(state.Folder).Bind(_ => FileLock.Take(state.SyntheticLock, Wait)).Failed(out var held, out error))
                {
                    return new(stamp, error);
                }

                using (held)
                {
                    return Locked(root, asked.server, asked.name, name, baseFingerprint, inputs, log, stamp, @base.Built.Path,
                        copy => new Inputs(copy, log, basePackage, asked.profile, asked.existing, headPackage, blocks),
                        request.At, @base.Commit, change?.Commit);
                }
            }
        }
    }

    /// <summary>
    /// Step 1 under the lock: the registry's row of the copy read, and its database looked for; a copy of the same inputs built to its end
    /// answered as current; a row of other inputs, or of a build that did not end, dropped with its database; a database the registry does
    /// not hold refused as another checkout's (copy.foreign); and a fresh copy made from the base, its inputs set on its row, planted, and
    /// set ready where it was built to its end.
    /// </summary>
    private static Stamped<Answer> Locked(string root, string server, ServerName serverName, CopyName name, Fingerprint baseFingerprint,
        JsonObject inputs, SqlServer.QueryLog log, Stamp stamp, string dacpac, Func<SqlServer.Copy, Inputs> planting, GitRef at,
        string commit, string? head)
    {
        if (LocalServer.Standing(root, name, server, serverName, log).Failed(out var held, out var error))
        {
            return new(stamp, error);
        }

        var (standing, row, exists) = held;
        if (row is not null && exists && Ready(row) && inputs.All(i => (string?)row[i.Key] == (string?)i.Value))
        {
            return new(SqlServer.ServerOf(standing, log).Match(s => stamp with { Server = s }, _ => stamp),
                new Answer(Kind.Current, name, at, commit, head, false, [], [], [], [], []));
        }

        if ((row is not null ? LocalServer.Drop(standing, log).Map(_ => standing)
                : exists ? LocalServer.Foreign(name, serverName) : Result.Ok(standing))
            .Bind(_ => LocalServer.FromBase(root, server, baseFingerprint, copy => copy.Publish(dacpac, planting(copy).Profile, log), log,
                copyName: name))
            .Bind(fresh => LocalServer.Mark(fresh.Copy, Ready(inputs, false)).Map(_ => fresh))
            .Failed(out var made, out error))
        {
            return new(stamp, error);
        }

        var reached = SqlServer.ServerOf(made.Copy, log).Match(s => stamp with { Server = s }, _ => stamp);
        return new(reached, Steps(planting(made.Copy), at, commit, head, made.Restored, made.Notes)
            .Bind(answer => answer.Kind == Kind.Built ? LocalServer.Mark(made.Copy, new JsonObject { ["ready"] = true }).Map(_ => answer)
                : Result.Ok(answer)));
    }

    /// <summary>The inputs with the row's ready flag set as given.</summary>
    private static JsonObject Ready(JsonObject inputs, bool ready)
    {
        var row = (JsonObject)inputs.DeepClone();
        row["ready"] = ready;
        return row;
    }

    private static bool Ready(JsonObject row) => row["ready"] is JsonValue ready && ready.TryGetValue(out bool built) && built;

    /// <summary>
    /// The pipeline's profile for the synthetic copy, as check drift chooses one for a copy: the one the caller names, from the repository
    /// root, else the one profile every environment of the environments file names.
    /// </summary>
    private static Result<PublishProfile.Strict> Profile(string root, string? named) =>
        named is not null ? PublishProfiles.Load(Path.GetFullPath(Path.Combine(root, named)))
        : EnvironmentsFile.Read(root).Bind(environments => environments.SharedProfile is { } shared
            ? PublishProfiles.Load(Path.GetFullPath(Path.Combine(root, shared.ToString())))
            : new Error("arguments.missing-flag",
                "The synthetic copy is published under the pipeline's profile, and " + EnvironmentsFile.Json
                    + " names no one profile its environments share.",
                "Name the profile with dbchange synthetic-copy up --profile <the pipeline's .publish.xml>."));
}
