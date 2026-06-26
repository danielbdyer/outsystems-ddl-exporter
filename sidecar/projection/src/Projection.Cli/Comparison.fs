module Projection.Cli.Comparison
// LINT-ALLOW-FILE: the CLI diff-rendering module — terminal Spectre markup + evidence-string composition at the console boundary

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
/// The `Target` / `SourceAttribute` facets name a CROSS-entity SsKey (a target kind,
/// a source column) — resolved through the per-side name resolvers (`resolveSrc` /
/// `resolveTgt`, the partially-applied `nm srcNames` / `nm tgtNames`), NEVER
/// `SsKey.rootOriginal`: an `OssysOriginal` key's `rootOriginal` is a bare GUID, and
/// an FK retarget rendered `target <guid> → <guid>` is illegible exactly where the
/// operator most needs to read "this relationship now points at a different table."
let private referenceEvidence (resolveSrc: SsKey -> string) (resolveTgt: SsKey -> string) (s: Reference) (t: Reference) (f: ReferenceFacet) : string =
    match f with
    | ReferenceFacet.Target          -> sprintf "target %s → %s" (resolveSrc s.TargetKind) (resolveTgt t.TargetKind)
    | ReferenceFacet.SourceAttribute -> sprintf "source column %s → %s" (resolveSrc s.SourceAttribute) (resolveTgt t.SourceAttribute)
    | ReferenceFacet.OnDelete        -> sprintf "on delete %s → %s" (referenceActionText s.OnDelete) (referenceActionText t.OnDelete)
    | ReferenceFacet.OnUpdate        -> sprintf "on update %s → %s" (referenceActionOpt s.OnUpdate) (referenceActionOpt t.OnUpdate)
    | ReferenceFacet.UserFk          -> sprintf "user fk %s → %s" (yesNo s.IsUserFk) (yesNo t.IsUserFk)
    | ReferenceFacet.DbConstraint    -> sprintf "db constraint %s → %s" (yesNo (Reference.hasDbConstraint s)) (yesNo (Reference.hasDbConstraint t))
    | ReferenceFacet.Trust           -> sprintf "trust %s → %s" (yesNo (Reference.isConstraintTrusted s)) (yesNo (Reference.isConstraintTrusted t))

let private decimalOpt (o: decimal option) : string = match o with Some n -> string n | None -> "—"

/// An index's uniqueness, in plain words — the operationally-critical facet: a
/// `unique → not unique` drops a constraint (duplicates can now land) and the
/// reverse FAILS on apply if duplicates already exist.
let private uniquenessText (u: IndexUniqueness) : string =
    match u with NotUnique -> "not unique" | Unique -> "unique" | PrimaryKey -> "primary key"

/// One index facet's evidence. `Uniqueness` carries before → after (a clean
/// 3-state); the list / grouped facets (columns / included / filter / data space /
/// options) keep the facet-NAME form — a before→after of a column list would be a
/// wall, so they stay the deferred name (the merged delta-grade's stated reason).
let private indexEvidence (s: Index) (t: Index) (f: IndexFacet) : string =
    match f with
    | IndexFacet.Uniqueness -> sprintf "uniqueness %s → %s" (uniquenessText s.Uniqueness) (uniquenessText t.Uniqueness)
    | other                 -> indexFacetText other

/// One sequence facet's evidence — the scalar facets carry before → after
/// (`start 1 → 1000`, opposite risks from `1000 → 1`); `Cache` (mode + size,
/// grouped) keeps the facet name.
let private sequenceEvidence (s: Sequence) (t: Sequence) (f: SequenceFacet) : string =
    match f with
    | SequenceFacet.Schema     -> sprintf "schema %s → %s" s.Schema t.Schema
    | SequenceFacet.DataType   -> sprintf "type %s → %s" s.DataType t.DataType
    | SequenceFacet.StartValue -> sprintf "start %s → %s" (decimalOpt s.StartValue) (decimalOpt t.StartValue)
    | SequenceFacet.Increment  -> sprintf "increment %s → %s" (decimalOpt s.Increment) (decimalOpt t.Increment)
    | SequenceFacet.Minimum    -> sprintf "minimum %s → %s" (decimalOpt s.Minimum) (decimalOpt t.Minimum)
    | SequenceFacet.Maximum    -> sprintf "maximum %s → %s" (decimalOpt s.Maximum) (decimalOpt t.Maximum)
    | SequenceFacet.Cycle      -> sprintf "cycle %s → %s" (yesNo s.IsCycleEnabled) (yesNo t.IsCycleEnabled)
    | SequenceFacet.Cache      -> sequenceFacetText SequenceFacet.Cache

