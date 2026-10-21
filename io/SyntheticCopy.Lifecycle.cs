using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// synthetic-copy check, status and down (V3_MILESTONES.md WP 3.6). check reads the synthetic copy that up built from the same inputs and
/// holds it to law 11′ in its planted form: every table holds the rows up planted, byte for byte, as each table's content hash up
/// recorded in the registry's row shows, and a table up recorded no hash of holds no row; every foreign key the copy holds trusted
/// resolves, and an orphan stands only under a key whose ForeignKey precondition the existing data answers violating; counting the copy
/// again gives the existing data's answers, every no included (step 7); and the plan from the base to the copy is empty (step 8). check
/// writes nothing to the copy. status lists this checkout's registered synthetic copies, verifies the first sixteen hexadecimal digits each
/// name carries against the full fingerprints its row records (§4 row 13), and says whether the local server holds each database; it
/// names, as a note, each database of a synthetic copy's name the server holds and this checkout's registry does not, another checkout's.
/// down drops this checkout's synthetic copies of this machine on the local server, and with --if-idle only while no dbchange process
/// holds .dbchange/synthetic-copy.lock. status and down read and drop only this checkout's registry; neither starts or stops a server.
/// </summary>
public static partial class SyntheticCopy
{
    /// <summary>
    /// What check answers: whether the copy passed; the copy, the ref, its commit and the change's commit; each table's rows and hash
    /// against the hash up recorded; each foreign key's trust and orphans; the copy's answers to the change's preconditions; the findings
    /// that fail it; and the notes.
    /// </summary>
    public sealed record Checked(bool Passed, CopyName Copy, GitRef At, string Commit, string? Head, IReadOnlyList<Rehash> Tables,
        IReadOnlyList<Resolution> References, SortedArray<PreconditionState> Answers, IReadOnlyList<Finding> Findings,
        IReadOnlyList<Finding> Notes);

    /// <summary>A table's rows and content hash now, and the hash up recorded when it built the copy, none where it recorded
    /// none.</summary>
    public sealed record Rehash(Name Table, long Rows, Fingerprint? Recorded, Fingerprint? Hash);

    /// <summary>A foreign key of the copy, the state the copy's catalog holds it in, and its rows whose parent row is absent.</summary>
    public sealed record Resolution(TrustState State, long Orphans);

    /// <summary>
    /// A synthetic copy this checkout's registry holds: its name and the server it was made on; whether its build ended; the full
    /// fingerprints its row records; whether the name's digits begin them; and whether the local server holds its database, unknown for a
    /// copy made on another server than the one this machine names now; and whether down dropped it.
    /// </summary>
    public sealed record Registered(CopyName Copy, string Server, bool Ready, Fingerprint? Base, Fingerprint? ExistingData, bool Verified,
        bool? Present, bool Dropped);

    /// <summary>What status and down answer: this checkout's synthetic copies; the findings that fail status; and the notes.</summary>
    public sealed record Listing(bool Busy, IReadOnlyList<Registered> Copies, IReadOnlyList<Finding> Findings,
        IReadOnlyList<Finding> Notes);

    /// <summary>synthetic-copy check: the copy of the inputs up would build, read under the lock and held to law 11′.</summary>
    public static Stamped<Checked> Check(Checkout checkout, Request request, SqlServer.QueryLog log) =>
        Prepare(checkout, request, log, Checking);

    /// <summary>
    /// check under the lock: the copy of these inputs as this checkout's registry and the local server hold it, R15 cleared first; a
    /// database the registry does not hold is copy.foreign, and a copy whose build did not end, or of other inputs, is
    /// copy.synthetic-absent.
    /// </summary>
    private static Stamped<Checked> Checking(Prepared prepared)
    {
        if (LocalServer.Standing(prepared.Root, prepared.Name, prepared.Server, prepared.Log).Failed(out var held, out var error))
        {
            return new(prepared.Stamp, error);
        }

        var (copy, row, exists) = held;
        if (row is null || !exists || !Ready(row) || !prepared.Inputs.All(i => (string?)row[i.Key] == (string?)i.Value))
        {
            return new(prepared.Stamp, row is null && exists ? LocalServer.Foreign(prepared.Name, prepared.ServerName)
                : new Error("copy.synthetic-absent",
                    LocalState.CopiesName + " holds no synthetic copy built to its end from these inputs on " + prepared.ServerName
                        + ": " + prepared.Name + " is the synthetic copy's name for them.",
                    "Run dbchange synthetic-copy up with the same flags, then run dbchange synthetic-copy check again."));
        }

        var reached = SqlServer.ServerOf(copy, prepared.Log).Match(s => prepared.Stamp with { Server = s }, _ => prepared.Stamp);
        return new(reached, Rechecked(prepared.Planting(copy), row, prepared.At, prepared.Commit, prepared.Head));
    }

