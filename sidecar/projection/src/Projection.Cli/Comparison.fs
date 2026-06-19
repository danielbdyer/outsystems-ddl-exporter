module Projection.Cli.Comparison

open Projection.Core

/// Masterful base #3 (`REPORTING_HORIZON`) — comparison as a capability. The
/// torsor itself already exists and is law-tested (`CatalogDiff.between` ⊖ +
/// `applyDiff` ⊕; the Weyl axiom `applyDiff (between A B) A = B` is proven in
/// `CatalogDiffTests`). This primitive *unifies* the codebase's two diff
/// surfaces under one capability and connects them to the `View` substrate, so
/// a generic `diff` / drift / migrate verb is a thin `Render (Between a b)`.
///
/// The discriminating predicate lives in the type: **`Apply` is present iff
/// the delta is a torsor element** (replayable — `Some` for `Catalog`,
/// reconstructing the target from base + facets) and **absent iff the delta is
/// a lossy comparison quotient** (`None` for `PhysicalSchema`, which projects
/// out structure — see `DECISIONS 2026-06-04`). A consumer that needs to
/// *replay* a delta (migrate) can only do so where the type grants `Apply`.
type Comparison<'a, 'delta> = {
    /// ⊖ — observe the delta between two states. Failable (a malformed catalog
    /// can't be diffed); total comparisons wrap in `Ok`.
    Between : 'a -> 'a -> Result<'delta, string>
    IsEmpty : 'delta -> bool
    /// Project the delta onto the `View` substrate (→ pretty / plain / json).
    Render  : 'delta -> View.View
    /// ⊕ — the torsor action, present exactly when the delta is replayable.
    Apply   : ('delta -> 'a -> 'a) option
}

// --- delta → View projections (count-level summaries) ----------------------

let private countField (label: string) (n: int) : View.View =
    View.Field(label, string n, (if n = 0 then View.Neutral else View.Warn))

/// Render a `CatalogDiff` summary: the total change count plus the per-channel
/// added / dropped / renamed / changed breakdown, in plain words (the
/// legibility axiom holds at depth — `THE_VOICE.md` §2.1; the `‖δ‖ norm` the
/// engine computes never reaches the operator as a glyph).
let renderCatalogDiff (d: CatalogDiff) : View.View =
    let c = CatalogDiff.channelCounts d
    let n = CatalogDiff.norm d
    let h = Theme.humane
    let chan a r rn ch = sprintf "%s added · %s dropped · %s renamed · %s changed" (h a) (h r) (h rn) (h ch)
    View.Panel(
        "changes",
        [ View.PanelRow.Labeled("total changes", h n, (if n = 0 then View.Ok else View.Warn))
          View.PanelRow.Labeled("tables", sprintf "%s added · %s dropped · %s renamed" (h c.AddedKinds) (h c.RemovedKinds) (h c.RenamedKinds), View.Neutral)
          View.PanelRow.Labeled("columns", chan c.AddedAttributes c.RemovedAttributes c.RenamedAttributes c.ChangedAttributes, View.Neutral)
          View.PanelRow.Labeled("relationships", chan c.AddedReferences c.RemovedReferences c.RenamedReferences c.ChangedReferences, View.Neutral)
          View.PanelRow.Labeled("indexes", chan c.AddedIndexes c.RemovedIndexes c.RenamedIndexes c.ChangedIndexes, View.Neutral)
          View.PanelRow.Labeled("sequences", chan c.AddedSequences c.RemovedSequences c.RenamedSequences c.ChangedSequences, View.Neutral) ])

// --- the statement: the plain lead line of a change (INSTRUMENT slice 1) ----

/// The statement of a catalog change — the one plain line that leads the surface
/// (`THE_VOICE.md` §1 rule 3 — statement first, the substantiation beneath). A
/// destructive change leads amber ("review first"); an additive / no-op change
/// leads calm. The substantiation (the move-lanes + the per-channel ‖δ‖ panel,
/// progressively disclosed) is shown beneath. (Copy itself is the Act-2 diff
/// surface — payload-shaped, voiced to the twelve-rule register in a later
/// slice; this rename only aligns the field vocabulary, per `DECISIONS 2026-06-06`.)
let catalogStatement (d: CatalogDiff) : View.View =
    let c = CatalogDiff.channelCounts d
    let removed =
        c.RemovedKinds + c.RemovedAttributes + c.RemovedReferences
        + c.RemovedIndexes + c.RemovedSequences
    let n = CatalogDiff.norm d
    if n = 0 then View.Hero(View.Ok, "No differences found. The two states are identical.")
    elif removed > 0 then
        View.Hero(View.Warn, sprintf "%d changes · %d drops · review before applying" n removed)
    else
        View.Hero(View.Ok, sprintf "%d changes · no removals" n)

// --- the per-channel facet vocabulary (which facet of a reshape changed) ----
// One word per facet, in the plain register (`THE_VOICE.md` §2.1 — the engine's
// `‖δ‖` / facet DU never reaches the operator as a glyph). The before/after
// *value* of each facet (`type int → bigint`) is the delta-grade follow-on; this
// slice names WHICH facet moved, across every channel the diff computes.

/// One attribute facet, in plain words (for the reshape lane).
let private attributeFacetText (f: AttributeFacet) : string =
    match f with
    | AttributeFacet.DataType     -> "type"
    | AttributeFacet.Nullability  -> "nullability"
    | AttributeFacet.PrimaryKey   -> "primary key"
    | AttributeFacet.Length       -> "length"
    | AttributeFacet.Precision    -> "precision"
    | AttributeFacet.Scale        -> "scale"
    | AttributeFacet.Identity     -> "identity"
    | AttributeFacet.DefaultValue -> "default"
    | AttributeFacet.Computed     -> "computed"

/// One reference (FK) facet, in plain words.
let private referenceFacetText (f: ReferenceFacet) : string =
    match f with
    | ReferenceFacet.Target          -> "target"
    | ReferenceFacet.SourceAttribute -> "source column"
    | ReferenceFacet.OnDelete        -> "on delete"
    | ReferenceFacet.OnUpdate        -> "on update"
    | ReferenceFacet.UserFk          -> "user fk"
    | ReferenceFacet.DbConstraint    -> "db constraint"
    | ReferenceFacet.Trust           -> "trust"

/// One index facet, in plain words.
let private indexFacetText (f: IndexFacet) : string =
    match f with
    | IndexFacet.Columns         -> "columns"
    | IndexFacet.Uniqueness      -> "uniqueness"
    | IndexFacet.IncludedColumns -> "included columns"
    | IndexFacet.Filter          -> "filter"
    | IndexFacet.DataSpace       -> "data space"
    | IndexFacet.Options         -> "options"

/// One sequence facet, in plain words.
let private sequenceFacetText (f: SequenceFacet) : string =
    match f with
    | SequenceFacet.Schema     -> "schema"
    | SequenceFacet.DataType   -> "type"
    | SequenceFacet.StartValue -> "start"
    | SequenceFacet.Increment  -> "increment"
    | SequenceFacet.Minimum    -> "minimum"
    | SequenceFacet.Maximum    -> "maximum"
    | SequenceFacet.Cycle      -> "cycle"
    | SequenceFacet.Cache      -> "cache"

/// One kind-OWN facet (the table's own shape — NM-17), in plain words.
let private kindFacetText (f: KindFacet) : string =
    match f with
    | KindFacet.Modality     -> "modality"
    | KindFacet.Triggers     -> "triggers"
    | KindFacet.ColumnChecks -> "checks"
    | KindFacet.IsActive     -> "active"

/// Join a facet set in plain words. Deterministic: `Set.toList` is sorted (T1).
let private facetsJoin (render: 'f -> string) (facets: Set<'f>) : string =
    facets |> Set.toList |> List.map render |> String.concat ", "

// --- before/after value rendering (the delta-grade evidence) ----------------
// A reshape names which facet moved AND, for a scalar facet, shows the value it
// moved between (`type text → integer`) — the per-item before/after evidence an
// operator reads an ALTER by. Option-valued facets (default / computed) render
// the presence transition (added / removed / changed); the literal / config is
// the deeper dig, not the lane line. The values resolve from the diff's RETAINED
// source / target catalogs (`CatalogDiff.source` / `.target`) — the diff already
// carries both states, so no Core change is needed to read the value.
//
// (Concrete-storage width — `int → bigint` — is a Core diff modulus, not a render
// gap: `changedFacets` compares the semantic `PrimitiveType` [`Integer`], not the
// concrete storage type, so int↔bigint produces no facet to render. Widening that
// is a `CatalogDiff` change, out of this Cli-only scope.)

let private yesNo (b: bool) : string = if b then "yes" else "no"
let private nullText (nullable: bool) : string = if nullable then "null" else "not null"
let private intOpt (o: int option) : string = match o with Some n -> string n | None -> "—"
let private lenOpt (o: int option) : string = match o with Some n -> string n | None -> "max"

let private primitiveTypeText (t: PrimitiveType) : string =
    match t with
    | Integer -> "integer" | Decimal  -> "decimal"  | Text -> "text"
    | Boolean -> "boolean" | DateTime -> "datetime" | Date -> "date"
    | Time    -> "time"    | Binary   -> "binary"   | Guid -> "guid"

/// Presence transition for an option-valued facet, keyed on whether each side
/// HAS a value: None→Some = added, Some→None = removed, otherwise = changed.
/// (Both-`None` is reachable when only a default's NAME moved — the value stayed
/// absent but the facet fired; `changed` is the honest reading there.)
let private presence (sHas: bool) (tHas: bool) : string =
    match sHas, tHas with
    | false, true -> "added"
    | true, false -> "removed"
    | _           -> "changed"

let private referenceActionText (a: ReferenceAction) : string =
    match a with
    | NoAction -> "no action" | Cascade -> "cascade" | SetNull -> "set null" | Restrict -> "restrict"

let private referenceActionOpt (a: ReferenceAction option) : string =
    match a with Some x -> referenceActionText x | None -> "unset"

/// One attribute facet's `word before → after` evidence (the column ALTER surface).
let private attributeEvidence (s: Attribute) (t: Attribute) (f: AttributeFacet) : string =
    match f with
    | AttributeFacet.DataType     -> sprintf "type %s → %s" (primitiveTypeText s.Type) (primitiveTypeText t.Type)
    | AttributeFacet.Nullability  -> sprintf "nullability %s → %s" (nullText s.Column.IsNullable) (nullText t.Column.IsNullable)
    | AttributeFacet.PrimaryKey   -> sprintf "primary key %s → %s" (yesNo s.IsPrimaryKey) (yesNo t.IsPrimaryKey)
    | AttributeFacet.Length       -> sprintf "length %s → %s" (lenOpt s.Length) (lenOpt t.Length)
    | AttributeFacet.Precision    -> sprintf "precision %s → %s" (intOpt s.Precision) (intOpt t.Precision)
    | AttributeFacet.Scale        -> sprintf "scale %s → %s" (intOpt s.Scale) (intOpt t.Scale)
    | AttributeFacet.Identity     -> sprintf "identity %s → %s" (yesNo s.IsIdentity) (yesNo t.IsIdentity)
    | AttributeFacet.DefaultValue -> sprintf "default %s" (presence (Option.isSome s.DefaultValue) (Option.isSome t.DefaultValue))
    | AttributeFacet.Computed     -> sprintf "computed %s" (presence (Option.isSome s.Computed) (Option.isSome t.Computed))

/// One reference (FK) facet's `word before → after` evidence (the FK ALTER surface).
let private referenceEvidence (s: Reference) (t: Reference) (f: ReferenceFacet) : string =
    match f with
    | ReferenceFacet.Target          -> sprintf "target %s → %s" (SsKey.rootOriginal s.TargetKind) (SsKey.rootOriginal t.TargetKind)
    | ReferenceFacet.SourceAttribute -> sprintf "source column %s → %s" (SsKey.rootOriginal s.SourceAttribute) (SsKey.rootOriginal t.SourceAttribute)
    | ReferenceFacet.OnDelete        -> sprintf "on delete %s → %s" (referenceActionText s.OnDelete) (referenceActionText t.OnDelete)
    | ReferenceFacet.OnUpdate        -> sprintf "on update %s → %s" (referenceActionOpt s.OnUpdate) (referenceActionOpt t.OnUpdate)
    | ReferenceFacet.UserFk          -> sprintf "user fk %s → %s" (yesNo s.IsUserFk) (yesNo t.IsUserFk)
    | ReferenceFacet.DbConstraint    -> sprintf "db constraint %s → %s" (yesNo (Reference.hasDbConstraint s)) (yesNo (Reference.hasDbConstraint t))
    | ReferenceFacet.Trust           -> sprintf "trust %s → %s" (yesNo (Reference.isConstraintTrusted s)) (yesNo (Reference.isConstraintTrusted t))

/// Resolve a child entity within a kind by SsKey, in a given catalog — the
/// before/after lookup for reshape evidence. Partially applied per kind at the
/// call site (`let find = findChild … kk` → `find source key` / `find target key`).
let private findChild (entitiesOf: Kind -> 'e list) (keyOf: 'e -> SsKey) (kindKey: SsKey) (cat: Catalog) (entityKey: SsKey) : 'e option =
    Catalog.tryFindKind kindKey cat
    |> Option.bind (fun k -> entitiesOf k |> List.tryFind (fun e -> keyOf e = entityKey))

// --- name resolution -------------------------------------------------------
// The lanes read by NAME (`column Customer.Email`), not raw SsKey: an
// `OssysOriginal` key's `rootOriginal` is a bare GUID (`CatalogReader`), so a
// per-column / per-FK changeset keyed by `rootOriginal` would be a wall of GUIDs
// on a real OSSYS estate. The rename lane already names by `Name`; the add /
// remove / reshape lanes now match it. A flat SsKey → Name index is built ONCE
// per side (discover-once, derive-pure — CLAUDE.md §6) and shared by every
// collector; `rootOriginal` is the fallback for a key absent from the catalog
// (defensive — every diffed key resolves in its own side).

let private nameIndex (cat: Catalog) : Map<SsKey, string> =
    seq {
        for k in Catalog.allKinds cat do
            yield k.SsKey, Name.value k.Name
            for a in k.Attributes do yield a.SsKey, Name.value a.Name
            for r in k.References  do yield r.SsKey, Name.value r.Name
            for i in k.Indexes     do yield i.SsKey, Name.value i.Name
        for s in cat.Sequences do yield s.SsKey, Name.value s.Name
    }
    |> Map.ofSeq

let private nm (names: Map<SsKey, string>) (key: SsKey) : string =
    Map.tryFind key names |> Option.defaultValue (SsKey.rootOriginal key)

// --- the per-channel move collectors ---------------------------------------
// Every child entity is qualified by its owning kind (`column Customer.Email`),
// so a single move-lane stays legible while carrying every channel at once. The
// channel nouns match `renderCatalogDiff`'s panel vocabulary (tables / columns /
// relationships / indexes / sequences). `names` is the side the entity lives on:
// target for adds, source for removes / reshapes / the rename qualifier.

let private qualify (noun: string) (names: Map<SsKey, string>) (kindKey: SsKey) (entityKey: SsKey) : string =
    sprintf "%s %s.%s" noun (nm names kindKey) (nm names entityKey)

let private channelAdds (noun: string) (names: Map<SsKey, string>) (diffs: Map<SsKey, ChannelDiff<'c>>) : string list =
    diffs |> Map.toList
    |> List.collect (fun (kk, cd) -> cd.Added |> Set.toList |> List.map (qualify noun names kk))

let private channelRemoves (noun: string) (names: Map<SsKey, string>) (diffs: Map<SsKey, ChannelDiff<'c>>) : string list =
    diffs |> Map.toList
    |> List.collect (fun (kk, cd) -> cd.Removed |> Set.toList |> List.map (qualify noun names kk))

let private channelRenames (noun: string) (names: Map<SsKey, string>) (diffs: Map<SsKey, ChannelDiff<'c>>) : string list =
    diffs |> Map.toList
    |> List.collect (fun (kk, cd) ->
        cd.Renamed |> Map.toList
        |> List.map (fun (_, r) ->
            sprintf "%s %s.%s → %s" noun (nm names kk) (Name.value r.OldName) (Name.value r.NewName)))

/// A per-kind channel's reshaped items in the FACET-NAME form — `noun
/// Kind.Entity: facet, facet`. Used for the structural channels (index) whose
/// facets are lists / grouped knobs, where a before→after value would be a wall;
/// attributes and references carry the richer before/after evidence instead.
/// `keyOf` / `facetsOf` project the change record (annotated at the call sites
/// because `.Facets` is shared across the four change records).
let private channelReshapes
    (noun: string)
    (names: Map<SsKey, string>)
    (render: 'f -> string)
    (keyOf: 'c -> SsKey)
    (facetsOf: 'c -> Set<'f>)
    (diffs: Map<SsKey, ChannelDiff<'c>>)
    : string list =
    diffs |> Map.toList
    |> List.collect (fun (kk, cd) ->
        cd.Reshaped
        |> List.map (fun c ->
            sprintf "%s %s.%s: %s" noun (nm names kk) (nm names (keyOf c)) (facetsJoin render (facetsOf c))))

/// The move-typed lanes of a catalog change — rename / reshape / add / remove,
/// each a `View.Lane` badged by reversibility: rename + add are reversible-safe
/// (Ok); reshape may rewrite data (Warn — review); remove destroys structure
/// (Bad). Each lane stays HOMOGENEOUS (one status badge over plain items — the
/// move IS the status, never per-item), but now carries EVERY channel the diff
/// computes: kinds + columns + relationships + indexes + sequences + the kind's
/// own facets (modality / triggers / checks / activation). Items are qualified
/// by channel + owning kind, so a multi-channel lane reads. The diff already
/// computed all of this (`CatalogDiff` C1 + NM-17); this surfaces it onto the
/// walkable changeset the L2 Navigator digs.
let renderCatalogLanes (d: CatalogDiff) : View.View list =
    let source    = CatalogDiff.source d
    let target    = CatalogDiff.target d
    let srcNames  = nameIndex source
    let tgtNames  = nameIndex target
    let attrDiffs = CatalogDiff.attributeDiffs d
    let refDiffs  = CatalogDiff.referenceDiffs d
    let idxDiffs  = CatalogDiff.indexDiffs d
    let seqd      = CatalogDiff.sequenceDiff d

    // Kind-level moves (the top-level presence/name partition). Added kinds name
    // from the target, removed from the source — the side each lives on.
    let kindRenames =
        CatalogDiff.renamed d |> Map.toList
        |> List.map (fun (_, r) -> sprintf "table %s → %s" (Name.value r.OldName) (Name.value r.NewName))
    let kindAdds    = CatalogDiff.added d   |> Set.toList |> List.map (fun k -> sprintf "table %s" (nm tgtNames k))
    let kindRemoves = CatalogDiff.removed d |> Set.toList |> List.map (fun k -> sprintf "table %s" (nm srcNames k))

    // Catalog-level sequence channel (sequences are not kind-scoped → no qualifier).
    let seqAdds    = seqd.Added   |> Set.toList |> List.map (fun k -> sprintf "sequence %s" (nm tgtNames k))
    let seqRemoves = seqd.Removed |> Set.toList |> List.map (fun k -> sprintf "sequence %s" (nm srcNames k))
    let seqRenames =
        seqd.Renamed |> Map.toList
        |> List.map (fun (_, r) -> sprintf "sequence %s → %s" (Name.value r.OldName) (Name.value r.NewName))
    let seqReshapes =
        seqd.Reshaped
        |> List.map (fun c -> sprintf "sequence %s: %s" (nm srcNames c.SequenceKey) (facetsJoin sequenceFacetText c.Facets))

    // Kind-OWN facets (modality / triggers / checks / activation) — a reshape of
    // the table itself, not its children.
    let kindFacetReshapes =
        CatalogDiff.kindFacetDiffs d |> Map.toList
        |> List.map (fun (kk, facets) -> sprintf "table %s: %s" (nm srcNames kk) (facetsJoin kindFacetText facets))

    // The two ALTER surfaces an operator reviews — columns and FKs — carry the
    // before/after EVIDENCE (`text → integer`), resolved from the retained
    // source/target. The body falls back to the facet word if either side can't
    // be resolved (defensive; a reshape always has both). Index reshapes keep the
    // facet-name form (their facets are lists/grouped — see `channelReshapes`).
    let attrReshapes =
        attrDiffs |> Map.toList
        |> List.collect (fun (kk, ad) ->
            let find = findChild (fun (k: Kind) -> k.Attributes) (fun (a: Attribute) -> a.SsKey) kk
            ad.Reshaped
            |> List.map (fun c ->
                match find source c.AttributeKey, find target c.AttributeKey with
                | Some s, Some t ->
                    let body = c.Facets |> Set.toList |> List.map (attributeEvidence s t) |> String.concat ", "
                    sprintf "column %s.%s: %s" (nm srcNames kk) (Name.value s.Name) body
                | _ ->
                    sprintf "column %s.%s: %s" (nm srcNames kk) (nm srcNames c.AttributeKey) (facetsJoin attributeFacetText c.Facets)))
    let refReshapes =
        refDiffs |> Map.toList
        |> List.collect (fun (kk, rd) ->
            let find = findChild (fun (k: Kind) -> k.References) (fun (r: Reference) -> r.SsKey) kk
            rd.Reshaped
            |> List.map (fun c ->
                match find source c.ReferenceKey, find target c.ReferenceKey with
                | Some s, Some t ->
                    let body = c.Facets |> Set.toList |> List.map (referenceEvidence s t) |> String.concat ", "
                    sprintf "relationship %s.%s: %s" (nm srcNames kk) (Name.value s.Name) body
                | _ ->
                    sprintf "relationship %s.%s: %s" (nm srcNames kk) (nm srcNames c.ReferenceKey) (facetsJoin referenceFacetText c.Facets)))

    // Assemble each move across every channel, in the count-panel's channel order
    // (tables, columns, relationships, indexes, sequences).
    let renames =
        kindRenames
        @ channelRenames "column" srcNames attrDiffs
        @ channelRenames "relationship" srcNames refDiffs
        @ channelRenames "index" srcNames idxDiffs
        @ seqRenames
    let reshapes =
        attrReshapes
        @ refReshapes
        @ channelReshapes "index" srcNames indexFacetText (fun (c: IndexChange) -> c.IndexKey) (fun (c: IndexChange) -> c.Facets) idxDiffs
        @ seqReshapes
        @ kindFacetReshapes
    let adds =
        kindAdds
        @ channelAdds "column" tgtNames attrDiffs
        @ channelAdds "relationship" tgtNames refDiffs
        @ channelAdds "index" tgtNames idxDiffs
        @ seqAdds
    let removes =
        kindRemoves
        @ channelRemoves "column" srcNames attrDiffs
        @ channelRemoves "relationship" srcNames refDiffs
        @ channelRemoves "index" srcNames idxDiffs
        @ seqRemoves

    let lane glyph label st items =
        if List.isEmpty items then [] else [ View.Lane(glyph, label, st, items) ]
    lane "⟲" "rename" View.Ok renames
    @ lane "~" "reshape" View.Warn reshapes
    @ lane "+" "add" View.Ok adds
    @ lane "−" "remove" View.Bad removes

/// A catalog change as a `Surface` — the statement over the substantiation: the
/// move-typed lanes (kind moves: rename / add / remove, each badged by
/// reversibility, progressively disclosed) with the per-channel ‖δ‖ panel
/// beneath. The statement/substantiation shape every later surface reuses
/// (`THE_VOICE.md` §1 rule 3).
let changeSurface (d: CatalogDiff) : Surface.Surface =
    { Statement      = catalogStatement d
      Substantiation = renderCatalogLanes d @ [ View.Blank; renderCatalogDiff d ]
      Action         = None }

/// A catalog change rendered statement-first: the plain verdict, then the
/// substantiation — the move-typed lanes and the per-channel ‖δ‖ panel, revealed
/// on demand.
let renderCatalogChange (d: CatalogDiff) : View.View =
    Surface.render (changeSurface d)

/// Render a `PhysicalSchemaDiff` summary: identical / diverged, plus per-axis
/// −missing / +extra counts.
let renderPhysicalDiff (d: PhysicalSchemaDiff) : View.View =
    let isEq = PhysicalSchema.isEqual d
    let axis label (missing: int) (extra: int) : View.PanelRow =
        View.PanelRow.Labeled(label, sprintf "−%d +%d" missing extra, (if missing + extra > 0 then View.Warn else View.Neutral))
    View.Panel(
        "physical Δ",
        [ View.PanelRow.Labeled("status", (if isEq then "identical" else "diverged"), (if isEq then View.Ok else View.Bad))
          axis "columns" d.MissingColumns.Length d.ExtraColumns.Length
          axis "foreignKeys" d.MissingForeignKeys.Length d.ExtraForeignKeys.Length
          axis "indexes" d.MissingIndexes.Length d.ExtraIndexes.Length
          axis "rows" d.MissingRows.Length d.ExtraRows.Length
          axis "annotations" d.MissingAnnotations.Length d.ExtraAnnotations.Length ])

// --- the two instances -----------------------------------------------------

/// `Catalog` comparison — a genuine torsor: `Apply = Some applyDiff` (the
/// delta replays to reconstruct the target; Weyl-proven).
let catalog : Comparison<Catalog, CatalogDiff> =
    { Between = (fun a b -> Ok (CatalogDiff.between a b))
      IsEmpty = CatalogDiff.isEmpty
      Render  = renderCatalogDiff
      Apply   = Some (fun d a -> CatalogDiff.applyDiff a d) }

/// `PhysicalSchema` comparison — a lossy quotient: `Apply = None` (the diff
/// projects out structure; it observes divergence but does not replay).
let physicalSchema : Comparison<PhysicalSchema, PhysicalSchemaDiff> =
    { Between = (fun a b -> Ok (PhysicalSchema.diff a b))
      IsEmpty = PhysicalSchema.isEqual
      Render  = renderPhysicalDiff
      Apply   = None }

/// Generic summary: observe the delta between two states and project it onto
/// the `View` substrate. The thin core a `diff` / drift verb would call.
let summary (cmp: Comparison<'a, 'delta>) (a: 'a) (b: 'a) : Result<View.View, string> =
    match cmp.Between a b with
    | Ok d    -> Ok (cmp.Render d)
    | Error e -> Error e