/// One kind-OWN facet's evidence. `IsActive` carries before → after (`active yes →
/// no` is a deactivation an operator must SEE, not infer from the bare word); the
/// list-shaped facets (modality / triggers / checks) keep the facet name.
let private kindFacetEvidence (s: Kind) (t: Kind) (f: KindFacet) : string =
    match f with
    | KindFacet.IsActive -> sprintf "active %s → %s" (yesNo s.IsActive) (yesNo t.IsActive)
    | other              -> kindFacetText other

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

// The flat SsKey → Name index is the shared `Catalog.nameIndex` primitive (Core) —
// the diff, the run/apply narration (`RunFaces`), and the transfer report all resolve
// through ONE projection now (the displayName chapter's consolidation).
let private nameIndex (cat: Catalog) : Map<SsKey, string> = Catalog.nameIndex cat

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

// --- the data-risk classification (delta-grade polish #C) -------------------
// The reshape lane is ONE amber bucket — a harmless `default added` sits beside a
// `null → not null` that fails or rewrites every null row. These predicates pull
// the genuinely DATA-TOUCHING subset (a change that can rewrite or LOSE existing
// row data on apply) out by name, for the honest statement count and the "review
// these first" callout. The homogeneous-lane rule is respected: the danger lives
// in a SEPARATE surface, never as a per-item status inside a lane.

/// An attribute facet transition that rewrites or risks existing row data: a type
/// conversion (truncation / cast failure), `null → not null` (rows with null fail
/// or need backfill), a primary-key change, an identity change. (A length / scale
/// NARROWING is also a truncation risk — deferred: it needs an option-aware
/// comparison; named, not silently dropped.)
let private attrFacetRewrites (s: Attribute) (t: Attribute) (f: AttributeFacet) : bool =
    match f with
    | AttributeFacet.DataType    -> true
    | AttributeFacet.Nullability -> s.Column.IsNullable && not t.Column.IsNullable
    | AttributeFacet.PrimaryKey  -> true
    | AttributeFacet.Identity    -> true
    | _ -> false

/// A reference facet transition that risks data: gaining `ON DELETE CASCADE` (a
/// future delete now cascades to child rows).
let private refFacetRewrites (s: Reference) (t: Reference) (f: ReferenceFacet) : bool =
    match f with
    | ReferenceFacet.OnDelete -> s.OnDelete <> Cascade && t.OnDelete = Cascade
    | _ -> false

/// An index facet transition that fails on existing data: gaining uniqueness (a
/// `unique` / `primary key` index errors on apply if duplicates already exist).
let private idxFacetRewrites (s: Index) (t: Index) (f: IndexFacet) : bool =
    match f with
    | IndexFacet.Uniqueness -> not (IndexUniqueness.isUnique s.Uniqueness) && IndexUniqueness.isUnique t.Uniqueness
    | _ -> false

/// The risk CATEGORY of a data-touching attribute facet — the bucket the danger
/// callout groups by at scale, so 300 concerns read as their shape (how many
/// drops / type changes / tightenings / …), not 12 arbitrary lines.
let private attrRewriteCategory (f: AttributeFacet) : string =
    match f with
    | AttributeFacet.DataType    -> "type change"
    | AttributeFacet.Nullability -> "null → not null"
    | AttributeFacet.PrimaryKey  -> "primary key change"
    | AttributeFacet.Identity    -> "identity change"
    | _                          -> "reshape"

