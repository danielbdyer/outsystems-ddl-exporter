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

/// One attribute facet, in plain words (for the reshape lane).
let private facetText (f: AttributeFacet) : string =
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

let private facetsText (facets: Set<AttributeFacet>) : string =
    facets |> Set.toList |> List.map facetText |> String.concat ", "

/// The move-typed lanes of a catalog change — rename / reshape / add / remove,
/// each a `View.Lane` badged by reversibility: rename + add are reversible-safe
/// (Ok); reshape may rewrite data (Warn — review); remove destroys structure
/// (Bad). The rename lane carries `old → new`; the reshape lane carries the
/// changed attribute + which facets changed.
let renderCatalogLanes (d: CatalogDiff) : View.View list =
    let renamed = CatalogDiff.renamed d |> Map.toList
    let added   = CatalogDiff.added d   |> Set.toList
    let removed = CatalogDiff.removed d  |> Set.toList
    let reshaped =
        CatalogDiff.attributeDiffs d
        |> Map.toList
        |> List.collect (fun (_, ad) ->
            ad.Reshaped
            |> List.map (fun c -> sprintf "%s · %s" (SsKey.rootOriginal c.AttributeKey) (facetsText c.Facets)))
    let lane glyph label st items =
        if List.isEmpty items then [] else [ View.Lane(glyph, label, st, items) ]
    lane "⟲" "rename" View.Ok
        (renamed |> List.map (fun (_, r) -> sprintf "%s → %s" (Name.value r.OldName) (Name.value r.NewName)))
    @ lane "~" "reshape" View.Warn reshaped
    @ lane "+" "add" View.Ok (added |> List.map SsKey.rootOriginal)
    @ lane "−" "remove" View.Bad (removed |> List.map SsKey.rootOriginal)

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
