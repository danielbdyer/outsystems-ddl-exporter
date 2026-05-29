namespace Projection.Core

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
    }

/// How the operator reconciles Source surrogates to pre-existing Sink
/// surrogates — the `ReconciledByRule` ruleset, generalized from the
/// User kind's `UserMatchingStrategy`. `MatchByColumn` is the structural
/// form of `ByEmail` / `BySsKey` (match on a designated column's value);
/// `ManualOverride` is the explicit operator map (V1's `UserMapLoader`
/// CSV). Recursive `FallbackToSystemUser`-style composition is deferred.
[<RequireQualifiedAccess>]
type ReconciliationStrategy =
    | MatchByColumn of column: Name
    | ManualOverride of map: Map<SourceKey, AssignedKey>

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
        let resolve : SourceKey -> StaticRow -> AssignedKey option =
            match strategy with
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

        let mutable remap = SurrogateRemapContext.empty
        let mutable unmatched : (SsKey * SourceKey) list = []
        for row in sourceRows do
            match surrogateOf row with
            | None -> ()
            | Some src ->
                match resolve src row with
                | Some assigned ->
                    match SurrogateRemapContext.capture kind src assigned remap with
                    | Ok r    -> remap <- r
                    | Error _ -> ()   // duplicate Source surrogate — keep first
                | None -> unmatched <- (kind, src) :: unmatched

        { Remap     = remap
          Unmatched = unmatched |> List.sortBy (fun (_, SourceKey s) -> s) }

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