/// The data-bearing DROPS — a dropped table or column loses its rows. (A dropped
/// FK / index / sequence is structural, not row-data loss, so it stays in the
/// remove lane only; the callout is specifically about DATA.) Each item carries
/// its risk CATEGORY (here, "dropped") for the at-scale callout grouping.
let private dataDrops (d: CatalogDiff) : (string * string) list =
    let srcNames = nameIndex (CatalogDiff.source d)
    let droppedTables =
        CatalogDiff.removed d |> Set.toList
        |> List.map (fun k -> "dropped", sprintf "table %s — dropped, data lost" (nm srcNames k))
    let droppedColumns =
        CatalogDiff.attributeDiffs d |> Map.toList
        |> List.collect (fun (kk, ad) ->
            ad.Removed |> Set.toList
            |> List.map (fun ak -> "dropped", sprintf "column %s.%s — dropped, data lost" (nm srcNames kk) (nm srcNames ak)))
    droppedTables @ droppedColumns

/// The RESHAPES whose transition rewrites or can fail on existing rows — the
/// column / FK / index reshapes that touch data (a type conversion, `null → not
/// null`, a cascade added, a uniqueness gained). Distinct from `dataDrops`: a
/// reshape keeps the entity, a drop removes it. Each item is `(category, text)`:
/// the category buckets the at-scale callout, the text is the line. Pure over the
/// diff's retained source / target; deterministic (`Set` / `Map` order, T1).
let private rewrites (d: CatalogDiff) : (string * string) list =
    let source   = CatalogDiff.source d
    let target   = CatalogDiff.target d
    let srcNames = nameIndex source
    let tgtNames = nameIndex target
    let attrRewrites =
        CatalogDiff.attributeDiffs d |> Map.toList
        |> List.collect (fun (kk, ad) ->
            let find = findChild (fun (k: Kind) -> k.Attributes) (fun (a: Attribute) -> a.SsKey) kk
            ad.Reshaped |> List.collect (fun c ->
                match find source c.AttributeKey, find target c.AttributeKey with
                | Some s, Some t ->
                    c.Facets |> Set.toList |> List.filter (attrFacetRewrites s t)
                    |> List.map (fun f -> attrRewriteCategory f, sprintf "column %s.%s — %s" (nm srcNames kk) (Name.value s.Name) (attributeEvidence s t f))
                | _ -> []))
    let refRewrites =
        CatalogDiff.referenceDiffs d |> Map.toList
        |> List.collect (fun (kk, rd) ->
            let find = findChild (fun (k: Kind) -> k.References) (fun (r: Reference) -> r.SsKey) kk
            rd.Reshaped |> List.collect (fun c ->
                match find source c.ReferenceKey, find target c.ReferenceKey with
                | Some s, Some t ->
                    c.Facets |> Set.toList |> List.filter (refFacetRewrites s t)
                    |> List.map (fun f -> "cascade delete", sprintf "relationship %s.%s — %s" (nm srcNames kk) (Name.value s.Name) (referenceEvidence (nm srcNames) (nm tgtNames) s t f))
                | _ -> []))
    let idxRewrites =
        CatalogDiff.indexDiffs d |> Map.toList
        |> List.collect (fun (kk, idd) ->
            let find = findChild (fun (k: Kind) -> k.Indexes) (fun (i: Index) -> i.SsKey) kk
            idd.Reshaped |> List.collect (fun c ->
                match find source c.IndexKey, find target c.IndexKey with
                | Some s, Some t ->
                    c.Facets |> Set.toList |> List.filter (idxFacetRewrites s t)
                    |> List.map (fun f -> "uniqueness gained", sprintf "index %s.%s — %s" (nm srcNames kk) (Name.value s.Name) (indexEvidence s t f))
                | _ -> []))
    attrRewrites @ refRewrites @ idxRewrites

/// The full callout set as `(category, text)` — data-bearing drops first (the
/// heaviest), then the rewrites. The "review these first" list.
let private dangers (d: CatalogDiff) : (string * string) list = dataDrops d @ rewrites d

