namespace Projection.Pipeline
// LINT-ALLOW-FILE: cross-environment name alignment — the ResizeArrays are
//   single-pass accumulators pairing source↔sink modules/entities (an imperative
//   fold building the match set, isolated and pure-pool-tested); `String.concat`
//   composes a terminal facet-name list inside a divergence error message. No
//   pure equivalent for the paired accumulation; no AST for the error prose.

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

    let private strEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    let private nameEq (a: Name) (b: Name) : bool = strEq (Name.value a) (Name.value b)

    /// Operator-facing name for a changed attribute facet — THE_VOICE register,
    /// no raw `%A` reflection output in a refusal message.
    let private facetName (f: AttributeFacet) : string =
        match f with
        | AttributeFacet.DataType     -> "data type"
        | AttributeFacet.Nullability  -> "nullability"
        | AttributeFacet.PrimaryKey   -> "primary-key flag"
        | AttributeFacet.Length       -> "length"
        | AttributeFacet.Precision    -> "precision"
        | AttributeFacet.Scale        -> "scale"
        | AttributeFacet.Identity     -> "identity flag"
        | AttributeFacet.DefaultValue -> "default value"
        | AttributeFacet.Computed     -> "computed expression"

    /// Best-effort by-name correspondence into `pairs`: each source element that
    /// UNIQUELY name-matches a sink element contributes its SsKey pair; no match
    /// is silently skipped (the identity-collision guard below catches any
    /// many-to-one). Shared by the reference / index / sequence channels.
    let private addByNameInto
        (pairs: ResizeArray<SsKey * SsKey>)
        (nameOf: 'a -> Name)
        (keyOf: 'a -> SsKey)
        (srcs: 'a list)
        (snks: 'a list)
        : unit =
        for s in srcs do
            match snks |> List.tryFind (fun t -> nameEq (nameOf t) (nameOf s)) with
            | Some t -> pairs.Add (keyOf s, keyOf t)
            | None -> ()

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
    /// (source-module-name → sink-module-name). `strictScope` is the set of
    /// SOURCE entity SsKeys actually being transferred (the resolved load set):
    /// an entity in it must be a CLEAN clone (name + strict shape) or the pass
    /// refuses; an entity OUTSIDE it (an unrelated table in a mapped module) is
    /// still re-keyed by name where it uniquely matches — so FK targets stay
    /// consistent — but a name/shape mismatch there is NOT a refusal (its data
    /// is not moving). `None` = a full transfer: strict over the whole estate.
    /// See the module docstring.
    let align
        (alignMap: Map<string, string>)
        (strictScope: Set<SsKey> option)
        (source: Catalog)
        (sink: Catalog)
        : Result<Catalog> =
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
        // Is this source entity being transferred (⇒ strict), or merely a
        // neighbor in a mapped module (⇒ best-effort re-key, never a refusal)?
        let isStrict (k: Kind) : bool =
            match strictScope with
            | None -> true
            | Some keys -> Set.contains k.SsKey keys
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
                let strict = isStrict srcKind
                let candidates = sinkMod.Kinds |> List.filter (fun k -> nameEq k.Name srcKind.Name)
                match candidates with
                | [] ->
                    // No correspondent. Strict (transferred) ⇒ refuse; a neighbor
                    // ⇒ leave it unmapped (an in-scope FK to it becomes an
                    // out-of-contract escape the T0.3 gate owns).
                    if strict then
                        entityErrors.Add
                            (ValidationError.create "alignment.entity.unmatched"
                                (System.String.Concat
                                    [ "entity '"; Name.value srcKind.Name; "' (source module '"
                                      Name.value srcMod.Name; "') has no same-named entity in the mapped sink module '"
                                      Name.value sinkMod.Name; "'." ]))
                | _ :: _ :: _ ->
                    // Ambiguous. Strict ⇒ refuse (cannot re-key the transferred
                    // entity); a neighbor ⇒ skip (leave unmapped rather than guess).
                    if strict then
                        entityErrors.Add
                            (ValidationError.create "alignment.entity.ambiguous"
                                (System.String.Concat
                                    [ "entity '"; Name.value srcKind.Name; "' matches "
                                      string (List.length candidates); " entities in the mapped sink module '"
                                      Name.value sinkMod.Name; "' — the correspondence is ambiguous." ]))
                | [ sinkKind ] ->
                    pairs.Add (srcKind.SsKey, sinkKind.SsKey)
                    // attributes — strict shape (transferred entity) via the shared
                    // diff comparator; a neighbor re-keys by name without a shape
                    // check (the shape gate judges only the transferred set).
                    for srcAttr in srcKind.Attributes do
                        match sinkKind.Attributes |> List.tryFind (fun a -> nameEq a.Name srcAttr.Name) with
                        | None ->
                            if strict then
                                entityErrors.Add
                                    (ValidationError.create "alignment.attribute.unmatched"
                                        (System.String.Concat
                                            [ Name.value srcKind.Name; "."; Name.value srcAttr.Name
                                              " has no same-named attribute on the matched sink entity." ]))
                        | Some sinkAttr ->
                            let facets = if strict then CatalogDiff.attributeShapeFacets srcAttr sinkAttr else Set.empty
                            if Set.isEmpty facets then
                                pairs.Add (srcAttr.SsKey, sinkAttr.SsKey)
                            else
                                let facetText =
                                    facets |> Set.toList |> List.map facetName |> String.concat ", "
                                entityErrors.Add
                                    (ValidationError.create "alignment.attribute.shapeDivergence"
                                        (System.String.Concat
                                            [ Name.value srcKind.Name; "."; Name.value srcAttr.Name
                                              " differs from the sink on: "; facetText
                                              " — a cloned entity's attributes must be identical." ]))
                    // references + indexes — best-effort name match (no refusal); a
                    // source-only one keeps its SsKey and falls to the FK gates.
                    addByNameInto pairs (fun (r: Reference) -> r.Name) (fun r -> r.SsKey) srcKind.References sinkKind.References
                    addByNameInto pairs (fun (i: Index) -> i.Name) (fun i -> i.SsKey) srcKind.Indexes sinkKind.Indexes

        // Sequences are catalog-level (no module linkage), so this is a
        // best-effort by-name re-key across the whole catalog — the identity
        // collision guard below is what keeps it safe.
        addByNameInto pairs (fun (s: Sequence) -> s.Name) (fun s -> s.SsKey) srcL.Sequences snkL.Sequences

        if entityErrors.Count > 0 then Result.failure (List.ofSeq entityErrors) else

        // -- 3. Guard IDENTITY INJECTIVITY, then rewrite. `pairs` is keyed on the
        //    unique SOURCE SsKey, so `Map.ofList` would silently launder a
        //    many-to-one VALUE collision (two source identities → one sink
        //    identity — e.g. two modules pointed at one sink, or a duplicated
        //    entity/attribute name) into duplicate SsKeys that `CatalogDiff`
        //    then collapses (rows vanish). Refuse instead: a clean clone is 1:1,
        //    so this never fires on a genuine clone.
        let assoc = pairs |> List.ofSeq
        let collisions =
            assoc
            |> List.groupBy snd
            |> List.choose (fun (sinkKey, srcs) ->
                if List.length srcs > 1 then Some (sinkKey, List.length srcs) else None)
        if not (List.isEmpty collisions) then
            Result.failure
                (collisions
                 |> List.map (fun (sinkKey, n) ->
                     ValidationError.create "alignment.identityCollision"
                        (System.String.Concat
                            [ string n; " source identities align to one sink identity '"
                              SsKey.rootOriginal sinkKey
                              "' — the correspondence is not injective (two modules mapped to one, or a duplicated entity/attribute name)." ])))
        else
            Result.success (rewriteCatalog (Map.ofList assoc) source)

    /// The mode router — the ONE call the peer face and the go board both make so
    /// they align on the SAME fact (board/engine two-traversal parity). `BySsKey`
    /// is identity (`Ok source`, byte-identical to today); `ByName` resolves the
    /// declared `tables` subset against the source (the strict scope — an empty
    /// subset is a full transfer, strict estate-wide) and runs the pass. Names
    /// are rewrite-invariant, so resolving against the raw source is sound.
    let alignForMode
        (mode: AlignmentMode)
        (alignMap: Map<string, string>)
        (tables: string list)
        (source: Catalog)
        (sink: Catalog)
        : Result<Catalog> =
        match mode with
        | AlignmentMode.BySsKey -> Result.success source
        | AlignmentMode.ByName  ->
            TransferSubset.resolveLoadSet source tables
            |> Result.bind (fun strictScope -> align alignMap strictScope source sink)
