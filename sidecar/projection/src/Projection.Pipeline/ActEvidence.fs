namespace Projection.Pipeline

// THE ACT FINGERPRINT DERIVATION (2026-07-10, the transfer-manifest program,
// slices 4a/4b — THE_TRANSFER_MANIFEST.md §6.3): the bridge from the typed
// evidence in hand (the dry-run `DataLoadPlan`, the slice-2 `EvidenceCache`
// match products, the sink population probes) to each act's
// `ActConsent.ActFingerprint`. The derivation (`fingerprintsOf`) is a pure
// PROJECTION of substrate read once elsewhere; the two named IO seams at the
// bottom (`populationProbe`, `fillMatchCache`) are SHARED by the go board's
// consent axis and the engine's execute gate, so the fingerprint the board
// narrates and the fingerprint the gate enforces come from the same reads —
// byte-identical by construction, not by convention.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
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

    /// The single match column a reconcile strategy resolves rows by — the
    /// Match act's effect substrate. `FallbackToAssigned` defers to its
    /// primary; a composite or manual strategy carries no single column.
    /// Shared by the board's consent axis and the engine's execute gate.
    let rec matchColumnOf (strategy: ReconciliationStrategy) : Name option =
        match strategy with
        | ReconciliationStrategy.MatchByColumn c -> Some c
        | ReconciliationStrategy.FallbackToAssigned (_, primary) -> matchColumnOf primary
        | _ -> None

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
                // `valueOrEmpty` (NULL → "") keeps the persisted consent
                // fingerprint BYTE-STABLE across WP-3 — do not lift to the
                // option grain here without a re-bless decision.
                let pks = l.Rows |> List.map (StaticRow.valueOrEmpty pkName)
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
                    // Blessing-stable flatten (NULL → ""), as above.
                    StaticRow.valueOrEmpty pkName r,
                    StaticRow.valueOrEmpty column r))
            |> Option.defaultValue []

    /// A Match act's effect substrate per reconcile strategy: the resolution
    /// identity, the (key, resolved identity) pairs, the unmatched values, the
    /// exact sink counts, and the planned count. A column match reads the
    /// evidence cache (the same pairs the forecast matched); a pinned
    /// `ManualOverride` IS its own substrate (the operator-authored key map —
    /// pure config, no read needed); a fallback folds its primary and names
    /// the fallback key in the resolution (a re-pointed fallback is a
    /// different consent). A composite-column match has no single-column
    /// substrate here — `None`, narrated as unread.
    let rec private matchSubstrate
        (cache: EvidenceCache.Cache option)
        (catalog: Catalog)
        (target: SsKey)
        (strategy: ReconciliationStrategy)
        : (string * (string * string) list * string list * (int64 * int64) option * int) option =
        match strategy with
        | ReconciliationStrategy.MatchByColumn col ->
            cache
            |> Option.map (fun c ->
                let pairs, unmatched, counts = EvidenceCache.matchProducts c catalog target col
                ("reconcile:" + Name.value col), pairs, unmatched, counts, List.length pairs)
        | ReconciliationStrategy.ManualOverride map ->
            let pairs =
                map |> Map.toList |> List.map (fun (s, a) -> SourceKey.value s, AssignedKey.value a)
            Some ("pinned", pairs, [], None, List.length pairs)
        | ReconciliationStrategy.FallbackToAssigned (fallback, primary) ->
            matchSubstrate cache catalog target primary
            |> Option.map (fun (res, pairs, unmatched, counts, planned) ->
                (res + "+fallback:" + AssignedKey.value fallback), pairs, unmatched, counts, planned)
        | ReconciliationStrategy.MatchByColumns _ -> None

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
    ///  Match           — effect per strategy (`matchSubstrate`): the cache's
    ///                    matched pairs for a column match, the authored key
    ///                    map for a pin, the primary + named fallback for a
    ///                    fallback;
    ///  Drop            — effect over the plan's dropped source keys;
    ///  DeleteScope / Resolve — never derived here (`actsOf` emits neither on
    ///                    this path).
    let fingerprintsOf
        (nameOf: SsKey -> string)
        (catalog: Catalog)
        (plan: DataLoadPlan)
        (cache: EvidenceCache.Cache option)
        (reconciliation: Map<SsKey, ReconciliationStrategy>)
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
                    reconciliation
                    |> Map.tryFind t
                    |> Option.bind (matchSubstrate cache catalog t)
                    |> Option.map (fun (resolution, pairs, unmatched, counts, planned) ->
                        effectOf token resolution pairs unmatched counts planned)
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

    // -- the two IO seams, shared by the board's consent axis and the engine's
    //    execute gate (slice 4b): one probe, one cache read — the fingerprints
    //    the two surfaces derive come from the same SQL text and the same
    //    rendering, so they are byte-identical by construction.

    /// The population boundary a Wipe act consumes on the target: COUNT plus
    /// MIN/MAX primary key, each rendered as nvarchar IN THE QUERY so the
    /// fingerprint text is byte-stable regardless of the key's SQL type.
    /// `None` when the kind has no primary key or the probe fails — the
    /// caller narrates the absence (board) or refuses on it (gate), never
    /// invents a boundary.
    let populationProbe (cnn: SqlConnection) (kind: Kind) : Task<PopulationProbe option> =
        match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
        | None -> Task.FromResult None
        | Some pk ->
            let pkText = ColumnRealization.columnNameText pk.Column
            let sql =
                sprintf "SELECT COUNT_BIG(*), CONVERT(nvarchar(128), MIN([%s])), CONVERT(nvarchar(128), MAX([%s])) FROM [%s].[%s];"
                    pkText pkText (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)  // LINT-ALLOW: validated TableId/column coordinates at the read boundary
            task {
                try
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- sql
                    use! r = cmd.ExecuteReaderAsync()
                    let! hasRow = r.ReadAsync()
                    if hasRow then
                        return Some { FirstKey = (if r.IsDBNull 1 then "" else r.GetString 1)
                                      LastKey  = (if r.IsDBNull 2 then "" else r.GetString 2)
                                      RowCount = int (r.GetInt64 0) }
                    else return None
                with _ -> return None
            }

    /// The reconciled kinds' row substrate for the Match effect hashes: one
    /// synthetic self-edge per (kind, match column) through
    /// `EvidenceCache.fill` — the same reader the workbench evidence uses, so
    /// the pairs a Match fingerprint hashes are the pairs the forecast
    /// matched. `None` when there is nothing to read or the read fails.
    let fillMatchCache
        (source: SqlConnection)
        (sink: SqlConnection)
        (sourceContract: Catalog)
        (sinkContract: Catalog)
        (reconcileColumns: Map<SsKey, Name>)
        : Task<EvidenceCache.Cache option> =
        if Map.isEmpty reconcileColumns then Task.FromResult None
        else
            let selfEdges =
                reconcileColumns
                |> Map.toList
                |> List.choose (fun (t, c) ->
                    Catalog.tryFindKind t sinkContract
                    |> Option.map (fun k ->
                        { PeerTransfer.Kind = t; PeerTransfer.KindName = k.Name
                          PeerTransfer.Column = c; PeerTransfer.Nullable = true
                          PeerTransfer.Target = t; PeerTransfer.TargetName = k.Name
                          PeerTransfer.TargetModule = k.Name
                          PeerTransfer.CandidateReconcileColumns = [ c ] }))
            task {
                try
                    let! cache = EvidenceCache.fill source sink sourceContract sinkContract selfEdges
                    return Some cache
                with _ -> return None
            }
