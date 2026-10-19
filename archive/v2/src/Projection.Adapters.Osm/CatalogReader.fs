namespace Projection.Adapters.Osm

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Projection.Core
open OssysRowsetTypes
open OssysTranslation
open OssysJsonReader
open OssysRowsetReader

/// Boundary adapter — converts V1's `osm_model.json` snapshot shape
/// into V2's `Catalog` IR.
///
/// **V1↔V2 boundary.** V1's metadata extraction chain
/// (`outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` →
/// `SnapshotJsonBuilder` → `osm_model.json`) is the source of truth
/// for OutSystems platform metadata. V2's adapter consumes the JSON
/// document V1 produces. The cherry-pick discipline (`HANDOFF.md`)
/// keeps the boundary as data, not typed cross-references — this
/// adapter does not depend on any V1 C# types.
///
/// **Position B for `ICatalogReader`.** Per `DECISIONS 2026-05-15 —
/// OSSYS adapter parse signature`, the entry-point shape is
/// `SnapshotSource -> Task<Result<Catalog>>`. A future
/// `ICatalogReader` interface (when a second catalog source
/// materializes) wraps this signature trivially via object expression;
/// no retrofit needed.
///
/// **Implementation discipline.** Only what the differential tests
/// demand. The OSSYS implementation chapter accumulates translation
/// rules under empirical pressure from
/// `OsmCatalogReaderDifferentialTests`; speculative DTO design that
/// mirrors V1's full ~22 rowset surface is deliberately avoided.
[<RequireQualifiedAccess>]
module CatalogReader =

    /// The input slot on the parse function. Closed DU.
    ///
    /// **Two variants today; one variant planned (canonical
    /// resolution); one variant reserved.**
    ///
    ///   - `SnapshotFile` and `SnapshotJson` are the current
    ///     consumers. Both feed V1's canonical `osm_model.json`
    ///     shape; SsKey is name-synthesized; the bound on A1 is
    ///     documented (`DECISIONS 2026-05-15 — OSSYS adapter
    ///     translation rules`).
    ///
    ///   - **Planned: `SnapshotRowsets`.** Per the operator
    ///     decision recorded in `DECISIONS 2026-05-15 — OSSYS
    ///     adapter translation rules`, session-20 amendment, the
    ///     canonical resolution to the lossy-SSKey question is to
    ///     consume V1's trailing rowsets directly. Rowsets carry
    ///     SSKey natively and preserve per-table column structure
    ///     the `FOR JSON PATH` aggregations collapse. The variant
    ///     itself lands when chapter 2's organic flow brings it —
    ///     likely after the current OSSYS adapter chapter
    ///     completes its translation work through `SnapshotJson`.
    ///     The operator decision is locked; not subject to
    ///     relitigation.
    ///
    ///   - **Reserved: `LiveOssysConnection`.** A future variant
    ///     for the case where V2 needs to operate without V1's
    ///     chain in the loop entirely. Per `DECISIONS 2026-05-15 —
    ///     OSSYS adapter parse signature`, deferred until its
    ///     specific demand surfaces.
    ///
    /// Adding `SnapshotRowsets` speculatively today would violate
    /// the closed-DU expansion discipline (one consumer needed;
    /// zero exist). The variant is named here so future readers of
    /// the code see the architectural commitment without having to
    /// read DECISIONS to discover it.

    type SnapshotSource =
        /// Path to a V1-produced `osm_model.json` file on disk.
        | SnapshotFile of path: string
        /// In-memory snapshot string. Useful for tests and for
        /// pipelines that produce the snapshot in memory rather than
        /// via disk.
        | SnapshotJson of json: string
        /// V1 pre-aggregation rowset bundle. Chapter 3.2 — closes the
        /// JSON-projection-lossiness class (`DECISIONS 2026-05-19 —
        /// naming the two classes`). The rowsets carry SsKey natively
        /// (via `OssysOriginal guid` per `Identity.fs:70`); A1's
        /// "identity survives rename" bound resolves through this
        /// path. Coexists permanently with `SnapshotJson` per
        /// `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §6 — no
        /// deprecation trigger named.
        | SnapshotRowsets of bundle: RowsetBundle

    let parse (source: SnapshotSource) : Task<Result<Catalog>> =
        use _ = Bench.scope "adapter.osm.parse"
        match source with
        | SnapshotJson json ->
            Task.FromResult(parseJsonString json)
        | SnapshotRowsets bundle ->
            // Chapter 3.2 slice 1. Pure translation; no I/O. The
            // rowset-shaped V1 metadata flows through
            // `parseRowsetBundle` for FK-by-ID join + Module/Kind/
            // Attribute construction. Closed-DU expansion empirical-
            // test discipline (`DECISIONS 2026-05-13`): exhaustiveness
            // errors should light up only at this match site.
            Task.FromResult(parseRowsetBundle bundle)
        | SnapshotFile path ->
            // Read-then-parse is the natural shape; async file I/O
            // would benefit primarily for very large snapshots, which
            // the chapter-open scope hasn't reached yet. Keep the
            // synchronous read for now; the boundary is async.
            try
                let json = File.ReadAllText(path)
                Task.FromResult(parseJsonString json)
            with
            | :? IOException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileReadFailed"
                            (sprintf "Failed to read snapshot file '%s': %s" path ex.Message)))
            | :? System.UnauthorizedAccessException as ex ->
                Task.FromResult(
                    Result.failureOf (
                        adapterError
                            "fileAccessDenied"
                            (sprintf "Access denied reading snapshot file '%s': %s" path ex.Message)))

    /// Chapter A.4.7 slice δ. The OSSYS adapter's `RegisteredTransform`
    /// surface — metadata-only, per the adapter's boundary-stage
    /// nature. The adapter's `parse : SnapshotSource -> Task<Result<
    /// Catalog>>` is not a pure `Catalog -> Lineage<Diagnostics<...>>`
    /// transformation (it does I/O for `SnapshotFile`, returns
    /// `Task<Result<...>>` for boundary-error reporting); the
    /// `RegisteredTransform<'In, 'Out>` typed shell doesn't fit cleanly.
    /// Slice δ ships `registeredMetadata : RegisteredTransformMetadata`
    /// — the metadata view of the adapter's harvest-discipline
    /// classification, suitable for the registry's totality-coverage
    /// scan (slice θ) and manifest emission (slice η).
    ///
    /// Per the chapter A.4.7 open: "every transformative rule (filters,
    /// remaps, derivations — not pass-through field-to-field mappings)
    /// gets a RegisteredTransform entry." Slice δ packages the rules
    /// as Sites within one registry entry (intra-adapter classification
    /// fidelity per pillar 9 + Q11); per-rule separate registration
    /// would require extracting each helper into a standalone
    /// transformation, which is a larger refactor deferred-with-trigger
    /// (real consumer pressure for per-rule audit granularity).
    ///
    /// All adapter rules classify as `DataIntent`. The adapter is a
    /// translation layer carrying V1 source-schema evidence forward
    /// into V2 typed evidence; no operator opinion enters at the
    /// adapter boundary (the operator-intent passes — IsActive
    /// filter retired at slice β, etc. — run downstream of the
    /// adapter). The skeleton-purity property test (slice θ) will
    /// witness that `Project(catalog, Policy.empty, profile)` traverses
    /// the adapter without emitting any `OperatorIntent` lineage event.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.adapter "ossysCatalogReader" Schema
            [ TransformSite.dataIntent "identitySynthesis"
                "Synthesize V2 SsKeys from V1 names: moduleSsKey / kindSsKey / attributeSsKey / referenceSsKey / indexSsKey / triggerSsKey / sequenceSsKey / columnCheckSsKey. Derivation is deterministic from source identifiers; no operator opinion enters."
              TransformSite.dataIntent "typeTranslation"
                "Map V1 type/code values to V2 typed DUs: parsePrimitiveType (V1 dataType string → V2 PrimitiveType per A13's typed surface); parseDeleteRule (V1 OutSystems-domain onDelete code 'Delete'/'Protect'/'Ignore'/'SetNull' → V2 ReferenceAction); parseSqlForeignKeyAction (V1 #FkReality SQL-Server-domain update_referential_action_desc 'NO_ACTION'/'CASCADE'/'SET_NULL'/'SET_DEFAULT' → V2 ReferenceAction option — distinct vocabulary from parseDeleteRule per slice A.4.7'-prelude.row17-18-rowset-roundtrip); parseOrigin / parseOriginFromRowset (isExternal flag → Origin DU). All translations are structural — V1's vocabulary maps deterministically into V2's typed system."
              TransformSite.dataIntent "jsonAggregateParsing"
                "Assemble JSON-path IR records: parseAttribute / parseReference / parseIndex / parseTrigger / parseExtendedProperty / parseKind / parseModule / parseDocument / parseJsonString. Each parser threads V1 evidence into V2's typed records; the parsing is field-by-field translation with no operator overlay."
              TransformSite.dataIntent "rowsetAggregateParsing"
                "Assemble rowset-path IR records: parseAttributeRow / parseReferenceRowFor / parseKindRow / parseModuleRow / parseRowsetBundle. Mirrors the JSON-path semantics for the rowset-source variant (chapter 3.2 slice 1 onward); same DataIntent translation discipline. Slice A.4.7'-prelude.row53-source-side extended the AttributeRow projection to surface V1 `#ColumnReality` reflection (IsComputed + ComputedDefinition + DefaultConstraintName) — V1 deployed-target evidence flows into `Attribute.Computed : ComputedColumnConfig option` + `Attribute.DefaultName : Name option` via `MetadataSnapshotRunner.toBundle`'s join."
              TransformSite.dataIntent "isActiveCarryThrough"
                "Chapter A.0' slice β retroactive site. IsActive is carried through at Module / Kind / Attribute levels (not filtered at the adapter boundary; the session-21 filter was retired as a mis-placed OperatorIntent of Selection per DECISIONS 2026-05-16 (slice β) — the first worked example of pillar 9). The carriage itself is DataIntent evidence; a downstream Selection-axis pass that re-applies an inactive-records drop is deferred-with-trigger per IR-grows-under-evidence."
              TransformSite.dataIntent "tableIdCatalogRead"
                "Chapter A.0' slice θ retroactive site. V1's db_catalog field is read into TableId.Catalog (string option); cross-database FK qualification carries through without silent degradation to implicit-current-database scope. DataIntent — source-schema evidence carried forward." ]
