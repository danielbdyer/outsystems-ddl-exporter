namespace Projection.Core

/// The ledger contract — R3 (`CONSTELLATION.md` §9.2, corrected per
/// `CONSTELLATION_BACKLOG.md` RI-3). The engine's two durable ledgers — the
/// capture journal (a partial-sum ledger of chunk quanta) and the episode
/// store (a snapshot chain with derivable displacements) — converged on one
/// discipline independently: append-only, admission-guarded, FTC-related.
/// This file names that discipline once, in Core, pure: the instances adapt
/// at the boundary (the journal's effectful remap fold, the store's JSON
/// home) while the chain algebra and the admission split live here.
///
/// **The admission split (RI-3).** One `FingerprintOf : 'entry -> 'fp`
/// cannot honestly cover both grains, because the two ledgers verify
/// DIFFERENT things at different times:
///   - `WriteAdmit` — at write time, an entry may demand an **external
///     witness** (the episode grain's B′≡B round-trip; the journal grain's
///     completed sink statement). The witness is checked once, when it is
///     checkable, and the `Verified<_>` token records that it held.
///   - `ResumeAdmit` — at resume time, the external witness is gone (no
///     B′ to re-deploy; no completed write to observe). What CAN be checked
///     is the grain's declared `ChainAdmission` discipline — a recomputation
///     from the live source (drift refuses by name), ordinal monotonicity, or
///     grouped predecessor linkage — never a silent re-run over changed data.
/// **The discipline is a VALUE on the spec (`ChainAdmission`, align-III.2),
/// not a `FingerprintOf` a grain can quietly make recompute to itself** — the
/// tautology the sink journal carried until a5-F* named it. A snapshot-chain
/// instance whose resume check is ordinal monotonicity (the episode store)
/// declares `Monotone` and says so honestly — it verifies chain STRUCTURE,
/// not the write witness (see `LifecycleStore`, card L3); the sink journal
/// declares `Linkage` and verifies its `PrevSyncId` chain for real.
type LedgerEntry<'entry, 'fp> =
    {
        /// The entry's position in the chain (chunk index; episode ordinal).
        Position    : int
        /// The fingerprint recorded AT WRITE TIME — what `ResumeAdmit`
        /// recomputes against. For the journal: first/last source PK + raw
        /// count. For the episode store: the monotone coordinate ordinal.
        Fingerprint : 'fp
        Entry       : 'entry
    }

/// A ledger entry that passed its grain's admission check. Minted ONLY by
/// `Ledger.writeAdmit` / `Ledger.resumeAdmit` — the private constructor is
/// the admission law (the `ArtifactByKind` proof-token idiom): a
/// `Verified<'entry>` cannot exist unless an admission arm held when it
/// was constructed. `Ledger.replay` folds over verified entries only, so
/// an unadmitted entry structurally cannot reach the partial sums.
type Verified<'entry> = private Verified of 'entry

/// A recorded fingerprint disagreeing with its recomputation — the named
/// drift at one position. Core keeps this typed and instance-neutral; each
/// instance maps it onto its own named refusal (the journal:
/// `transfer.resume.sourceDrift`).
type LedgerDrift<'fp> =
    {
        Position   : int
        Recorded   : 'fp
        Recomputed : 'fp
    }

/// How a ledger grain admits its stored chain at RESUME — the discipline
/// declared once on the spec, as a value `Ledger.admitChain` inspects, never
/// a tautology hidden in a fingerprint that recomputes to the very value it
/// is compared against. (The sink journal carried exactly that tautology:
/// `FingerprintOf = line.SyncId`, so `resumeAdmit line.SyncId …` compared
/// `SyncId = SyncId` — always `Ok`, the drift arm unreachable, the real
/// check a hand guard beside it. align-III.2 retires it.) The three arms are
/// the three disciplines the engine's real ledgers operate:
///   - **`WitnessRecompute`** — the fingerprint is recomputed AT THE BOUNDARY
///     from the live source and compared to the recorded one (the capture
///     journal: first/last PK + row count from the ReadSide stream). Only
///     this arm needs I/O, so only it admits per-entry via `resumeAdmit`; a
///     recompute chain CANNOT be admitted offline (`admitChain` refuses it by
///     name). The projection it carries is the write-time stamp.
///   - **`Monotone`** — each entry's ordinal strictly exceeds the prior; a
///     snapshot chain re-derives its states, so structural order is the whole
///     check (the episode store — card L3's honest ResumeAdmit).
///   - **`Linkage`** — GROUPED linkage: entries sharing an identity are one
///     group; group N+1 must name group N's identity as its predecessor and
///     strictly exceed it, and the first group claims no predecessor (the
///     sink journal: a sync's lines share `SyncId` + `PrevSyncId`, and each
///     sync must link to the one before). A broken link refuses by name.
[<RequireQualifiedAccess>]
type ChainAdmission<'entry, 'fp when 'fp : comparison> =
    | WitnessRecompute of fingerprintOf: ('entry -> 'fp)
    | Monotone of ordinalOf: ('entry -> 'fp)
    | Linkage of identityOf: ('entry -> 'fp) * predecessorOf: ('entry -> 'fp option)

