namespace Projection.Core

// LINT-ALLOW-FILE: `Reconciliation.ofUserMatching` projects the User
// kind's typed `SourceUserId` / `TargetUserId` integers into the
// `RawValueCodec` raw-string surrogate form via `sprintf "%d"`. This is
// the SAME integer-to-raw projection `UserRemapContext.toSurrogate` uses
// (per `UserRemap.fs`'s LINT-ALLOW-FILE block) — the two acquisition
// surfaces must agree byte-for-byte on the surrogate raw form, so the
// codec convention is shared, not duplicated divergently. Same allowed-
// exception class as `UserRemap.fs` / `SurrogateRemap.fs`.

/// Reconciliation is one *acquisition* of a `SurrogateRemapContext` —
/// the sink-row-matching method specialized for Transfer flows where
/// Source surrogates reconcile to pre-existing Sink identities. The
/// produced remap is the same generic shape (`SurrogateRemap.fs`) every
/// other consumer (static-artifact emit, future MERGE re-pointing, etc.)
/// reads from; this file does NOT carry the consumption primitives —
/// those live in `SurrogateRemap` so emitters are not coupled to the
/// reconciliation semantics.

/// One kind's reconciliation outcome: the matched Source→Sink surrogate
/// remap, plus the Source surrogates the ruleset could not match to a
/// pre-existing Sink identity (downstream re-point skips-and-diagnoses
/// these). Generalizes `UserFkReflowPass.discover` to any kind.
type ReconciledIdentity =
    {
        Remap     : SurrogateRemapContext
        Unmatched : (SsKey * SourceKey) list
        /// NM-51 — Source surrogates whose PK column value is NOT unique among
        /// the rows reconciled: `SurrogateRemapContext.capture` refused the
        /// second binding (kept the first). Surfaced, never silently dropped —
        /// a non-unique reconcile key is a data-fidelity hazard the operator
        /// must see (the `MatchByColumn` / CSV `--user-map` reachable case).
        Ambiguous : (SsKey * SourceKey) list
        /// NM-58 — Sink surrogates that LOST the deterministic match tiebreaker:
        /// more than one Sink row shared a (non-blank) `MatchByColumn` value, so
        /// the oldest (lowest-PK) row won the reconcile and these are the rows it
        /// displaced. Surfaced, never silently dropped — a duplicated reconcile
        /// key on the Sink side is the real-estate hazard the operator must see
        /// (the production user directory's "duplicate email groups"); the
        /// operator can supply a `ManualOverride` for the specific keys. Empty
        /// for a unique key, a `ManualOverride`, or a non-reconciling Transfer.
        AmbiguousTargetKeys : (SsKey * AssignedKey) list
        /// 2026-07-06 (the single-owner program) — a `FallbackToAssigned`
        /// pinned owner whose key matches NO sink row: every re-keyed FK
        /// would point at a nonexistent record (a raw 547 mid-load).
        /// Surfaced so the caller refuses BY NAME before any write. Empty
        /// for match-only strategies or a pinned key the sink holds.
        MissingPinnedOwners : (SsKey * AssignedKey) list
    }

/// How the operator reconciles Source surrogates to pre-existing Sink
/// surrogates — the `ReconciledByRule` ruleset, generalized from the
/// User kind's `UserMatchingStrategy`. `MatchByColumn` is the structural
/// form of `ByEmail` / `BySsKey` (match on a designated column's value);
/// `ManualOverride` is the explicit operator map (V1's `UserMapLoader`
/// CSV); `FallbackToAssigned` is the structural form of
/// `FallbackToSystemUser` — try the `primary` ruleset, and on miss attribute
/// to a fixed `fallback` surrogate (the safety net catches every Source
/// surrogate, structurally guaranteeing an empty `Unmatched` set).
///
/// These four variants are the surjective image of `UserMatchingStrategy`
/// (`ByEmail` / `BySsKey` → `MatchByColumn`; `ManualOverride` →
/// `ManualOverride`; `FallbackToSystemUser` → `FallbackToAssigned`), so
/// every user-match strategy the operator can choose reaches the Transfer
/// re-key path through `Reconciliation.ofUserMatching`.
[<RequireQualifiedAccess>]
type ReconciliationStrategy =
    | MatchByColumn of column: Name
    /// A COMPOSITE natural/business key (data-portability Slice 5): match on the
    /// tuple of several columns' values. The declared/inferred multi-column key
    /// that drives reuse-vs-insert when a single column is not unique.
    | MatchByColumns of columns: Name list
    | ManualOverride of map: Map<SourceKey, AssignedKey>
    | FallbackToAssigned of fallback: AssignedKey * primary: ReconciliationStrategy

