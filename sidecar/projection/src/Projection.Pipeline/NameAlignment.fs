namespace Projection.Pipeline

open Projection.Core

/// The IDENTITY BASIS a peer transfer aligns its two contracts on.
///
///   - `BySsKey` — the two contracts are two RENDITIONS of ONE authored model
///     (or one model read from two environments whose native OSSYS GUIDs are
///     preserved by LifeTime). Identity is the native `OssysOriginal` GUID and
///     is EQUAL across the pair; every existing peer/reverse-leg operation keys
///     on that. Default — byte-identical to the established legs.
///   - `ByName` — the two contracts are CLONED MODULES: distinct OSSYS entities
///     (different native GUIDs) that are SAME-NAMED with identical attributes.
///     Identity is derived from the logical NAME, so it is A1-BOUNDED (a rename
///     on either side breaks it) — materially weaker than `BySsKey`. Entered
///     only by explicit operator opt-in (`alignment: "by-name"`), never
///     auto-detected: a genuinely mis-wired pair (wrong environment) whose GUIDs
///     diverged by accident must NOT be silently "aligned by name."
[<RequireQualifiedAccess>]
type AlignmentMode =
    | BySsKey
    | ByName

/// Parse / render for the flow-config `alignment` token (A44: the config word
/// and the DU are one round-trip). Lenient on case/whitespace at the parse
/// boundary; an unknown token refuses BY NAME.
[<RequireQualifiedAccess>]
module AlignmentMode =

    let serialize (mode: AlignmentMode) : string =
        match mode with
        | AlignmentMode.BySsKey -> "by-sskey"
        | AlignmentMode.ByName  -> "by-name"

    let parse (token: string) : Result<AlignmentMode> =
        match (token.Trim()).ToLowerInvariant() with
        | "by-sskey" -> Result.success AlignmentMode.BySsKey
        | "by-name"  -> Result.success AlignmentMode.ByName
        | other ->
            Result.failureOf
                (ValidationError.create "alignment.mode.unknown"
                    (System.String.Concat
                        [ "unknown alignment mode '"; other; "' — expected 'by-sskey' or 'by-name'." ]))

