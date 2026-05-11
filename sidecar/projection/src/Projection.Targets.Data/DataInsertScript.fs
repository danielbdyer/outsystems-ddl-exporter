namespace Projection.Targets.Data

open Projection.Core

/// One orderable row of a data-insertion artifact. Per `CHAPTER_4_PRESCOPE
/// _DATA_TRIUMVIRATE.md` §2.4: the structured form NAMES the orderable units
/// (rather than carrying a `RawSql` flag); this enables the composer to
/// interleave rows from multiple emitters under one global topological
/// order before rendering.
///
/// **Slice κ scope (chapter 4.1.B).** `Values : Map<Name, SqlLiteral>` —
/// the typed SQL-literal form per pillar 1 (data-structure-oriented).
/// Pre-κ the field carried raw IR strings (`Map<Name, string>`); the
/// emitter then re-converted via `SqlLiteral.ofRaw` at every MERGE /
/// UPDATE construction. Lifting the typed projection to the row level
/// strengthens the type-system contract (consumers can pattern-match on
/// `SqlLiteral` variants without re-running attribute-type lookup) and
/// removes the redundant conversion at render time.
///
/// **Slice δ scope.** `DeferredFkSet : Set<Name>` names the (attribute-name)
/// columns that cycle-break the row across the two-phase pattern: in a
/// `Phase1Merges` row the deferred columns are emitted as `NULL` in the
/// MERGE's VALUES; in a `Phase2Updates` row the same name set scopes the
/// `SET` clause that populates them once Phase-1 INSERTs have completed.
/// `Set.empty` for non-cycle rows.
type DataInsertRow =
    {
        /// The owning kind's stable identity (per A4). The composer
        /// uses this to look up the kind's `Attribute` list (for type
        /// resolution) and to interleave rows by topological order.
        KindKey : SsKey

        /// The row's stable identity (per A1 / A7: static populations
        /// carry per-row SsKey). Used for FK resolution and lineage.
        Identifier : SsKey

        /// Column-name → typed-SQL-literal. Per slice κ (chapter
        /// 4.1.B; pillar 1 strengthening): the typed shape replaces
        /// the raw-string `Map<Name, string>` so consumers query the
        /// typed value directly. Construction-time projection
        /// (`SqlLiteral.ofRaw type raw`) requires the kind's
        /// `Attribute` list for type resolution; the row constructor
        /// at the emitter does that lookup once.
        Values : Map<Name, SqlLiteral>

        /// Attribute-names of columns deferred across the two-phase
        /// pattern (slice δ; chapter 4.1.B). For `Phase1Merges` rows
        /// these columns are emitted as `NULL` so the row inserts
        /// before its same-SCC FK target exists; the matching
        /// `Phase2Updates` row carries the same `DeferredFkSet` and
        /// renders the `UPDATE … SET <deferred> = <orig>` once every
        /// Phase-1 row across the cycle is in place. Identity of a
        /// deferral is the (`KindKey`, `Identifier`, column-name)
        /// triple — the same set drives both phases.
        DeferredFkSet : Set<Name>
    }

/// Per-kind data-insertion artifact. Per pre-scope §2.4: `Phase1Merges`
/// holds INSERT-or-NoOp MERGE rows (the dominant case); `Phase2Updates`
/// holds the UPDATE statements that populate FK columns deferred in
/// Phase 1 to break cycles.
///
/// **Slice α scope.** `Phase1Merges` is the only populated field;
/// `Phase2Updates` is empty and the rendered text carries V1-shape MERGE
/// statements (no change-detection predicate yet). Slice β adds the
/// CDC-aware predicate per `Profile.CdcAwareness`; slice δ adds the
/// two-phase insertion for FK cycles.
///
/// **Slice ι scope (chapter 4.1.B; multi-kind cycle reification).**
/// The rendered text splits into `RenderedPhase1` (MERGE only) +
/// `RenderedPhase2` (UPDATE only); `Rendered` is the per-kind
/// self-complete concatenation (`RenderedPhase1 + RenderedPhase2`).
/// The split lets `DataEmissionComposer.composeRendered` produce a
/// **globally-ordered** GO-batched T-SQL where ALL Phase-1 MERGEs
/// across ALL kinds (in topological order) precede ANY Phase-2
/// UPDATE — the structural cash-out of the slice-δ improvement
/// surface item #2 (multi-kind cycles deploy correctly only when
/// the global Phase-1-then-Phase-2 boundary is preserved). Per-kind
/// `Rendered` remains correct for self-referencing FK cases (one
/// kind, FK to itself) where the per-kind concatenation IS the
/// deploy order.
type DataInsertScript =
    {
        /// Phase 1: MERGE statements for kinds whose FK targets are
        /// either acyclic-prior or NULL-deferred. The dominant case
        /// at slice α (no cycle-breaking yet).
        Phase1Merges : DataInsertRow list

        /// Phase 2: UPDATE statements that populate Phase-1-deferred
        /// FK columns once their target rows exist. Empty at slice α.
        Phase2Updates : DataInsertRow list

        /// Rendered T-SQL for the kind's Phase-1 MERGE only (no
        /// Phase-2 UPDATEs). The composer concatenates these in
        /// topological order across all kinds to form the
        /// globally-ordered Phase-1 prefix of `composeRendered`'s
        /// output. Empty string for kinds with no rows.
        RenderedPhase1 : string

        /// Rendered T-SQL for the kind's Phase-2 UPDATE statements
        /// only (no Phase-1 MERGE). The composer concatenates these
        /// in topological order across all kinds AFTER the global
        /// Phase-1 prefix to form the globally-ordered Phase-2
        /// suffix. Empty string for kinds without deferred FK
        /// columns.
        RenderedPhase2 : string

        /// The per-kind rendered T-SQL (`RenderedPhase1 +
        /// RenderedPhase2`). Deterministic across runs with equal
        /// inputs (T1 byte-determinism). GO-batched per V1
        /// convention. **Correctness scope:** self-FK kinds (single-
        /// kind cycles) deploy correctly from this per-kind view;
        /// multi-kind cycles require `DataEmissionComposer.compose
        /// Rendered` for global Phase-1-then-Phase-2 ordering across
        /// all kinds.
        Rendered : string
    }