    /// <summary>The copy held to law 11′, in four parts: its hashes, its foreign keys, step 7 and step 8.</summary>
    private static Result<Checked> Rechecked(Inputs inputs, JsonObject row, GitRef at, string commit, string? head)
    {
        var (copy, log, notes) = (inputs.Copy, inputs.Log, new List<Finding>());
        if (Read(inputs).Failed(out var read, out var error)
            || Rehashed(copy, read.Schema, Recorded(row), log).Failed(out var tables, out error)
            || Result.All(read.Schema.Tables.SelectMany(t => t.References.Select(r => Resolved(copy, t, r, log))))
                .Failed(out var references, out error)
            || (read.Asked is { } asked ? Count(copy, asked, log) : Result.Ok(SortedArray.Of<PreconditionState>([])))
                .Failed(out var answers, out error))
        {
            return error;
        }

        var kept = (inputs.Existing?.Trust ?? []).Where(t => t.Disabled).Select(t => t.Constraint).ToHashSet();
        if (Identical(inputs, kept, notes).Failed(out var differences, out error))
        {
            return error;
        }

        var schema = read.Schema;
        List<Finding> findings =
        [
            .. tables.Where(t => t.Recorded != t.Hash).Select(t => Finding.Warning("synthetic-copy.hash", t.Table.ToString(),
                t.Recorded is null
                    ? t.Table + " holds " + Number(t.Rows) + " rows, and up recorded no hash of it, since it planted and seeded none."
                    : t.Table + " holds " + Number(t.Rows) + " rows whose content hash is not the one up recorded when it planted them.",
                "Run dbchange synthetic-copy down, then dbchange synthetic-copy up, to plant the copy again.")),
            .. references.Where(r => r.Orphans > 0 && (!r.State.Disabled && !r.State.Untrusted
                    || !Held(inputs.Existing, schema, r.State.Constraint)))
                .Select(r => Finding.Warning("synthetic-copy.orphan", r.State.Constraint.ToString(),
                    r.State + " has " + Number(r.Orphans) + " rows whose parent row is absent, "
                        + (!r.State.Disabled && !r.State.Untrusted ? "though the copy holds it trusted"
                            : "and the existing data answers no ForeignKey precondition of its columns violating") + ".",
                    "Run dbchange synthetic-copy down, then dbchange synthetic-copy up, to plant the copy again.")),
            .. inputs.Existing is { } existing ? Accepted(existing, read.Asked!.Preconditions, answers) : [],
            .. differences,
        ];
        return new Checked(findings.Count == 0, copy.Name, at, commit, head, tables, references, answers, findings, notes);
    }

    /// <summary>The tables and hashes the registry's row records, as up wrote them when the copy's build ended.</summary>
    private static IReadOnlyDictionary<string, Fingerprint> Recorded(JsonObject row) =>
        (row["tables"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(t => ((string?)t["table"], Fingerprint.Parse((string?)t["hash"] ?? "")))
            .Where(t => t.Item1 is not null && t.Item2 is Result<Fingerprint>.Ok)
            .ToDictionary(t => t.Item1!, t => ((Result<Fingerprint>.Ok)t.Item2).Value, StringComparer.Ordinal);

    /// <summary>Each table of the copy: hashed where up recorded a hash, else its rows counted, which up's recording says are
    /// none.</summary>
    private static Result<IReadOnlyList<Rehash>> Rehashed(SqlServer.Copy copy, Schema schema,
        IReadOnlyDictionary<string, Fingerprint> recorded, SqlServer.QueryLog log) =>
        Hashed(copy, schema, schema.Tables.Select(t => t.Name).Where(t => recorded.ContainsKey(t.ToString())), log).Bind(hashed =>
            Result.All(schema.Tables.Where(t => !recorded.ContainsKey(t.Name.ToString())).Select(t => SqlServer.Query(copy,
                    new SqlServer.Statement(t.Name + ": rows", "SELECT COUNT_BIG(*) FROM " + t.Name + ";"), log,
                    rows => (t.Name, Rows: Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture)))))
                .Map(counted => (IReadOnlyList<Rehash>)
                [
                    .. hashed.Select(h => new Rehash(h.Key, h.Value.Rows, recorded[h.Key.ToString()], h.Value.Hash)),
                    .. counted.Where(c => c.Rows > 0).Select(c => new Rehash(c.Name, c.Rows, null, null)),
                ]))
            .Map(rehashed => (IReadOnlyList<Rehash>)[.. rehashed.OrderBy(r => r.Table)]);

    /// <summary>
    /// A foreign key of the copy: its state as the catalog holds it (AggregateQueries.Trust, through SqlServer.Measure), as the copy's
    /// schema reads it where the key is unnamed, and its rows whose every leg holds a value and whose parent row is absent.
    /// </summary>
    private static Result<Resolution> Resolved(SqlServer.Copy copy, Table table, Reference reference, SqlServer.QueryLog log) =>
        AggregateQueries.Trust(copy, reference.Key, log).Bind(state => SqlServer.Query(copy, new SqlServer.Statement(
                reference.Key + ": orphans", "SELECT COUNT_BIG(*) FROM " + table.Name + " AS c WHERE "
                    + string.Join(" AND ", reference.Legs.Select(l => "c." + l.Column + " IS NOT NULL"))
                    + " AND NOT EXISTS (SELECT 1 FROM " + reference.Parent + " AS p WHERE "
                    + string.Join(" AND ", reference.Legs.Select(l => "p." + l.ParentColumn + " = c." + l.Column)) + ");"), log,
            rows => new Resolution(state ?? new TrustState(reference.Key, reference.Disabled, false),
                Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture))));

