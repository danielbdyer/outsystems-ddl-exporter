namespace Projection.Targets.Data

open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for the `Statement` typed-stream DU (CellValue / InsertRow / SetIdentityInsert) and the `TableId` re-spelling, per A35 (Π's canonical output is a typed deterministic statement stream); same architectural shape `StaticSeedsEmitter.fs:4` declares for `ScriptDomBuild`. Lifting `Statement` to Core (or a `Projection.Targets.Common` project) is the structural fix and is a deferred separate refactor — until then the cross-target edge is the gold-standard primitive route at the absolute terminal typed-stream-construction boundary.

/// Π_StaticPopulation — the typed-`Statement.InsertRow` realization
/// of `Modality.Static` populations. Sibling Π to:
///   - `SsdtDdlEmitter.statements` (DDL realization, schema axis;
///     chapter 4.1.A) — emits `Statement.CreateTable` /
///     `Statement.CreateIndex` per kind in topological order.
///   - `StaticSeedsEmitter.emit` (DataInsertScript / MERGE
///     realization, chapter 4.1.B) — produces idempotent CDC-aware
///     MERGE statements for the V2-driver production redeploy
///     contract.
///
/// **Why this is distinct from `StaticSeedsEmitter`** — same source
/// data (kind.Modality.Static), different output algebra:
///   - `StaticSeedsEmitter` produces MERGE rendering for production
///     idempotent redeploys with CDC-aware change-detection (the
///     V2-driver KPI Phase 3 contract).
///   - `StaticPopulationEmitter` produces typed `Statement.InsertRow`
///     for fresh-deploy-into-empty-target consumers — the canary
///     round-trip lane (`Deploy.runWideCanary`) and any future
///     ephemeral-container scenario where idempotency isn't required
///     and CDC isn't running.
///
/// Concept-shaped name (pillar 8) — names what the emitter IS in the
/// domain (the static-population emitter); the realization axis is in
/// the output type (`seq<Statement>`), not the name.
///
/// **A35 alignment.** Π's canonical output is a typed deterministic
/// statement stream. Realizations consume the stream — `Render.toSql`
/// renders text, `Deploy.executeStream` folds consecutive `InsertRow`s
/// into `SqlBulkCopy` batches at 43k rows/sec (per chapter 3.1
/// session-34). The algebra holds at the stream level; bulk-vs-
/// incremental deploy is realization-layer policy invisible to Π
/// (A36).
///
/// **A18 amended.** Profile-independent at this slice (no policy or
/// profile evidence is consumed). Future expansion (e.g., per-row
/// gating on a Profile field) is a closed-DU expansion when a real
/// consumer surfaces.
///
/// **Big-O.** O(N + R × K) where N = `Catalog.allKinds`, R = total
/// static rows across the catalog, K = average attributes per kind.
/// Streamed lazily via `seq` (one row's worth of `CellValue` resident
/// at a time when consumed by `Deploy.executeStream`); suitable for
/// 50k+ rows × 100 tables operator-reality canary scale.
///
/// **Topological order.** Identical to `SsdtDdlEmitter.statements`
/// (`TopologicalOrderPass.runWith SkipSelfEdges`) so a composer
/// (`Seq.append (SsdtDdlEmitter.statements c) (StaticPopulationEmitter
/// .statements c)`) yields a deploy-correct schema-then-data stream:
/// FK targets are created before referencers, and FK targets'
/// rows are inserted before referencers' rows (so deferred-constraint
/// FK checks don't fire on the row insert).
[<RequireQualifiedAccess>]
module StaticPopulationEmitter =

    /// Pass version. Bump when the static-population emission shape
    /// changes in a way that matters for cross-version comparators.
    [<Literal>]
    let version : int = 1

    /// Project one (`Attribute`, raw) pair to a typed `CellValue`. The
    /// realization layer (`Render.toSql` / `Bulk.copyRows` →
    /// `formatRawValue`) interprets the raw string per the column's
    /// `PrimitiveType`.
    let private cellValue (a: Attribute) (raw: string) : CellValue =
        { Column = a.Column.ColumnName
          Type   = a.Type
          Raw    = raw }

    /// Build the `CellValue list` for one row's emission. Per the
    /// retired `RawTextEmitter.rowToInsert` partial-fixture
    /// discipline (session-33): when a fixture row omits a column's
    /// value (Map.tryFind = None), that column is excluded from the
    /// emitted `INSERT (cols...) VALUES (vals...)` rather than
    /// emitting an explicit NULL. SqlBulkCopy with `KeepNulls`
    /// honors per-column omission identically.
    let private rowToCellValues (k: Kind) (row: StaticRow) : CellValue list =
        k.Attributes
        |> List.choose (fun a ->
            Map.tryFind a.Name row.Values
            |> Option.map (cellValue a))

    /// True when the kind carries any IDENTITY-flagged attribute. The
    /// `SET IDENTITY_INSERT [table] ON ... OFF` toggle brackets the
    /// `InsertRow` block when realizing through `Render.toSql` (the
    /// text path); `Bulk.copyRows` honors `SqlBulkCopyOptions
    /// .KeepIdentity` and does not require the toggle (per
    /// `Bulk.fs:105`). The toggle is emitted unconditionally so both
    /// realizations are correct without consumer-specific knowledge.
    let private hasIdentity (k: Kind) : bool =
        k.Attributes |> List.exists (fun a -> a.IsIdentity)

    /// Emit the per-kind `InsertRow` block bracketed by IDENTITY
    /// toggles when applicable. Empty-population kinds yield an
    /// empty subsequence — they contribute no statements (the
    /// stream-shape sibling to `ArtifactByKind`'s strict-keyset T11;
    /// per A35 the stream form is keyset-shape-free).
    let private kindStatements (k: Kind) : seq<Statement> =
        seq {
            let rows = Kind.staticPopulations k
            if not (List.isEmpty rows) then
                let table : TableId = k.Physical
                let identityToggle = hasIdentity k
                if identityToggle then
                    yield SetIdentityInsert (table, true)
                for row in rows do
                    let values = rowToCellValues k row
                    if not (List.isEmpty values) then
                        yield InsertRow (table, values)
                if identityToggle then
                    yield SetIdentityInsert (table, false)
        }

    /// Catalog-wide typed statement stream over `Modality.Static`
    /// populations. Kinds without static populations contribute no
    /// statements (per the stream-form T11 caveat above). Topological
    /// order matches `SsdtDdlEmitter.statements`; composing the two
    /// streams yields the canonical schema-then-data deploy form.
    let statements (catalog: Catalog) : seq<Statement> =
        use _ = Bench.scope "emit.staticPopulation.statements"
        let order =
            (TopologicalOrderPass.runWith SkipSelfEdges catalog).Value.Order
        let kindByKey =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList
        seq {
            for ssKey in order do
                match Map.tryFind ssKey kindByKey with
                | None   -> ()  // unreachable: order is derived from catalog
                | Some k -> yield! kindStatements k
        }

    // -----------------------------------------------------------------------
    // Slice 5.13.sibling-emitter-registry-static-population —
    // `registeredMetadata` entry for the StaticPopulationEmitter sibling Π.
    // Mirrors `StaticSeedsEmitter.registeredMetadata`'s precedent on the
    // fresh-deploy InsertRow axis (vs. idempotent MERGE).
    //
    // **Classification.** All Sites carry `DataIntent`. Static rows live
    // in `Kind.Modality` (the `Static rows` variant); they ARE catalog-
    // resident evidence, not operator overlay. The emitter projects from
    // Catalog only (no Profile, no Policy — A18 amended). Per pillar 9,
    // this is the same classification logic that `StaticSeedsEmitter`
    // uses (sibling shape; same source data, different output algebra).
    // -----------------------------------------------------------------------

    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "staticPopulationEmitter" Data
            [ TransformSite.dataIntent "kindStatements"
                "Per-kind static rows from `Kind.Modality.Static` → `seq<Statement>` of `InsertRow` bracketed by `SetIdentityInsert` toggles when any attribute carries `IsIdentity`. Pure projection of catalog-resident static data (the rows live in the IR; the emitter just streams them). Sibling to `StaticSeedsEmitter.staticRowsProjection` on the InsertRow-vs-MERGE realization axis."
              TransformSite.dataIntent "rowToCellValues"
                "Project (`Attribute`, raw) pair → typed `CellValue` per column. Partial-fixture discipline: a row that omits a column's value contributes no `CellValue` for that column (per the session-33 `RawTextEmitter.rowToInsert` precedent; `SqlBulkCopy.KeepNulls` honors the same shape). Excluded columns become DEFAULT-or-NULL at the realization layer, not explicit NULL."
              TransformSite.dataIntent "identityToggle"
                "Emit `SET IDENTITY_INSERT [table] ON` before the row block and `OFF` after when the kind carries any `IsIdentity` attribute. Pure projection of `Kind.Attributes.IsIdentity` evidence; emitted unconditionally for both realization layers (`Render.toSql` consumes; `Bulk.copyRows` honors `SqlBulkCopyOptions.KeepIdentity` and the toggle is redundant-but-correct)."
              TransformSite.dataIntent "topologicalOrder"
                "Order kinds via `TopologicalOrderPass.runWith SkipSelfEdges` so FK targets' rows insert before referencers'. Identical algorithm to `SsdtDdlEmitter.topologicalOrder` (per A40 harmonization-via-parameterization); composing `Seq.append (SsdtDdlEmitter.statements c) (StaticPopulationEmitter.statements c)` yields the canonical schema-then-data deploy form."
              TransformSite.dataIntent "statements"
                "Catalog-wide typed statement stream realization (A35) — `Catalog → seq<Statement>` over `Modality.Static` populations. Sibling Π to `SsdtDdlEmitter.statements` on the data-axis; both consume Catalog only (A18). Empty-population kinds contribute no statements (stream-form T11 caveat: per-kind absence is silent, not a structural breach)." ]
