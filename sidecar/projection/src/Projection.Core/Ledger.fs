namespace Projection.Core

/// R3 — the partial-sum ledger contract (`CONSTELLATION.md` §9.2; backlog
/// card L1, corrected per RI-3). The shared shape of the engine's two
/// durable ledgers — `CaptureJournal` (the chunk-grain quantum ledger) and
/// `LifecycleStore` (the episode-grain snapshot chain) — extracted as one
/// pure algebra: append-only entries, fingerprint-guarded admission,
/// replay = fold, drift = a named refusal. Per no-I/O-in-Core, this file
/// owns only the chain algebra; the boundary instances own append.
///
/// RI-3's correction is the admission split. The two ledgers admit
/// DIFFERENTLY at write time and at resume time, and the first edition's
/// single `FingerprintOf` could not honestly cover both:
///   - **WriteAdmit** may demand an EXTERNAL witness (the episode store's
///     B′≡B verification cannot be recomputed from the quantum alone) —
///     it mints the `Verified<_>` proof token, differently per grain.
///   - **ResumeAdmit** is fingerprint RECOMPUTATION against the live
///     source (the journal's first/last-PK × count check) — drift refuses
///     by name (`transfer.resume.sourceDrift`'s shape, generalized),
///     never a silent re-run over changed data.
///
/// Record-of-functions, not an interface — the house prefers data over
/// dispatch (object expressions stay deferred).
type LedgerSpec<'state, 'quantum, 'fp when 'fp : equality> =
    { /// The chain's empty state — what a fresh run folds from.
      Genesis       : 'state
      /// ⊕ at this grain: one quantum's contribution to the partial sum.
      Apply         : 'state -> 'quantum -> 'state
      /// What ResumeAdmit recomputes and compares.
      FingerprintOf : 'quantum -> 'fp }

/// One recorded entry of the chain: a position, the fingerprint recorded
/// at write time, and the quantum itself.
type LedgerEntry<'quantum, 'fp> =
    { Position    : int
      Fingerprint : 'fp
      Quantum     : 'quantum }

/// The admission proof token (the house derive-macro; §9.8.9's row): a
/// `Verified<'entry>` exists only if this grain's admission check passed.
/// The constructor is private — `Ledger.admitWrite` is the one mint, so
/// `Ledger.replay` folds over admitted entries by construction.
type Verified<'entry> = private Verified of 'entry

[<RequireQualifiedAccess>]
module Verified =
    let value (Verified entry) : 'entry = entry

/// Resume drift: a recorded fingerprint disagreeing with recomputation at
/// `Position` — the named refusal's typed payload (instances project it to
/// their own refusal codes).
type LedgerDrift<'fp> =
    { Position   : int
      Recorded   : 'fp
      Recomputed : 'fp }

[<RequireQualifiedAccess>]
module Ledger =

    /// WriteAdmit — admission at write time, external-witness-capable. The
    /// witness is the per-grain verification (the journal: the chunk's sink
    /// statement committed; the episode store: `recordVerified`'s B′≡B) —
    /// supplied by the instance, never recomputed here. On a passing
    /// witness the entry is fingerprinted and the proof token minted; a
    /// failing witness propagates its named errors and nothing is
    /// admitted.
    let admitWrite
        (witness: 'q -> Result<unit>)
        (spec: LedgerSpec<'s, 'q, 'fp>)
        (position: int)
        (quantum: 'q)
        : Result<Verified<LedgerEntry<'q, 'fp>>> =
        match witness quantum with
        | Ok () ->
            Result.success
                (Verified
                    { Position    = position
                      Fingerprint = spec.FingerprintOf quantum
                      Quantum     = quantum })
        | Error es -> Result.failure es

    /// The FTC at this grain (§5.1): the state is the fold of ⊕ over the
    /// verified entries, in position order. Entry order in the input list
    /// is immaterial — the chain's order is the recorded positions'.
    let replay
        (spec: LedgerSpec<'s, 'q, 'fp>)
        (entries: Verified<LedgerEntry<'q, 'fp>> list)
        : 's =
        entries
        |> List.map Verified.value
        |> List.sortBy (fun e -> e.Position)
        |> List.fold (fun state e -> spec.Apply state e.Quantum) spec.Genesis

    /// ResumeAdmit — recomputation against the stored fingerprints. The
    /// resume point is the first position absent from the recorded chain;
    /// every present position up to it must recompute to its recorded
    /// fingerprint or the resume REFUSES by name (drift, located). A
    /// duplicated position resolves last-write-wins (the journal index's
    /// existing semantics); entries beyond the first gap are ignored —
    /// they re-execute past the resume point.
    let resumePoint
        (recorded: LedgerEntry<'q, 'fp> list)
        (recompute: int -> 'fp)
        : Result<int, LedgerDrift<'fp>> =
        let byPosition =
            recorded |> List.map (fun e -> e.Position, e) |> Map.ofList
        let rec walk (position: int) : Result<int, LedgerDrift<'fp>> =
            match Map.tryFind position byPosition with
            | None -> Ok position
            | Some entry ->
                let recomputed = recompute position
                if recomputed = entry.Fingerprint then walk (position + 1)
                else
                    Error
                        { Position   = position
                          Recorded   = entry.Fingerprint
                          Recomputed = recomputed }
        walk 0