    /// <summary>Whether the existing data answers violating a ForeignKey precondition of the key's table, parent and column
    /// pairs.</summary>
    private static bool Held(ExistingData? existing, Schema schema, ElementKey key) =>
        schema.Tables.SelectMany(t => t.References.Select(r => (Table: t, Reference: r))).FirstOrDefault(r => r.Reference.Key == key)
            is { Table: { } table, Reference: { } reference }
        && (existing?.States ?? []).Any(s => s.Violating && s.Precondition.Table == table.Name
            && s.Precondition.Kind is PreconditionKind.ForeignKey f && f.Parent == reference.Parent
            && f.Pairs.Select(p => (p.Column, p.Referenced)).Order().SequenceEqual(
                reference.Legs.Select(l => (l.Column, l.ParentColumn)).Order()));

    /// <summary>
    /// synthetic-copy status: this checkout's registered synthetic copies, each verified against its row, and present or absent on the
    /// local server, which R15 clears before it is asked; the databases of a synthetic name the registry does not hold are notes.
    /// </summary>
    public static Result<Listing> Status(Checkout checkout, SqlServer.QueryLog log) =>
        LocalServer.SyntheticRows(checkout.Root).Bind(rows => LocalServer.Server().Bind(server => LocalServer.ServerName(server)
            .Bind(now => LocalServer.SyntheticDatabases(checkout.Root, server, log).Map(held =>
            {
                var copies = rows.Select(r => Listed(r.Name, r.Row, now, held, dropped: false)).ToList();
                return new Listing(false, copies,
                    [
                        .. copies.Where(c => !c.Verified).Select(c => Finding.Warning("synthetic-copy.fingerprint", c.Copy.ToString(),
                            c.Copy + " carries fingerprints that " + LocalState.CopiesName + " does not record for it: its row records "
                                + (c.Base is { } b ? "the base " + b : "no base's fingerprint") + " and "
                                + (c.ExistingData is { } d ? "the existing data " + d : "no existing data's fingerprint") + ".",
                            "Run dbchange synthetic-copy down, then dbchange synthetic-copy up, to build the copy again.")),
                    ],
                    [.. Foreign(held, rows, now), .. Elsewhere(copies, now)]);
            }))));

    /// <summary>
    /// synthetic-copy down: under .dbchange/synthetic-copy.lock, each of this checkout's synthetic copies made on this machine on the
    /// local server it names now, dropped as Drop drops a copy, its row with it, once R15 clears the server; with
    /// <paramref name="ifIdle"/>, nothing where another dbchange process holds the lock. Where the registry holds none, nothing connects.
    /// </summary>
    public static Result<Listing> Down(Checkout checkout, bool ifIdle, SqlServer.QueryLog log)
    {
        var (root, state) = (checkout.Root, new LocalState(checkout.Root));
        if (LocalServer.SyntheticRows(root).Failed(out var seen, out var error))
        {
            return error;
        }

        if (!seen.Any(r => (string?)r.Row["machine"] == LocalServer.ThisMachine))
        {
            return new Listing(false, [], [], [.. Unheld(seen)]);
        }

        var taken = state.Made(state.Folder).Bind(_ => FileLock.Take(state.SyntheticLock, ifIdle ? TimeSpan.Zero : Wait));
        if (taken is Result<FileLock>.Failed { Error.Code: "lock.timed-out" } && ifIdle)
        {
            return new Listing(true, [], [], []);
        }

        if (taken.Failed(out var held, out error))
        {
            return error;
        }

        using (held)
        {
            return LocalServer.SyntheticRows(root).Bind(rows => LocalServer.Server().Bind(server => LocalServer.ServerName(server)
                .Bind(now => Result.All(rows.Where(r => Droppable(r.Row, now)).Select(r => LocalServer.Standing(root, r.Name, server, log)
                        .Bind(standing => LocalServer.Drop(standing.Copy, log))))
                    .Map(dropped => new Listing(false,
                        [.. rows.Select(r => Listed(r.Name, r.Row, now, null, dropped.Contains(r.Name)))], [],
                        [.. Unheld(rows), .. Elsewhere([.. rows.Where(r => (string?)r.Row["machine"] == LocalServer.ThisMachine
                            && !Droppable(r.Row, now)).Select(r => Listed(r.Name, r.Row, now, null, false))], now)])))));
        }
    }