/// Above this many data-touching concerns the flat 12-item callout buries the
/// shape of the risk — so the callout groups by CATEGORY instead (a 310-table
/// estate can carry hundreds of FK cascades / not-null tightenings at once).
[<Literal>]
let private dangerGroupThreshold = 12

// --- channel scoping (`diff --only <channel>`) ------------------------------
// At scale an operator reviews ONE channel at a time ("just the column changes").
// `--only <channel>` keeps the lane items of one channel — robust because every
// item is noun-prefixed by its channel (`column …` / `relationship …` / …), the
// format these collectors own, not parsed external data.

/// Map an operator's channel word (singular / plural / synonym) to its item noun.
/// An unrecognized value falls through as-is (it simply matches nothing — the
/// operator sees an empty scope and corrects the flag).
let private channelNoun (s: string) : string =
    match s.Trim().ToLowerInvariant() with
    | "table" | "tables" | "kind" | "kinds"                                   -> "table"
    | "column" | "columns" | "attribute" | "attributes" | "field" | "fields" -> "column"
    | "relationship" | "relationships" | "fk" | "fks" | "reference" | "references" -> "relationship"
    | "index" | "indexes" | "indices"                                         -> "index"
    | "sequence" | "sequences"                                                -> "sequence"
    | other                                                                   -> other

/// Keep only the items of the requested channel (`None` = every channel). Matches
/// the noun prefix, so it scopes lanes AND the danger callout uniformly.
let private keepChannel (chan: string option) (items: string list) : string list =
    match chan with
    | None   -> items
    | Some c -> let noun = channelNoun c + " " in items |> List.filter (fun (s: string) -> s.StartsWith noun)

// --- the statement: the plain lead line of a change (INSTRUMENT slice 1) ----

/// The statement of a catalog change — the one plain line that leads the surface
/// (`THE_VOICE.md` §1 rule 3 — statement first, the substantiation beneath). It
/// leads amber when the change can hurt: a removal, OR a data-touching reshape (a
/// `null → not null`, a type conversion, a cascade added, a uniqueness gained).
/// Counting the data-touch set is the #C honesty fix — a zero-drop migration that
/// adds a NOT NULL column used to lead CALM ("no removals") while being genuinely
/// risky; now it reads "N changes · K may rewrite or lose data · review."
let catalogStatement (d: CatalogDiff) : View.View =
    let c = CatalogDiff.channelCounts d
    let removed =
        c.RemovedKinds + c.RemovedAttributes + c.RemovedReferences
        + c.RemovedIndexes + c.RemovedSequences
    let n = CatalogDiff.norm d
    let r = List.length (rewrites d)
    if n = 0 then View.Hero(View.Ok, "No differences found. The two states are identical.")
    elif removed > 0 && r > 0 then
        View.Hero(View.Warn, sprintf "%d changes · %d drops · %d may rewrite data · review before applying" n removed r)
    elif removed > 0 then
        View.Hero(View.Warn, sprintf "%d changes · %d drops · review before applying" n removed)
    elif r > 0 then
        View.Hero(View.Warn, sprintf "%d changes · %d may rewrite or lose data · review before applying" n r)
    else
        View.Hero(View.Ok, sprintf "%d changes · no removals" n)

/// The "review these first" callout — the data-touching changes promoted to the
/// top of the substantiation, as a single `Bad`-badged lane named honestly. Empty
/// (no lane) when nothing touches data. A SEPARATE surface, so the move-lanes stay
/// homogeneous (the operator's rejection of per-item lane status holds).
let dangerLaneScoped (chan: string option) (d: CatalogDiff) : View.View list =
    // Filter by channel on the line text (noun-prefixed), keeping the category.
    let noun = chan |> Option.map (fun c -> channelNoun c + " ")
    let kept =
        dangers d
        |> List.filter (fun (_, t) -> match noun with None -> true | Some n -> t.StartsWith n)
    match kept with
    | [] -> []
    | items when List.length items <= dangerGroupThreshold ->
        // Small: the flat callout lane — every concern on one line, the historical shape.
        [ View.Lane("!", "may rewrite or lose data", View.Bad, items |> List.map snd |> List.sort) ]
    | items ->
        // At scale: group by risk CATEGORY (dropped / cascade delete / null → not null /
        // …), each a diggable sub-group with its count, so the operator reads the risk
        // PROFILE then drills — the flat 12-cap would bury 300 concerns behind "and N more".
        // The top node carries the loud total; the Navigator digs the tree (Disclosures).
        let groups =
            items
            |> List.groupBy fst
            |> List.map (fun (cat, xs) -> cat, xs |> List.map snd |> List.sort)
            |> List.sortBy (fun (cat, xs) -> (-List.length xs, cat))        // most concerns first, then name (T1)
        let detail =
            groups
            |> List.map (fun (cat, texts) ->
                View.Disclosure(sprintf "%s  %s" cat (Theme.humane (List.length texts)), View.Bad, texts |> List.map View.Note))
        [ View.Disclosure(sprintf "may rewrite or lose data  %s" (Theme.humane (List.length items)), View.Bad, detail) ]