/// A chain admission's named refusal — the drift `Ledger.admitChain` returns.
/// Core keeps it typed and instance-neutral; each grain maps it onto its own
/// named refusal (the sink journal: `sink.journal.syncRegression` /
/// `sink.journal.brokenChain`).
[<RequireQualifiedAccess>]
type ChainRefusal<'fp> =
    /// A `Monotone`/`Linkage` chain went backward: the entry at `position`
    /// carries `ordinal`, not strictly above the prior group's `priorOrdinal`.
    | OrdinalRegression of position: int * ordinal: 'fp * priorOrdinal: 'fp
    /// A `Linkage` chain broke: the entry at `position` claims predecessor
    /// `claimed`, but the established predecessor is `expected`.
    | BrokenLink of position: int * claimed: 'fp option * expected: 'fp option
    /// A `WitnessRecompute` chain cannot be admitted offline — its
    /// fingerprints recompute from the live source, per entry (`resumeAdmit`).
    | RecomputeRequiresSource

/// The pure chain algebra of one ledger grain — record-of-functions, per
/// the house preference for data over dispatch. `Apply` is ⊕ at this
/// grain; `Genesis` its unit; `Admission` the RESUME discipline (declared,
/// never faked — align-III.2). The journal (chunk grain), the episode store
/// (episode grain), the sink journal (acquisition grain), and the G10
/// progress marker (the degenerate single-entry grain) are its instances.
type LedgerSpec<'state, 'entry, 'fp when 'fp : comparison> =
    {
        Genesis   : 'state
        Apply     : 'state -> 'entry -> 'state
        Admission : ChainAdmission<'entry, 'fp>
    }

[<RequireQualifiedAccess>]
module Verified =

    /// Read the admitted entry out of its token.
    let value (Verified entry) : 'entry = entry