    /// <summary>Whether down drops the row's copy: made on this machine, on the local server this machine names now.</summary>
    private static bool Droppable(JsonObject row, ServerName now) =>
        (string?)row["machine"] == LocalServer.ThisMachine && (string?)row["server"] == now.ToString();

    /// <summary>
    /// A registered copy as status and down list it: absent once dropped; else present or absent among <paramref name="held"/>, the
    /// databases the local server holds, where it was made on the server this machine names now and they were read; else unknown.
    /// </summary>
    private static Registered Listed(CopyName name, JsonObject row, ServerName now, IReadOnlyList<CopyName>? held, bool dropped)
    {
        var made = (string?)row["server"] ?? "";
        return new Registered(name, made, Ready(row), Parsed(row["base"]), Parsed(row["existingData"]), Verified(name, row),
            dropped ? false : made == now.ToString() && held is not null ? held.Contains(name) : null, dropped);
    }

    /// <summary>
    /// Whether a synthetic copy's name and its row agree (§4 row 13): the row records the full fingerprint of the base, which begins with
    /// the sixteen hexadecimal digits the name carries, and that of the existing data exactly where the name carries its digits.
    /// </summary>
    internal static bool Verified(CopyName name, JsonObject row) =>
        Parsed(row["base"]) is { } @base && @base.ToString()[..16] == name.Base
        && (Parsed(row["existingData"]) is { } data ? data.ToString()[..16] == name.ExistingData
            : name.ExistingData is null && row["existingData"] is null);

    private static Fingerprint? Parsed(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && Fingerprint.Parse(text) is Result<Fingerprint>.Ok(var parsed)
            ? parsed : null;

    /// <summary>
    /// copy.foreign, as a note: each database of a synthetic copy's name the local server holds that this checkout's registry does not,
    /// which another checkout made; status reads it by its name alone, and down never drops it.
    /// </summary>
    private static IEnumerable<Finding> Foreign(IReadOnlyList<CopyName> held, IReadOnlyList<(CopyName Name, JsonObject Row)> rows,
        ServerName now) =>
        held.Where(name => !rows.Any(r => r.Name == name && (string?)r.Row["server"] == now.ToString())).Select(name => Finding.Note(
            "copy.foreign", name.ToString(), now + " holds the database " + name + ", a synthetic copy's name that " + LocalState.CopiesName
                + " of this checkout does not hold: another checkout made it, and status reads and down drops only this checkout's"
                + " copies."));

    /// <summary>A note for each registered copy made on another server than the one this machine names now, which down leaves.</summary>
    private static IEnumerable<Finding> Elsewhere(IEnumerable<Registered> copies, ServerName now) =>
        copies.Where(c => c.Server != now.ToString()).Select(c => Finding.Note("synthetic-copy.elsewhere", c.Copy.ToString(),
            c.Copy + " was made on " + c.Server + ", and the local server this machine names now is " + now
                + ", so status cannot say whether the database stands and down leaves it; set DBCHANGE_SQL back to that server to"
                + " drop it."));

    /// <summary>A note for each registered copy another machine made, which down leaves.</summary>
    private static IEnumerable<Finding> Unheld(IEnumerable<(CopyName Name, JsonObject Row)> rows) =>
        rows.Where(r => (string?)r.Row["machine"] != LocalServer.ThisMachine).Select(r => Finding.Note("synthetic-copy.elsewhere",
            r.Name.ToString(), r.Name + " was made on the machine " + ((string?)r.Row["machine"] ?? "that the row does not name")
                + ", and down drops only this machine's copies."));
}