let dangerLane (d: CatalogDiff) : View.View list = dangerLaneScoped None d

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
/// Above this many items a flat lane caps at 12 and buries the rest behind "and N
/// more"; instead the lane groups by module into a navigable `Disclosure` tree (a
/// 310-table estate can put hundreds of changes in one channel). Only groups when
/// the items span ≥ 2 modules — a single-module lane gains nothing from one group.
[<Literal>]
let private laneGroupThreshold = 12

/// The owning module of a lane item, by extracting its kind name — the token after
/// the channel noun, up to the qualifier `.` — and resolving it against a
/// name→module map. Sequences are catalog-level (no module). The item format is
/// owned by the collectors here (`<noun> <Kind>[.<entity>]…`), so the extraction is
/// reliable; an unresolved name (or a sequence) falls to its own bucket. A
/// `ComparisonTests` grouping case pins it against format drift.
let private moduleOfItem (nameToModule: Map<string, string>) (item: string) : string =
    let parts = item.Split(' ')
    if parts.Length < 2 then "(other)"
    elif parts.[0] = "sequence" then "(sequences)"
    else
        let tok = parts.[1]
        let kindName = match tok.IndexOf('.') with | -1 -> tok | i -> tok.Substring(0, i)
        Map.tryFind kindName nameToModule |> Option.defaultValue "(other)"

