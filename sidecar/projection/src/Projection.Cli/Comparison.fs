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

/// Render a `CatalogDiff` summary: the ‖δ‖ norm (the WAVE-6 change-measure)
/// plus per-channel +added / −removed / ~renamed / ≠changed counts.
let renderCatalogDiff (d: CatalogDiff) : View.View =
    let c = CatalogDiff.channelCounts d
    let n = CatalogDiff.norm d
    let chan a r rn ch = sprintf "+%d −%d ~%d⟲ %d≠" a r rn ch
    View.Panel(
        "catalog Δ",
        [ View.Field("‖δ‖ norm", string n, (if n = 0 then View.Ok else View.Warn))
          View.Field("kinds", sprintf "+%d −%d ~%d⟲" c.AddedKinds c.RemovedKinds c.RenamedKinds, View.Neutral)
          View.Field("attributes", chan c.AddedAttributes c.RemovedAttributes c.RenamedAttributes c.ChangedAttributes, View.Neutral)
          View.Field("references", chan c.AddedReferences c.RemovedReferences c.RenamedReferences c.ChangedReferences, View.Neutral)
          View.Field("indexes", chan c.AddedIndexes c.RemovedIndexes c.RenamedIndexes c.ChangedIndexes, View.Neutral)
          View.Field("sequences", chan c.AddedSequences c.RemovedSequences c.RenamedSequences c.ChangedSequences, View.Neutral) ])

/// Render a `PhysicalSchemaDiff` summary: identical / diverged, plus per-axis
/// −missing / +extra counts.
let renderPhysicalDiff (d: PhysicalSchemaDiff) : View.View =
    let isEq = PhysicalSchema.isEqual d
    let axis label (missing: int) (extra: int) : View.View =
        View.Field(label, sprintf "−%d +%d" missing extra, (if missing + extra > 0 then View.Warn else View.Neutral))
    View.Panel(
        "physical Δ",
        [ View.Field("status", (if isEq then "identical" else "diverged"), (if isEq then View.Ok else View.Bad))
          axis "columns" d.MissingColumns.Length d.ExtraColumns.Length
          axis "foreignKeys" d.MissingForeignKeys.Length d.ExtraForeignKeys.Length
          axis "indexes" d.MissingIndexes.Length d.ExtraIndexes.Length
          axis "rows" d.MissingRows.Length d.ExtraRows.Length
          axis "annotations" d.MissingAnnotations.Length d.ExtraAnnotations.Length ])

// --- the two instances -----------------------------------------------------

/// `Catalog` comparison — a genuine torsor: `Apply = Some applyDiff` (the
/// delta replays to reconstruct the target; Weyl-proven).
let catalog : Comparison<Catalog, CatalogDiff> =
    { Between = (fun a b -> CatalogDiff.between a b |> Result.mapError (fun e -> sprintf "%A" e))
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
