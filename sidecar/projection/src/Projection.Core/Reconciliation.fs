namespace Projection.Core

/// One kind's reconciliation outcome: the matched Source→Sink surrogate
/// remap, plus the Source surrogates the ruleset could not match to a
/// pre-existing Sink identity (phase 2 skips-and-diagnoses these). The
/// per-kind, any-kind generalization of `UserFkReflowPass.discover` /
/// `UserRemapContext` — `SurrogateRemapContext` is the carrier (slice 1).
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

/// One Source row reference the reconciliation could not re-point: its FK
/// `Column` targeted a reconciled kind, but the referenced Source surrogate
/// (`UnresolvedSource`) had no matched Sink identity (unmatched at reconcile
/// time, or absent from the reconciled set entirely). The owning row is
/// dropped — skip-and-diagnose, the row analog of `reconcileKind`'s
/// per-identity `Unmatched`.
type UnresolvedReference =
    {
        Column           : Name
        Target           : SsKey
        UnresolvedSource : SourceKey
    }

/// The outcome of re-pointing one kind's FK values through a reconciliation
/// remap: the rows kept (with reconciled-FK values re-pointed to the Sink
/// surrogate) and the references dropped because the Sink had no matched
/// identity.
type RemappedRows =
    {
        Rows    : StaticRow list
        Skipped : UnresolvedReference list
    }

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

    /// The FK columns on `kind` whose target is a reconciled kind, keyed by
    /// the column `Name` (the key `StaticRow.Values` uses) → the reconciled
    /// target's `SsKey` (the key the `SurrogateRemapContext` uses). The set
    /// of columns whose value must be re-pointed from the Source surrogate
    /// to the matched Sink surrogate. Pure over the kind's FK graph.
    let reconciledFkColumns (reconciledKinds: Set<SsKey>) (kind: Kind) : Map<Name, SsKey> =
        kind.References
        |> List.choose (fun r ->
            if Set.contains r.TargetKind reconciledKinds then
                Kind.tryFindAttribute r.SourceAttribute kind
                |> Option.map (fun a -> a.Name, r.TargetKind)
            else None)
        |> Map.ofList

    /// Re-point a kind's FK values that target reconciled kinds through the
    /// remap. For each row, every `fkTargets` column carrying a non-NULL
    /// Source surrogate is resolved against the remap: a hit re-points the
    /// value to the Sink-assigned surrogate; a miss drops the row
    /// (skip-and-diagnose — the referenced identity has no Sink home). A
    /// NULL / absent FK is left untouched (it references nothing). Pure and
    /// order-preserving (T1 determinism).
    let remapRowFks
        (fkTargets: Map<Name, SsKey>)
        (remap: SurrogateRemapContext)
        (rows: StaticRow list)
        : RemappedRows =
        let mutable kept : StaticRow list = []
        let mutable skipped : UnresolvedReference list = []
        for row in rows do
            let resolved =
                fkTargets
                |> Map.fold
                    (fun acc col target ->
                        match acc with
                        | Error _ -> acc
                        | Ok values ->
                            match Map.tryFind col values with
                            | None -> Ok values
                            | Some v when v = "" -> Ok values
                            | Some v ->
                                match SurrogateRemapContext.tryFindAssigned target (SourceKey.ofString v) remap with
                                | Some assigned -> Ok (Map.add col (AssignedKey.value assigned) values)
                                | None ->
                                    Error { Column = col; Target = target; UnresolvedSource = SourceKey.ofString v })
                    (Ok row.Values)
            match resolved with
            | Ok values  -> kept <- { row with Values = values } :: kept
            | Error uref -> skipped <- uref :: skipped
        { Rows    = List.rev kept
          Skipped = List.rev skipped }

    /// Registry metadata (pillar 9). The reconciliation ruleset is operator
    /// intent — which Source identities reconcile to which pre-existing Sink
    /// identities. Classified `OperatorIntent Selection`, mirroring the
    /// forward `UserFkReflowPass` ("re-direction reads as Selection"); the
    /// Transfer epic's first `OperatorIntent` site.
    let registeredMetadata : RegisteredTransformMetadata =
        { Name         = "transferReconciliation"
          Domain       = Identity
          StageBinding = Pipeline
          Sites =
            [ TransformSite.operatorIntent "matchByRule" Selection
                "Match each Source surrogate to a pre-existing Sink surrogate by the operator-supplied ruleset (match column or manual override), producing the per-kind SurrogateRemapContext that phase 2 re-points reconciled FKs through. Operator intent — which identities reconcile; generalizes UserFkReflowPass.discover from the User kind. Unmatched Source surrogates skip-and-diagnose." ]
          Status = Active }
