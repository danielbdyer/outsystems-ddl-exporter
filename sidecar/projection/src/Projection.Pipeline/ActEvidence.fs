namespace Projection.Pipeline

// THE ACT FINGERPRINT DERIVATION (2026-07-10, the transfer-manifest program,
// slice 4a — THE_TRANSFER_MANIFEST.md §6.3): the pure bridge from the typed
// evidence the board already holds (the dry-run `DataLoadPlan`, the slice-2
// `EvidenceCache` match products, the sink population probes) to each act's
// `ActConsent.ActFingerprint`. Everything here is a PROJECTION of substrate
// read once elsewhere — no IO, no re-derivation, so the fingerprint the board
// narrates and the fingerprint the engine gates on (slice 4b) come from the
// same facts and cannot drift.

open Projection.Core

[<RequireQualifiedAccess>]
module ActEvidence =

    /// The probed population boundary for one table an act consumes — the
    /// face's MIN/MAX-primary-key + COUNT probe on the sink table (a wipe's
    /// blast radius, pinned).
    type PopulationProbe =
        { FirstKey : string
          LastKey  : string
          RowCount : int }

    let private pkNameOf (catalog: Catalog) (kind: SsKey) : Name option =
        Catalog.tryFindKind kind catalog
        |> Option.bind (fun k -> k.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey))
        |> Option.map (fun a -> a.Name)

    /// The plan's population for one kind: first / last primary-key value in
    /// planned load order, and the raw row count. Pure over the plan.
    let private plannedPopulation (catalog: Catalog) (plan: DataLoadPlan) (kind: SsKey) : PopulationProbe option =
        match pkNameOf catalog kind with
        | None -> None
        | Some pkName ->
            plan.Loads
            |> List.tryFind (fun l -> l.Kind = kind)
            |> Option.map (fun l ->
                let pks = l.Rows |> List.map (fun r -> r.Values |> Map.tryFind pkName |> Option.defaultValue "")
                { FirstKey = (match pks with [] -> "" | first :: _ -> first)
                  LastKey  = (match List.tryLast pks with Some last -> last | None -> "")
                  RowCount = List.length pks })

    /// One kind's planned (pk, column) value pairs — a re-key act's effect
    /// substrate: WHICH rows re-point, keyed by WHICH reference values. The
    /// new keys a minted parent will receive do not exist until the write, so
    /// the fingerprint pins the correspondence the re-point starts from — any
    /// source edit to the rows or the reference values re-opens the act.
    let private plannedPairs (catalog: Catalog) (plan: DataLoadPlan) (kind: SsKey) (column: Name) : (string * string) list =
        match pkNameOf catalog kind with
        | None -> []
        | Some pkName ->
            plan.Loads
            |> List.tryFind (fun l -> l.Kind = kind)
            |> Option.map (fun l ->
                l.Rows
                |> List.map (fun r ->
                    (r.Values |> Map.tryFind pkName |> Option.defaultValue ""),
                    (r.Values |> Map.tryFind column |> Option.defaultValue "")))
            |> Option.defaultValue []

    /// THE fingerprint derivation: each act's fingerprint from the typed
    /// substrate already in hand. Pure; total over the map — an act whose
    /// substrate is unavailable (a failed sink probe, a reconciled kind the
    /// evidence cache did not cover) is simply ABSENT from the result, and the
    /// caller narrates the absence rather than inventing a fingerprint.
    ///
    /// Sources, per arm:
    ///  Wipe            — `probes` (the face's sink MIN/MAX + COUNT read);
    ///  Mint            — the plan's own rows for the kind (population);
    ///  IdentityInsert  — the plan's own rows for the kind (population);
    ///  Rekey           — effect over the plan's (pk, reference value) pairs;
    ///  Match           — effect over `EvidenceCache.matchProducts` (the same
    ///                    match the forecast computed: pairs, unmatched, exact
    ///                    sink counts);
    ///  Drop            — effect over the plan's dropped source keys;
    ///  DeleteScope / Resolve — never derived here (`actsOf` emits neither on
    ///                    this path).
    let fingerprintsOf
        (nameOf: SsKey -> string)
        (catalog: Catalog)
        (plan: DataLoadPlan)
        (cache: EvidenceCache.Cache option)
        (reconcileColumns: Map<SsKey, Name>)
        (probes: Map<SsKey, PopulationProbe>)
        (acts: ActConsent.Act list)
        : Map<string, ActConsent.ActFingerprint> =
        let effectOf (token: string) (resolution: string) (pairs: (string * string) list) (unmatched: string list) (sinkCounts: (int64 * int64) option) (planned: int) : ActConsent.ActFingerprint =
            let total, distinct = sinkCounts |> Option.defaultValue (0L, 0L)
            ActConsent.effectFingerprint
                { Token = token
                  Resolution = resolution
                  MatchedPairs = pairs
                  UnmatchedValues = unmatched
                  SinkTotal = total
                  SinkDistinct = distinct
                  PlannedCount = planned }
        acts
        |> List.choose (fun act ->
            let token = ActConsent.tokenOf nameOf act
            let fp =
                match act with
                | ActConsent.Act.Wipe t ->
                    probes
                    |> Map.tryFind t
                    |> Option.map (fun p -> ActConsent.populationFingerprint p.FirstKey p.LastKey p.RowCount)
                | ActConsent.Act.Mint t
                | ActConsent.Act.IdentityInsert t ->
                    plannedPopulation catalog plan t
                    |> Option.map (fun p -> ActConsent.populationFingerprint p.FirstKey p.LastKey p.RowCount)
                | ActConsent.Act.Rekey (o, c) ->
                    let pairs = plannedPairs catalog plan o c
                    Some (effectOf token "re-point" pairs [] None (List.length pairs))
                | ActConsent.Act.Match t ->
                    match cache, reconcileColumns |> Map.tryFind t with
                    | Some cache, Some col ->
                        let pairs, unmatched, counts = EvidenceCache.matchProducts cache catalog t col
                        Some (effectOf token ("reconcile:" + Name.value col) pairs unmatched counts (List.length pairs))
                    | _ -> None
                | ActConsent.Act.Drop (o, c) ->
                    let dropped =
                        plan.SkippedReferences
                        |> List.choose (fun (owner, r) ->
                            if owner = o && r.Column = c then Some (SourceKey.value r.UnresolvedSource) else None)
                    Some (effectOf token "drop" [] dropped None (List.length dropped))
                | ActConsent.Act.DeleteScope _
                | ActConsent.Act.Resolve _ -> None
            fp |> Option.map (fun f -> token, f))
        |> Map.ofList
