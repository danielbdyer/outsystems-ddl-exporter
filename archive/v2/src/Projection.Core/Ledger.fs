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
///     is the stored fingerprint against a **recomputation from the live
///     source** — equality admits, disagreement is drift, and drift refuses
///     by name (`transfer.resume.sourceDrift`'s shape, generalized), never
///     a silent re-run over changed data.
/// A snapshot-chain instance whose resume check is ordinal monotonicity
/// (the episode store) says so honestly — it verifies chain STRUCTURE, not
/// the write witness (see `LifecycleStore`, card L3).
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

/// The pure chain algebra of one ledger grain — record-of-functions, per
/// the house preference for data over dispatch. `Apply` is ⊕ at this
/// grain; `Genesis` its unit; `FingerprintOf` what write-time stamps and
/// resume-time recomputes. The journal (chunk grain), the episode store
/// (episode grain), and the G10 progress marker (the degenerate
/// single-entry grain) are its instances.
type LedgerSpec<'state, 'entry, 'fp when 'fp : equality> =
    {
        Genesis       : 'state
        Apply         : 'state -> 'entry -> 'state
        FingerprintOf : 'entry -> 'fp
    }

[<RequireQualifiedAccess>]
module Verified =

    /// Read the admitted entry out of its token.
    let value (Verified entry) : 'entry = entry

[<RequireQualifiedAccess>]
module Ledger =

    /// Stamp a new entry into chain form at write time: the position and
    /// the spec's fingerprint, recorded beside the entry so resume can
    /// recompute against it.
    let entryOf (spec: LedgerSpec<'s, 'e, 'fp>) (position: int) (entry: 'e) : LedgerEntry<'e, 'fp> =
        { Position = position; Fingerprint = spec.FingerprintOf entry; Entry = entry }

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