[<RequireQualifiedAccess>]
module Reconciliation =

    /// Reconcile one kind's Source surrogates to pre-existing Sink
    /// surrogates by the operator ruleset. `pkColumn` is the surrogate
    /// (PK) column whose value is the Source / Assigned key. A Source row
    /// with no Sink match lands in `Unmatched` (sorted by SourceKey for
    /// T1 determinism). Duplicate Source surrogates keep the first (a
    /// unique PK is a precondition).
    let reconcileKind
        (kind: SsKey)
        (pkColumn: Name)
        (strategy: ReconciliationStrategy)
        (sourceRows: StaticRow list)
        (sinkRows: StaticRow list)
        : ReconciledIdentity =

        let surrogateOf (row: StaticRow) : SourceKey option =
            Map.tryFind pkColumn row.Values |> Option.map SourceKey.ofString

        // NM-58 — a BLANK match key is "no key", never "a key that is empty".
        // The production user directory carries many rows with a blank/missing
        // email; without this exclusion a blank SOURCE key would match a blank
        // SINK key (`Map.ofList` would index ""), wrongly re-keying every
        // blank-email source row onto one arbitrary blank-email sink row. Blank
        // keys on either side are excluded so they fall to `Unmatched` (then the
        // fallback / pre-write halt / declared drop), never a silent miss-match.
        let isKey (v: string) : bool = not (System.String.IsNullOrWhiteSpace v)

        // Sink surrogates displaced by the duplicate-key tiebreaker (NM-58).
        let mutable ambiguousTargets : (SsKey * AssignedKey) list = []

        // `resolve src row` → the matched Sink surrogate for a Source row.
        // Recursive on `FallbackToAssigned`'s `primary` arm so nested fallback
        // chains compose; the fallback arm always returns `Some`, so any
        // strategy whose outermost variant is `FallbackToAssigned` produces an
        // empty `Unmatched` set (the safety net catches every Source surrogate).
        let rec resolveBy (s: ReconciliationStrategy) : SourceKey -> StaticRow -> AssignedKey option =
            match s with
            | ReconciliationStrategy.ManualOverride overrides ->
                fun src _ -> Map.tryFind src overrides
            | ReconciliationStrategy.MatchByColumn col ->
                // Index the Sink by its (non-blank) match-column value → Sink
                // surrogate. `sinkRows` arrive PK-ascending (`ReadSide` orders by
                // PK), so on a DUPLICATE match key the FIRST entry is the oldest
                // (lowest-PK) Sink row: the NAMED deterministic tiebreaker keeps
                // it and records the rest as `ambiguousTargets` (NM-58) — never
                // the prior silent, order-arbitrary `Map.ofList` last-wins.
                let sinkPairs =
                    sinkRows
                    |> List.choose (fun r ->
                        match Map.tryFind col r.Values, Map.tryFind pkColumn r.Values with
                        | Some matchValue, Some sinkSurrogate when isKey matchValue ->
                            Some (matchValue, AssignedKey.ofString sinkSurrogate)
                        | _ -> None)
                let sinkIndex =
                    sinkPairs
                    |> List.groupBy fst
                    |> List.map (fun (mv, group) ->
                        let winner = snd (List.head group)
                        group
                        |> List.tail
                        |> List.iter (fun (_, displaced) -> ambiguousTargets <- (kind, displaced) :: ambiguousTargets)
                        mv, winner)
                    |> Map.ofList
                fun _ row ->
                    match Map.tryFind col row.Values with
                    | Some mv when isKey mv -> Map.tryFind mv sinkIndex
                    | _                     -> None
            | ReconciliationStrategy.MatchByColumns cols ->
                // The COMPOSITE-key sibling of `MatchByColumn`: match on the
                // TUPLE of the columns' (all non-blank) values. Keyed by the
                // value LIST — comparable, so no string concatenation in Core
                // (the determinism layer's no-concat rule). Same oldest-row-wins
                // tiebreaker on a duplicate composite key (NM-58).
                let compositeKey (r: StaticRow) : string list option =
                    let present = cols |> List.choose (fun c -> Map.tryFind c r.Values)
                    if not (List.isEmpty cols)
                       && List.length present = List.length cols
                       && List.forall isKey present
                    then Some present
                    else None
                let sinkIndex =
                    sinkRows
                    |> List.choose (fun r ->
                        match compositeKey r, Map.tryFind pkColumn r.Values with
                        | Some k, Some sinkSurrogate -> Some (k, AssignedKey.ofString sinkSurrogate)
                        | _ -> None)
                    |> List.groupBy fst
                    |> List.map (fun (k, group) ->
                        let winner = snd (List.head group)
                        group
                        |> List.tail
                        |> List.iter (fun (_, displaced) -> ambiguousTargets <- (kind, displaced) :: ambiguousTargets)
                        k, winner)
                    |> Map.ofList
                fun _ row ->
                    match compositeKey row with
                    | Some k -> Map.tryFind k sinkIndex
                    | None   -> None
            | ReconciliationStrategy.FallbackToAssigned (fallback, primary) ->
                let resolvePrimary = resolveBy primary
                fun src row ->
                    match resolvePrimary src row with
                    | Some assigned -> Some assigned
                    | None          -> Some fallback

        let resolve = resolveBy strategy

        let mutable remap = SurrogateRemapContext.empty
        let mutable unmatched : (SsKey * SourceKey) list = []
        let mutable ambiguous : (SsKey * SourceKey) list = []
        for row in sourceRows do
            match surrogateOf row with
            | None -> ()
            | Some src ->
                match resolve src row with
                | Some assigned ->
                    match SurrogateRemapContext.capture kind src assigned remap with
                    | Ok r    -> remap <- r
                    // NM-51 — a duplicate Source surrogate keeps the first binding
                    // but is RECORDED (the named error is no longer discarded), so
                    // a non-unique reconcile key surfaces instead of silently
                    // dropping the row's identity.
                    | Error _ -> ambiguous <- (kind, src) :: ambiguous
                | None -> unmatched <- (kind, src) :: unmatched

        // The pinned-owner existence check (2026-07-06): every fallback key
        // the strategy carries (recursively) must name a row the sink holds.
        let sinkSurrogates =
            sinkRows |> List.choose surrogateOf |> List.map (fun (SourceKey s) -> s) |> Set.ofList
        let rec fallbackKeysOf (s: ReconciliationStrategy) : AssignedKey list =
            match s with
            | ReconciliationStrategy.FallbackToAssigned (k, primary) -> k :: fallbackKeysOf primary
            | _ -> []
        let missingPinned =
            fallbackKeysOf strategy
            |> List.filter (fun (AssignedKey a) -> not (Set.contains a sinkSurrogates))
            |> List.map (fun k -> kind, k)

        { Remap     = remap
          Unmatched = unmatched |> List.sortBy (fun (_, SourceKey s) -> s)
          Ambiguous = ambiguous |> List.sortBy (fun (_, SourceKey s) -> s)
          AmbiguousTargetKeys = ambiguousTargets |> List.sortBy (fun (_, AssignedKey a) -> a)
          MissingPinnedOwners = missingPinned }

    /// Translate the User kind's `UserMatchingStrategy` into the generic
    /// `ReconciliationStrategy` so all four operator-chosen user-match
    /// strategies reach the Transfer re-key path (`runReconciling`) through
    /// the same `reconcileKind` machinery. `ByEmail` / `BySsKey` are
    /// column-match variants in disguise — `ByEmail` matches on the
    /// `emailColumn`'s value, `BySsKey` on the `ssKeyColumn`'s value (the
    /// row column that carries the OSSYS-origin SsKey identity). The two
    /// columns are supplied because the User-kind's physical schema names
    /// them; the structural strategy is column-agnostic.
    ///
    /// `ManualOverride`'s typed `SourceUserId → TargetUserId` map projects
    /// into the raw `SourceKey → AssignedKey` shape via the `RawValueCodec`
    /// integer-to-raw convention (`sprintf "%d"`), the same projection
    /// `UserRemapContext.toSurrogate` uses, so the two acquisition surfaces
    /// agree byte-for-byte on the surrogate raw form.
    ///
    /// `FallbackToSystemUser (fallback, primary)` becomes
    /// `FallbackToAssigned (fallback's raw assigned key, translate primary)`,
    /// recursively — preserving the structural guarantee that a fallback
    /// strategy's `Unmatched` set is always empty.
    let rec ofUserMatching
        (emailColumn: Name)
        (ssKeyColumn: Name)
        (strategy: UserMatchingStrategy)
        : ReconciliationStrategy =
        match strategy with
        | ByEmail -> ReconciliationStrategy.MatchByColumn emailColumn
        | BySsKey -> ReconciliationStrategy.MatchByColumn ssKeyColumn
        | ManualOverride overrideMap ->
            overrideMap
            |> Map.toList
            |> List.map (fun (source, target) ->
                SourceKey.ofString (sprintf "%d" (SourceUserId.value source)),
                AssignedKey.ofString (sprintf "%d" (TargetUserId.value target)))
            |> Map.ofList
            |> ReconciliationStrategy.ManualOverride
        | FallbackToSystemUser (fallback, primary) ->
            ReconciliationStrategy.FallbackToAssigned
                (AssignedKey.ofString (sprintf "%d" (TargetUserId.value fallback)),
                 ofUserMatching emailColumn ssKeyColumn primary)

    /// Registry metadata (pillar 9). The reconciliation ruleset is operator
    /// intent — which Source identities reconcile to which pre-existing Sink
    /// identities. Classified `OperatorIntent Selection`, mirroring the
    /// forward `UserFkReflowPass` ("re-direction reads as Selection"); the
    /// Transfer epic's first `OperatorIntent` site. The downstream
    /// consumption (re-pointing FK values via `SurrogateRemap.remapRowFks`)
    /// registers separately on each consuming emitter, so this entry covers
    /// only the acquisition.
    let registeredMetadata : RegisteredTransformMetadata =
        { Name         = "transferReconciliation"
          Domain       = Identity
          StageBinding = Pipeline
          Sites =
            [ TransformSite.operatorIntent "matchByRule" Selection
                "Match each Source surrogate to a pre-existing Sink surrogate by the operator-supplied ruleset (match column or manual override), producing the per-kind SurrogateRemapContext that downstream consumers (Transfer realization, static-artifact emit) re-point FKs through. Operator intent — which identities reconcile; generalizes UserFkReflowPass.discover from the User kind. Unmatched Source surrogates skip-and-diagnose." ]
          Status = Active }