let renderCatalogLanesScoped (chan: string option) (d: CatalogDiff) : View.View list =
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
    let findSeq (cat: Catalog) (key: SsKey) = cat.Sequences |> List.tryFind (fun s -> s.SsKey = key)
    let seqReshapes =
        seqd.Reshaped
        |> List.map (fun c ->
            match findSeq source c.SequenceKey, findSeq target c.SequenceKey with
            | Some s, Some t ->
                sprintf "sequence %s: %s" (nm srcNames c.SequenceKey) (c.Facets |> Set.toList |> List.map (sequenceEvidence s t) |> String.concat ", ")
            | _ ->
                sprintf "sequence %s: %s" (nm srcNames c.SequenceKey) (facetsJoin sequenceFacetText c.Facets))

    // Kind-OWN facets (modality / triggers / checks / activation) — a reshape of
    // the table itself, not its children. `active` carries before → after; the
    // list-shaped facets keep the name.
    let kindFacetReshapes =
        CatalogDiff.kindFacetDiffs d |> Map.toList
        |> List.map (fun (kk, facets) ->
            match Catalog.tryFindKind kk source, Catalog.tryFindKind kk target with
            | Some s, Some t ->
                sprintf "table %s: %s" (nm srcNames kk) (facets |> Set.toList |> List.map (kindFacetEvidence s t) |> String.concat ", ")
            | _ ->
                sprintf "table %s: %s" (nm srcNames kk) (facetsJoin kindFacetText facets))

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
                    let body = c.Facets |> Set.toList |> List.map (referenceEvidence (nm srcNames) (nm tgtNames) s t) |> String.concat ", "
                    sprintf "relationship %s.%s: %s" (nm srcNames kk) (Name.value s.Name) body
                | _ ->
                    sprintf "relationship %s.%s: %s" (nm srcNames kk) (nm srcNames c.ReferenceKey) (facetsJoin referenceFacetText c.Facets)))
    let idxReshapes =
        idxDiffs |> Map.toList
        |> List.collect (fun (kk, idd) ->
            let find = findChild (fun (k: Kind) -> k.Indexes) (fun (i: Index) -> i.SsKey) kk
            idd.Reshaped
            |> List.map (fun c ->
                match find source c.IndexKey, find target c.IndexKey with
                | Some s, Some t ->
                    let body = c.Facets |> Set.toList |> List.map (indexEvidence s t) |> String.concat ", "
                    sprintf "index %s.%s: %s" (nm srcNames kk) (Name.value s.Name) body
                | _ ->
                    sprintf "index %s.%s: %s" (nm srcNames kk) (nm srcNames c.IndexKey) (facetsJoin indexFacetText c.Facets)))

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
        @ idxReshapes
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

    // The kind name → owning module map, from BOTH catalogs (an added kind lives only
    // in the target), for the at-scale grouping (#H).
    let nameToModule =
        (Catalog.allModulesKinds source @ Catalog.allModulesKinds target)
        |> List.map (fun (m, k) -> Name.value k.Name, Name.value m.Name)
        |> Map.ofList
    // Build one move's lane. Items are sorted in the canonical assembly (#D —
    // noun-prefixed, so channel-grouped then name-ordered; both lenses one order, T1).
    // SMALL or single-module ⇒ a flat `Lane` (the historical shape, capped at 12).
    // LARGE and multi-module ⇒ a navigable `Disclosure` TREE grouped by module (#H):
    // a 400-FK lane stops being a 12-item wall and becomes module headlines you drill
    // — the Navigator digs Disclosures natively, so no Navigator change is needed.
    let lane glyph label st items =
        let items' = keepChannel chan items |> List.sort
        match items' with
        | [] -> []
        | _ ->
            let modules = items' |> List.map (moduleOfItem nameToModule) |> List.distinct
            if List.length items' <= laneGroupThreshold || List.length modules < 2 then
                [ View.Lane(glyph, label, st, items') ]
            else
                let groups =
                    items'
                    |> List.groupBy (moduleOfItem nameToModule)
                    |> List.map (fun (m, xs) -> m, List.sort xs)
                    |> List.sortBy (fun (m, xs) -> (-List.length xs, m))        // hottest module first, then name (T1)
                let detail =
                    groups |> List.map (fun (m, xs) ->
                        View.Disclosure(sprintf "%s  %s" m (Theme.humane (List.length xs)), st, xs |> List.map View.Note))
                // The status carries the move's glyph + colour on the headline; the count is loud.
                [ View.Disclosure(sprintf "%s  %s" label (Theme.humane (List.length items')), st, detail) ]
    lane "⟲" "rename" View.Ok renames
    @ lane "~" "reshape" View.Warn reshapes
    @ lane "+" "add" View.Ok adds
    @ lane "−" "remove" View.Bad removes

/// The move-typed lanes, unscoped — every channel (the historical signature; the
/// tests + the default render path call this).
let renderCatalogLanes (d: CatalogDiff) : View.View list = renderCatalogLanesScoped None d

/// A catalog change as a `Surface` — the statement over the substantiation: the
/// move-typed lanes (kind moves: rename / add / remove, each badged by
/// reversibility, progressively disclosed) with the per-channel ‖δ‖ panel
/// beneath. The statement/substantiation shape every later surface reuses
/// (`THE_VOICE.md` §1 rule 3).
// --- the per-module "top movers" rollup (at-scale orientation) --------------
// On a real estate a diff spans many modules; "which module is hot" is the first
// triage question, answerable today only by reading every lane. A per-module
// change tally — sorted by churn — answers it at a glance and pairs with the
// `--module` scope (see the hot module → `diff --module <it>` to dig it). Shown
// only when the diff touches ≥ 2 modules (a single-module diff needs no rollup).

/// Kind `SsKey` → owning module name, for one catalog (`Catalog.allModulesKinds`).
let private kindModuleIndex (cat: Catalog) : Map<SsKey, string> =
    Catalog.allModulesKinds cat |> List.map (fun (m, k) -> k.SsKey, Name.value m.Name) |> Map.ofList

/// The per-module change tally as a `View.Table` (module · changes, churn-sorted),
/// or `[]` when the diff touches fewer than two modules. Counts each lane item
/// (an add / drop / rename / reshape) against its owning kind's module; sequences
/// are catalog-level (not module-scoped) and excluded. Deterministic: ties break
/// by module name (T1). The machine lens carries the full table (it is a `Table`).
let moduleRollup (d: CatalogDiff) : View.View list =
    let srcMods = kindModuleIndex (CatalogDiff.source d)
    let tgtMods = kindModuleIndex (CatalogDiff.target d)
    let viaSrc k = Map.tryFind k srcMods |> Option.defaultValue "(unknown)"
    let viaTgt k = Map.tryFind k tgtMods |> Option.defaultValue "(unknown)"
    let either k = Map.tryFind k srcMods |> Option.orElseWith (fun () -> Map.tryFind k tgtMods) |> Option.defaultValue "(unknown)"
    let channelTally (diffs: Map<SsKey, ChannelDiff<'c>>) (kk: SsKey) : (string * int) =
        let cd = diffs.[kk]
        either kk, (Set.count cd.Added + Set.count cd.Removed + Map.count cd.Renamed + List.length cd.Reshaped)
    let contribs =
        [ for k in CatalogDiff.added d   do yield viaTgt k, 1
          for k in CatalogDiff.removed d do yield viaSrc k, 1
          for KeyValue(k, _) in CatalogDiff.renamed d do yield viaSrc k, 1
          for KeyValue(kk, _) in CatalogDiff.attributeDiffs d do yield channelTally (CatalogDiff.attributeDiffs d) kk
          for KeyValue(kk, _) in CatalogDiff.referenceDiffs d do yield channelTally (CatalogDiff.referenceDiffs d) kk
          for KeyValue(kk, _) in CatalogDiff.indexDiffs d     do yield channelTally (CatalogDiff.indexDiffs d) kk
          for KeyValue(kk, _) in CatalogDiff.kindFacetDiffs d do yield either kk, 1 ]
    let perModule =
        contribs
        |> List.groupBy fst
        |> List.map (fun (m, xs) -> m, List.sumBy snd xs)
        |> List.filter (fun (_, n) -> n > 0)
        |> List.sortBy (fun (m, n) -> (-n, m))            // churn desc, then name (T1)
    if List.length perModule < 2 then []
    else
        let rows = perModule |> List.map (fun (m, n) -> [ (m, View.Neutral); (Theme.humane n, View.Neutral) ])
        [ View.Blank
          View.Note "by module (most-changed first)"
          View.Table([ "module"; "changes" ], rows) ]

let changeSurfaceScoped (chan: string option) (d: CatalogDiff) : Surface.Surface =
    { Statement      = catalogStatement d
      // The danger callout leads the substantiation ("review these first"), then
      // the full move-lanes, then the per-channel ‖δ‖ panel — statement-first
      // safety: honest verdict, the risky subset, the whole change, the counts.
      // `chan` (`--only`) scopes the lanes + callout to one channel; the statement,
      // the per-module rollup, and the ‖δ‖ panel stay WHOLE (the operator keeps the
      // full verdict + orientation while reviewing one channel).
      Substantiation = dangerLaneScoped chan d @ renderCatalogLanesScoped chan d @ moduleRollup d @ [ View.Blank; renderCatalogDiff d ]
      Action         = None }

let changeSurface (d: CatalogDiff) : Surface.Surface = changeSurfaceScoped None d

/// A catalog change rendered statement-first: the plain verdict, then the
/// substantiation — the move-typed lanes and the per-channel ‖δ‖ panel, revealed
/// on demand. `chan` scopes to one channel (`diff --only columns`).
let renderCatalogChangeScoped (chan: string option) (d: CatalogDiff) : View.View =
    Surface.render (changeSurfaceScoped chan d)

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
