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
                // Index the Sink by its match-column value → Sink surrogate.
                let sinkIndex =
                    sinkRows
                    |> List.choose (fun r ->
                        match Map.tryFind col r.Values, Map.tryFind pkColumn r.Values with
                        | Some matchValue, Some sinkSurrogate -> Some (matchValue, AssignedKey.ofString sinkSurrogate)
                        | _ -> None)
                    |> Map.ofList
                fun _ row ->
                    Map.tryFind col row.Values |> Option.bind (fun mv -> Map.tryFind mv sinkIndex)
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

        { Remap     = remap
          Unmatched = unmatched |> List.sortBy (fun (_, SourceKey s) -> s)
          Ambiguous = ambiguous |> List.sortBy (fun (_, SourceKey s) -> s) }

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