[<RequireQualifiedAccess>]
module Ledger =

    /// Stamp an entry into chain form at write time (the `WitnessRecompute`
    /// grains' recorded fingerprint, recomputed against at resume).
    /// `fingerprintOf` is the grain's projection — the same function its
    /// `ChainAdmission.WitnessRecompute` carries; taken directly, so the
    /// stamp stays a pure position + fingerprint pairing independent of the
    /// resume discipline.
    let entryOf (fingerprintOf: 'e -> 'fp) (position: int) (entry: 'e) : LedgerEntry<'e, 'fp> =
        { Position = position; Fingerprint = fingerprintOf entry; Entry = entry }

    /// **WriteAdmit** — the external-witness arm. The grain's verifier
    /// decides (`'err` stays the grain's own error algebra); a passing
    /// witness mints the token. The witness is the part that cannot be
    /// re-run later — B′≡B needs the deployed B′; the journal's commit
    /// point needs the write to have happened — which is exactly why the
    /// token exists: it carries "the witness held" forward in the type.
    let writeAdmit (witness: 'entry -> Result<unit, 'err>) (entry: 'entry) : Result<Verified<'entry>, 'err> =
        match witness entry with
        | Ok () -> Ok (Verified entry)
        | Error e -> Error e

    /// **ResumeAdmit** — the recomputation arm. The caller recomputes the
    /// fingerprint **from the live source** (that recomputation is the
    /// instance's I/O and happens at the boundary; Core compares values).
    /// Equality admits the recorded entry; disagreement is the named
    /// drift — never a silent re-run.
    let resumeAdmit (recomputed: 'fp) (recorded: LedgerEntry<'e, 'fp>) : Result<Verified<'e>, LedgerDrift<'fp>> =
        if recomputed = recorded.Fingerprint then
            Ok (Verified recorded.Entry)
        else
            Error { Position = recorded.Position; Recorded = recorded.Fingerprint; Recomputed = recomputed }

    /// Admit a WHOLE stored chain at resume by the spec's declared
    /// discipline — the pure counterpart to per-entry `resumeAdmit`, and the
    /// admission path for the grains that reload their chain from at-rest
    /// storage (the sink journal, the episode store). A `Monotone` chain must
    /// strictly increase; a `Linkage` chain must have each sync group name the
    /// prior group as its predecessor and strictly exceed it, the first group
    /// claiming none; a `WitnessRecompute` chain CANNOT be admitted offline
    /// (its fingerprints recompute from the live source — that is
    /// `resumeAdmit`, per entry, at the boundary). Only an admitted chain
    /// yields `Verified` tokens, so `replay` over the result is structurally
    /// sound — and the refusal is the typed `ChainRefusal`, never a silent
    /// re-run over a broken chain.
    let admitChain (spec: LedgerSpec<'s, 'e, 'fp>) (entries: 'e list) : Result<Verified<'e> list, ChainRefusal<'fp>> =
        match spec.Admission with
        | ChainAdmission.WitnessRecompute _ -> Error ChainRefusal.RecomputeRequiresSource
        | ChainAdmission.Monotone ordinalOf ->
            let rec loop pos prior acc rest =
                match rest with
                | [] -> Ok (List.rev acc)
                | entry :: tail ->
                    let o = ordinalOf entry
                    match prior with
                    | Some p when o <= p -> Error (ChainRefusal.OrdinalRegression (pos, o, p))
                    | _ -> loop (pos + 1) (Some o) (Verified entry :: acc) tail
            loop 0 None [] entries
        | ChainAdmission.Linkage (identityOf, predecessorOf) ->
            // GROUPED linkage: entries sharing an identity are one group.
            // `currentId` is the group being read; `priorDistinct` is the
            // group before it (the predecessor every line in `currentId`
            // must name).
            let rec loop pos priorDistinct currentId acc rest =
                match rest with
                | [] -> Ok (List.rev acc)
                | entry :: tail ->
                    let id = identityOf entry
                    let pred = predecessorOf entry
                    match currentId with
                    | Some cur when id = cur ->
                        // a continuation line of the current group — it carries
                        // the group's own predecessor claim, re-verified so an
                        // interior inconsistency cannot slip through.
                        if pred <> priorDistinct then Error (ChainRefusal.BrokenLink (pos, pred, priorDistinct))
                        else loop (pos + 1) priorDistinct currentId (Verified entry :: acc) tail
                    | Some cur ->
                        // a new group: strictly above the prior group AND naming
                        // it as the predecessor.
                        if id <= cur then Error (ChainRefusal.OrdinalRegression (pos, id, cur))
                        elif pred <> Some cur then Error (ChainRefusal.BrokenLink (pos, pred, Some cur))
                        else loop (pos + 1) (Some cur) (Some id) (Verified entry :: acc) tail
                    | None ->
                        // the first group claims no predecessor.
                        if pred <> None then Error (ChainRefusal.BrokenLink (pos, pred, None))
                        else loop (pos + 1) None (Some id) (Verified entry :: acc) tail
            loop 0 None None [] entries

    /// The FTC at this grain (`CONSTELLATION.md` §5.1): fold ⊕ over
    /// verified entries from genesis — the partial sums reconstruct the
    /// state. Total over its input by construction: only admitted entries
    /// can appear in the list.
    let replay (spec: LedgerSpec<'s, 'e, 'fp>) (entries: Verified<'e> list) : 's =
        entries |> List.fold (fun state (Verified entry) -> spec.Apply state entry) spec.Genesis

    /// Resume = the first position absent from the chain. A gapless chain
    /// 0..k−1 resumes at k; a gap resumes at the gap (the chain is an
    /// index, not a prefix — a last-write-wins journal may hold positions
    /// past a crash hole, and those entries still admit individually via
    /// `resumeAdmit` when the live walk reaches them).
    let resumePoint (recorded: LedgerEntry<'e, 'fp> list) : int =
        let positions = recorded |> List.map (fun r -> r.Position) |> Set.ofList
        let rec firstAbsent candidate =
            if Set.contains candidate positions then firstAbsent (candidate + 1) else candidate
        firstAbsent 0
