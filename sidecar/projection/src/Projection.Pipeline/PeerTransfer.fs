namespace Projection.Pipeline

// LINT-ALLOW-FILE: the gate/proposal renderers compose operator-facing report
//   prose (THE_VOICE register) at a terminal reporting boundary; the detection
//   core is pure and carries no I/O — contract acquisition (the two OSSYS
//   reads) is the one Task-returning seam, mirroring `Readiness`/`Compare`'s
//   pure-core / I/O-one-layer-up split.

open System.Threading.Tasks
open Projection.Core

/// The peer (A→A) transfer's contract + gate support: two cloud cells of ONE
/// model whose physical `OSUSR_*` names differ per espace (the QA→UAT partial
/// transfer, `PARTIAL_TRANSFER_READINESS_LOG.md` 2026-07-06).
///
/// The identity precondition the rename-aware engine needs (`TransferRun.fs`,
/// `runCore` with an ingestion pair) is SsKey ALIGNMENT between the two
/// contracts. `ReadSide` cannot supply it (SsKeys synthesized from physical
/// coordinates — two live reads never align); the ONE authored model rendered
/// twice (`CatalogRendition`) covers only the logical↔physical reverse leg.
/// The peer pair aligns the third way: EACH side's contract is read from its
/// own OSSYS metamodel (`Source.ofOssys` → `LiveModelRead`), whose `SsKey`s
/// are the native OutSystems GUIDs — stable across environments (LifeTime
/// preserves SS_KEY; the espace-invariance law, `CROSS_ENVIRONMENT_READINESS`
/// / `Readiness`). Same identity, two physical realizations — exactly the
/// precondition, without an authored model in the loop.
///
/// Two pre-transfer gates ride the acquired pair:
///   - the SHAPE gate — the SS_KEY-keyed schema-compatibility verdict
///     (insertability-blocking divergences refuse by name; real-but-benign
///     divergences surface as advisories, never silently), and
///   - the SUBSET-FK gate — every FK edge escaping a declared `tables` subset
///     is detected and a per-edge strategy is proposed (reconcile-by-key
///     against rows the sink already holds — the default recommendation;
///     widen the subset; or accept the drop-set with --allow-drops).
[<RequireQualifiedAccess>]
module PeerTransfer =

    // ------------------------------------------------------------------
    // Contract acquisition — the one I/O seam.
    // ------------------------------------------------------------------

    /// Read the two SsKey-aligned contracts from the two environments' OSSYS
    /// metamodels (D9 conn refs: `env:<var>` / `file:<path>` / raw). A failed
    /// read is the named `source.ossys.readFailed` refusal from the `Source`
    /// port, classified onto the schema-read axis (exit 6) by `Preflight`.
    let acquireContracts (sourceConn: string) (sinkConn: string) : Task<Result<Catalog * Catalog>> =
        task {
            match! Source.read (Source.ofOssys sourceConn) with
            | Error es -> return Result.failure es
            | Ok sourceContract ->
                match! Source.read (Source.ofOssys sinkConn) with
                | Error es -> return Result.failure es
                | Ok sinkContract -> return Result.success (sourceContract, sinkContract)
        }

    // ------------------------------------------------------------------
    // The shape gate — SS_KEY-keyed schema compatibility, scoped to the
    // kinds the run will touch.
    // ------------------------------------------------------------------

    /// The shape verdict over the pair: `Blocking` divergences prevent correct
    /// row insertion (kind presence, attribute presence, column-shape facets);
    /// `Advisory` divergences are real but do not block a data load
    /// (constraint/index/kind-facet drift, logical renames, widenings) — they
    /// surface so nothing is silent.
    type ShapeVerdict =
        { Blocking : string list
          Advisory : string list }

    let private kindNameIn (c: Catalog) (key: SsKey) : string =
        match Catalog.tryFindKind key c with
        | Some k -> Name.value k.Name
        | None -> SsKey.rootOriginal key

    let private attrIn (k: Kind) (key: SsKey) : Attribute option =
        k.Attributes |> List.tryFind (fun a -> a.SsKey = key)

    let private attrNameIn (k: Kind option) (key: SsKey) : string =
        k
        |> Option.bind (fun k -> attrIn k key)
        |> Option.map (fun a -> Name.value a.Name)
        |> Option.defaultValue (SsKey.rootOriginal key)

    /// Compute the shape verdict for the pair, scoped: `Some keys` restricts
    /// the verdict to the kinds a partial run touches (the subset + its
    /// reconciled kinds); `None` judges the whole estate (a full transfer).
    /// Both catalogs are normalized to their espace-invariant logical shape
    /// first (`Readiness.toLogicalShape` — physical-realization artifacts
    /// stripped), so only REAL model divergence registers; the physical
    /// `OSUSR_*` naming difference this leg exists for never does.
    let shapeVerdict (scope: Set<SsKey> option) (sourceContract: Catalog) (sinkContract: Catalog) : ShapeVerdict =
        let src = Readiness.toLogicalShape sourceContract
        let snk = Readiness.toLogicalShape sinkContract
        let diff = CatalogDiff.between src snk
        let inScope (k: SsKey) =
            match scope with
            | None -> true
            | Some s -> Set.contains k s
        let blocking = ResizeArray<string>()
        let advisory = ResizeArray<string>()
        // Kind presence: a source kind absent from the sink model blocks — the
        // rows have no landing table. (Sink-only kinds never block a load OUT
        // of the source; they surface only on an unscoped, whole-estate check.)
        for key in CatalogDiff.removed diff |> Set.filter inScope do
            blocking.Add (sprintf "entity '%s' is not in the sink model — its rows have no landing table." (kindNameIn src key))
        if Option.isNone scope then
            for key in CatalogDiff.added diff do
                advisory.Add (sprintf "entity '%s' exists only in the sink model (no source rows will touch it)." (kindNameIn snk key))
        // Same-identity logical renames: identity (SS_KEY) holds, so the load
        // is unaffected; surfaced because the operator's table lists are
        // name-keyed.
        for KeyValue (key, r) in CatalogDiff.renamed diff do
            if inScope key then
                advisory.Add (sprintf "entity '%s' is named '%s' in the sink — same identity (SS_KEY); the load keys on identity." (Name.value r.OldName) (Name.value r.NewName))
        // Attribute grain, per in-scope kind present in both models.
        for KeyValue (kindKey, ad) in CatalogDiff.attributeDiffs diff do
            if inScope kindKey then
                let srcKind = Catalog.tryFindKind kindKey src
                let snkKind = Catalog.tryFindKind kindKey snk
                let kindLabel = kindNameIn src kindKey
                for attrKey in ad.Removed do
                    blocking.Add (sprintf "%s.%s exists only in the source model — its values have no landing column." kindLabel (attrNameIn srcKind attrKey))
                for attrKey in ad.Added do
                    match snkKind |> Option.bind (fun k -> attrIn k attrKey) with
                    | Some a when a.IsMandatory && not a.IsIdentity && Option.isNone a.DefaultValue ->
                        blocking.Add (sprintf "%s.%s is sink-only, mandatory, and carries no default — inserted rows cannot satisfy it." kindLabel (Name.value a.Name))
                    | Some a ->
                        advisory.Add (sprintf "%s.%s exists only in the sink model (inserted rows leave it to its default)." kindLabel (Name.value a.Name))
                    | None -> ()
                for KeyValue (attrKey, r) in ad.Renamed do
                    advisory.Add (sprintf "%s.%s is named '%s' in the sink — same identity; the rename map re-points it." kindLabel (Name.value r.OldName) (Name.value r.NewName))
                for change in ad.Reshaped do
                    let srcAttr = srcKind |> Option.bind (fun k -> attrIn k change.AttributeKey)
                    let snkAttr = snkKind |> Option.bind (fun k -> attrIn k change.AttributeKey)
                    let attrLabel = attrNameIn srcKind change.AttributeKey
                    for facet in change.Facets do
                        let line (verdict: ResizeArray<string>) (detail: string) =
                            verdict.Add (sprintf "%s.%s: %s." kindLabel attrLabel detail)
                        match facet with
                        | AttributeFacet.DataType ->
                            line blocking "the data type differs between source and sink"
                        | AttributeFacet.PrimaryKey ->
                            line blocking "the primary-key marking differs between source and sink"
                        | AttributeFacet.Identity ->
                            line blocking "the IDENTITY marking differs between source and sink"
                        | AttributeFacet.Computed ->
                            line blocking "the computed-column definition differs between source and sink"
                        | AttributeFacet.Nullability ->
                            match srcAttr, snkAttr with
                            | Some s, Some t when (not s.IsMandatory) && t.IsMandatory ->
                                line blocking "nullable in the source but mandatory in the sink — NULL values cannot land"
                            | _ ->
                                line advisory "the sink is more permissive on nullability (no value is refused)"
                        | AttributeFacet.Length ->
                            match srcAttr |> Option.bind (fun a -> a.Length), snkAttr |> Option.bind (fun a -> a.Length) with
                            | Some s, Some t when t < s ->
                                line blocking (sprintf "the sink length (%d) is narrower than the source (%d) — values can overflow" t s)
                            | _, None ->
                                line advisory "the sink length is open-ended (no value is refused)"
                            | _ ->
                                line advisory "the sink length is wider (no value is refused)"
                        | AttributeFacet.Precision | AttributeFacet.Scale ->
                            line blocking "the decimal precision/scale differs between source and sink"
                        | AttributeFacet.DefaultValue ->
                            line advisory "the default value differs (defaults never rewrite transferred values)"
        // Constraint / index / kind-own drift: real divergence, but none of it
        // refuses a row — advisory by design (the migrate verb owns schema).
        let advisoryCount (label: string) (keys: Set<SsKey>) =
            if not (Set.isEmpty keys) then
                let names = keys |> Set.toList |> List.map (kindNameIn src) |> String.concat ", "
                advisory.Add (sprintf "%s differ(s) on: %s (schema drift — does not block a data load)." label names)
        advisoryCount "foreign-key constraints" (CatalogDiff.referenceDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        advisoryCount "indexes" (CatalogDiff.indexDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        advisoryCount "entity-level facets (modality/triggers/checks/activation)" (CatalogDiff.kindFacetDiffs diff |> Map.filter (fun k _ -> inScope k) |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        { Blocking = List.ofSeq blocking; Advisory = List.ofSeq advisory }

    /// The gate form of the verdict: blocking divergence refuses by name
    /// (`transfer.peer.shapeDivergence` — the shape-divergence axis, exit 5,
    /// the same verdict class `check shape` reports). Advisory lines ride in
    /// the `Ok` so the caller surfaces them regardless.
    let shapeGate (scope: Set<SsKey> option) (sourceContract: Catalog) (sinkContract: Catalog) : Result<string list> =
        let verdict = shapeVerdict scope sourceContract sinkContract
        if List.isEmpty verdict.Blocking then Result.success verdict.Advisory
        else
            Result.failureOf
                (ValidationError.create "transfer.peer.shapeDivergence"
                    (sprintf "the source and sink models are not one shape over the transferred set (%d blocking divergence(s)): %s"
                        verdict.Blocking.Length
                        (String.concat " " verdict.Blocking)))

    // ------------------------------------------------------------------
    // The subset-FK gate — every relationship escaping the declared subset,
    // detected, with a strategy proposed per edge.
    // ------------------------------------------------------------------

    /// One FK edge from an in-subset kind to an out-of-subset, un-reconciled
    /// kind — the edge a partial transfer must decide a strategy for.
    type EscapingFk =
        { Kind       : SsKey
          KindName   : Name
          /// The FK attribute's logical name (espace-safe).
          Column     : Name
          /// The FK column is optional — rows carrying NULL pass untouched.
          Nullable   : bool
          Target     : SsKey
          TargetName : Name
          /// Candidate reconcile columns on the target: its single-column
          /// UNIQUE indexes over non-PK attributes (the business keys a
          /// `reconcile <Target>:<Column>` can match sink rows by).
          CandidateReconcileColumns : Name list }

    /// Detect every FK edge that escapes the declared subset: source kind in
    /// the load-set, target kind neither in the load-set nor reconciled.
    /// Deterministic (sorted by kind name, then column name). Empty load-set
    /// semantics ride the caller: pass the RESOLVED subset (a full transfer
    /// has no escaping edges by definition — pass `Set.empty` mapped over
    /// `None` upstream and skip the call).
    let escapingFks (contract: Catalog) (loadSet: Set<SsKey>) (reconciled: Set<SsKey>) : EscapingFk list =
        let candidateKeysOf (target: Kind) : Name list =
            target.Indexes
            |> List.choose (fun ix ->
                match ix.Uniqueness, ix.Columns with
                | IndexUniqueness.Unique, [ col ] ->
                    attrIn target col.Attribute
                    |> Option.filter (fun a -> not a.IsPrimaryKey)
                    |> Option.map (fun a -> a.Name)
                | _ -> None)
            |> List.sortBy Name.value
        Catalog.allKinds contract
        |> List.filter (fun k -> Set.contains k.SsKey loadSet)
        |> List.collect (fun kind ->
            kind.References
            |> List.choose (fun r ->
                if Set.contains r.TargetKind loadSet || Set.contains r.TargetKind reconciled then None
                else
                    match Catalog.tryFindKind r.TargetKind contract with
                    | None -> None // a dangling model edge is the model's own diagnostic, not this gate's
                    | Some target ->
                        let sourceAttr = attrIn kind r.SourceAttribute
                        Some
                            { Kind       = kind.SsKey
                              KindName   = kind.Name
                              Column     = sourceAttr |> Option.map (fun a -> a.Name) |> Option.defaultValue r.Name
                              Nullable   = sourceAttr |> Option.map (fun a -> not a.IsMandatory) |> Option.defaultValue false
                              Target     = r.TargetKind
                              TargetName = target.Name
                              CandidateReconcileColumns = candidateKeysOf target }))
        |> List.sortBy (fun e -> Name.value e.KindName, Name.value e.Column)

    /// The per-edge strategy proposal lines (operator-facing; one line per
    /// escaping edge, the recommended move first).
    let narrateEscapes (escapes: EscapingFk list) : string list =
        escapes
        |> List.map (fun e ->
            let candidates =
                match e.CandidateReconcileColumns with
                | [] -> "no unique non-key column found — widen the subset or accept drops"
                | cs -> sprintf "reconcile candidates: %s" (cs |> List.map Name.value |> String.concat ", ")
            let softening = if e.Nullable then " (optional — rows with no reference pass untouched)" else ""
            sprintf "%s.%s -> %s is outside the subset%s. Strategies: reconcile '%s' against the rows the sink already holds (%s); or add '%s' to the subset; or accept the drop-set with --allow-drops."
                (Name.value e.KindName) (Name.value e.Column) (Name.value e.TargetName)
                softening
                (Name.value e.TargetName) candidates (Name.value e.TargetName))

    /// The gate form: a live Execute with un-strategized escaping edges
    /// refuses by name (`transfer.peer.subsetFkEscapes` — the drop-set axis,
    /// exit 9) unless the operator declared the drops acceptable. A DryRun
    /// never refuses here — the preview narrates the proposals instead.
    let subsetFkGate (execute: bool) (allowDrops: bool) (escapes: EscapingFk list) : Result<unit> =
        if execute && not allowDrops && not (List.isEmpty escapes) then
            Result.failureOf
                (ValidationError.create "transfer.peer.subsetFkEscapes"
                    (sprintf "%d relationship(s) escape the declared table subset; each needs a strategy before a live run: %s"
                        escapes.Length
                        (String.concat " " (narrateEscapes escapes))))
        else Result.success ()