/// THE cloned-module alignment pass (2026-07-09). Rewrites the SOURCE contract's
/// SsKey-bearing identity fields to the SINK's corresponding SsKeys, matched BY
/// NAME within an operator-declared source→sink MODULE correspondence, keeping
/// physical coordinates / types / structure UNCHANGED. After the pass the two
/// contracts are SsKey-aligned, so the entire existing engine
/// (`CatalogDiff.between`, `RenameProjection`, `PeerTransfer.shapeGate`,
/// reconcile, FK re-point, `TransferScope`, the write loop) runs UNCHANGED — for
/// a true clone the logical names are identical, so the rename map is empty and
/// the aligned pair diffs to zero. (`CatalogDiff` compares no physical
/// column-NAME facet, so the `OSUSR_*` prefix difference never registers.)
///
/// DEFENSIVE — progressively verifies each dependent concern and refuses BY NAME
/// where the alignment cannot be established safely:
///   - `alignment.module.unmatched`      — a declared module pair is missing a side.
///   - `alignment.entity.ambiguous`      — the mapped sink module holds >1 same-named entity.
///   - `alignment.entity.unmatched`      — a source entity has no counterpart on the sink.
///   - `alignment.attribute.unmatched`   — a source attribute is absent on the matched sink kind.
///   - `alignment.attribute.shapeDivergence` — a name-matched attribute pair differs on ≥1
///     `AttributeFacet` (STRICT — via the shared `CatalogDiff.attributeShapeFacets` comparator).
/// Entity/attribute mismatches are COLLECTED (independent — one report for a large
/// clone); the module-correspondence check short-circuits (nothing downstream is
/// meaningful without it).
///
/// FK targets / references / indexes with no sink counterpart are NOT a refusal:
/// their source SsKey passes through unchanged, so an in-contract escape is caught
/// by the existing subset-FK gate and an out-of-contract one by the T0.3
/// `subsetForeignRefsGate`.
[<RequireQualifiedAccess>]
module NameAlignment =

    let private nameEq (a: Name) (b: Name) : bool =
        System.String.Equals(Name.value a, Name.value b, System.StringComparison.OrdinalIgnoreCase)

    let private strEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    // -- the identity rewriters — apply the source→sink map at every SsKey-bearing
    //    field; any SsKey NOT in the map (an unmapped module, a `DerivedFrom`
    //    inverse reference, an out-of-contract FK target) passes through unchanged.

    let private remap (m: Map<SsKey, SsKey>) (k: SsKey) : SsKey =
        Map.tryFind k m |> Option.defaultValue k

    let private rewriteAttribute (m: Map<SsKey, SsKey>) (a: Attribute) : Attribute =
        { a with SsKey = remap m a.SsKey }

    let private rewriteReference (m: Map<SsKey, SsKey>) (r: Reference) : Reference =
        { r with
            SsKey           = remap m r.SsKey
            SourceAttribute = remap m r.SourceAttribute
            TargetKind      = remap m r.TargetKind }

    let private rewriteIndex (m: Map<SsKey, SsKey>) (i: Index) : Index =
        { i with
            SsKey           = remap m i.SsKey
            Columns         = i.Columns |> List.map (fun c -> { c with Attribute = remap m c.Attribute })
            IncludedColumns = i.IncludedColumns |> List.map (remap m) }

    let private rewriteKind (m: Map<SsKey, SsKey>) (k: Kind) : Kind =
        { k with
            SsKey      = remap m k.SsKey
            Attributes = k.Attributes |> List.map (rewriteAttribute m)
            References = k.References |> List.map (rewriteReference m)
            Indexes    = k.Indexes    |> List.map (rewriteIndex m) }

    let private rewriteModule (m: Map<SsKey, SsKey>) (mo: Module) : Module =
        { mo with
            SsKey = remap m mo.SsKey
            Kinds = mo.Kinds |> List.map (rewriteKind m) }

    let private rewriteCatalog (m: Map<SsKey, SsKey>) (c: Catalog) : Catalog =
        { c with
            Modules   = c.Modules   |> List.map (rewriteModule m)
            Sequences = c.Sequences |> List.map (fun s -> { s with SsKey = remap m s.SsKey }) }

    /// Rewrite `source` so its SsKeys equal `sink`'s by name, within `alignMap`
    /// (source-module-name → sink-module-name). See the module docstring.
    let align (alignMap: Map<string, string>) (source: Catalog) (sink: Catalog) : Result<Catalog> =
        // An empty map is not identity — under `ByName` it would silently no-op
        // and hand a misleading shape refusal downstream. Refuse BY NAME so a
        // programmatic caller (and the config path's mirror guard) fail loud.
        if Map.isEmpty alignMap then
            Result.failureOf
                (ValidationError.create "alignment.mapEmpty"
                    "name-alignment requires a non-empty source->sink module map.")
        else
        // Match + shape-check on the ESPACE-SAFE logical shape — the SAME
        // normalization the shape gate uses (`Readiness.toLogicalShape`): it
        // blanks the physical-realization artifacts (default-constraint NAME,
        // triggers, column checks) that OutSystems derives from the physical
        // table name and that legitimately differ between cells. SsKeys and
        // logical names are preserved, so the correspondence map built here
        // applies verbatim to the RAW `source` rewritten at the end.
        let srcL = Readiness.toLogicalShape source
        let snkL = Readiness.toLogicalShape sink
        // -- 1. Module correspondence. Validate each declared pair has BOTH
        //    sides; a missing side short-circuits (nothing else is meaningful).
        let moduleErrors = ResizeArray<ValidationError>()
        let modulePairs = ResizeArray<Module * Module>()   // (source module, sink module)
        for KeyValue (srcModName, sinkModName) in alignMap do
            let srcMod  = srcL.Modules |> List.tryFind (fun m -> strEq (Name.value m.Name) srcModName)
            let sinkMod = snkL.Modules |> List.tryFind (fun m -> strEq (Name.value m.Name) sinkModName)
            match srcMod, sinkMod with
            | Some s, Some k -> modulePairs.Add (s, k)
            | None, _ ->
                moduleErrors.Add
                    (ValidationError.create "alignment.module.unmatched"
                        (System.String.Concat
                            [ "the align map names source module '"; srcModName
                              "', which is not present in the source estate." ]))
            | _, None ->
                moduleErrors.Add
                    (ValidationError.create "alignment.module.unmatched"
                        (System.String.Concat
                            [ "the align map maps source module '"; srcModName; "' to sink module '"
                              sinkModName; "', which is not present in the sink estate." ]))
        if moduleErrors.Count > 0 then Result.failure (List.ofSeq moduleErrors) else

        // -- 2. Per mapped pair: name-match entities + attributes (strict shape),
        //    building ONE source→sink SsKey map; collect all mismatches.
        let entityErrors = ResizeArray<ValidationError>()
        let pairs = ResizeArray<SsKey * SsKey>()
        for (srcMod, sinkMod) in modulePairs do
            pairs.Add (srcMod.SsKey, sinkMod.SsKey)
            for srcKind in srcMod.Kinds do
                let candidates = sinkMod.Kinds |> List.filter (fun k -> nameEq k.Name srcKind.Name)
                match candidates with
                | [] ->
                    entityErrors.Add
                        (ValidationError.create "alignment.entity.unmatched"
                            (System.String.Concat
                                [ "entity '"; Name.value srcKind.Name; "' (source module '"
                                  Name.value srcMod.Name; "') has no same-named entity in the mapped sink module '"
                                  Name.value sinkMod.Name; "'." ]))
                | _ :: _ :: _ ->
                    entityErrors.Add
                        (ValidationError.create "alignment.entity.ambiguous"
                            (System.String.Concat
                                [ "entity '"; Name.value srcKind.Name; "' matches "
                                  string (List.length candidates); " entities in the mapped sink module '"
                                  Name.value sinkMod.Name; "' — the correspondence is ambiguous." ]))
                | [ sinkKind ] ->
                    pairs.Add (srcKind.SsKey, sinkKind.SsKey)
                    // attributes — strict shape via the shared diff comparator.
                    for srcAttr in srcKind.Attributes do
                        match sinkKind.Attributes |> List.tryFind (fun a -> nameEq a.Name srcAttr.Name) with
                        | None ->
                            entityErrors.Add
                                (ValidationError.create "alignment.attribute.unmatched"
                                    (System.String.Concat
                                        [ Name.value srcKind.Name; "."; Name.value srcAttr.Name
                                          " has no same-named attribute on the matched sink entity." ]))
                        | Some sinkAttr ->
                            let facets = CatalogDiff.attributeShapeFacets srcAttr sinkAttr
                            if Set.isEmpty facets then
                                pairs.Add (srcAttr.SsKey, sinkAttr.SsKey)
                            else
                                let facetText =
                                    facets |> Set.toList |> List.map (sprintf "%A") |> String.concat ", "
                                entityErrors.Add
                                    (ValidationError.create "alignment.attribute.shapeDivergence"
                                        (System.String.Concat
                                            [ Name.value srcKind.Name; "."; Name.value srcAttr.Name
                                              " differs from the sink on: "; facetText
                                              " — a cloned entity's attributes must be identical." ]))
                    // references — best-effort name match (no refusal); a source-only
                    // reference keeps its SsKey and falls to the FK gates downstream.
                    for srcRef in srcKind.References do
                        match sinkKind.References |> List.tryFind (fun r -> nameEq r.Name srcRef.Name) with
                        | Some sinkRef -> pairs.Add (srcRef.SsKey, sinkRef.SsKey)
                        | None -> ()
                    // indexes — best-effort name match (no refusal).
                    for srcIdx in srcKind.Indexes do
                        match sinkKind.Indexes |> List.tryFind (fun i -> nameEq i.Name srcIdx.Name) with
                        | Some sinkIdx -> pairs.Add (srcIdx.SsKey, sinkIdx.SsKey)
                        | None -> ()

        // sequences are catalog-level — best-effort name match (no refusal).
        for srcSeq in srcL.Sequences do
            match snkL.Sequences |> List.tryFind (fun s -> nameEq s.Name srcSeq.Name) with
            | Some sinkSeq -> pairs.Add (srcSeq.SsKey, sinkSeq.SsKey)
            | None -> ()

        if entityErrors.Count > 0 then Result.failure (List.ofSeq entityErrors) else

        // -- 3. Rewrite the source catalog through the correspondence map. Any
        //    unmapped SsKey passes through, so unmapped modules ride unchanged.
        let map = pairs |> List.ofSeq |> Map.ofList
        Result.success (rewriteCatalog map source)

    /// The mode router — the ONE call the peer face and the go board both make so
    /// they align on the SAME fact (board/engine two-traversal parity). `BySsKey`
    /// is identity (`Ok source`, byte-identical to today); `ByName` runs the pass.
    let alignForMode
        (mode: AlignmentMode)
        (alignMap: Map<string, string>)
        (source: Catalog)
        (sink: Catalog)
        : Result<Catalog> =
        match mode with
        | AlignmentMode.BySsKey -> Result.success source
        | AlignmentMode.ByName  -> align alignMap source sink
