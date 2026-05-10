namespace Projection.Targets.Data

open Projection.Core

/// One orderable row of a data-insertion artifact. Per `CHAPTER_4_PRESCOPE
/// _DATA_TRIUMVIRATE.md` §2.4: the structured form NAMES the orderable units
/// (rather than carrying a `RawSql` flag); this enables the composer to
/// interleave rows from multiple emitters under one global topological
/// order before rendering.
///
/// **Slice α scope (chapter 4.1.B).** `Values` carries the raw IR-form
/// strings from `StaticRow.Values : Map<Name, string>`; type-aware SQL
/// literal rendering happens at render time via `Projection.Targets.SSDT
/// .Render.formatSqlLiteral`.
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

        /// Column-name → raw-value-string map. Raw values follow V2's
        /// IR string-form contract per `RawValueCodec`; the renderer
        /// looks up the column's `PrimitiveType` from the catalog
        /// kind's `Attribute` list and applies `Render.formatSqlLiteral`
        /// to produce the SQL literal at emission time.
        Values : Map<Name, string>

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
/// Phase 1 to break cycles. `Rendered` is the deterministic GO-batched
/// T-SQL output the canary feeds to `sqlcmd`.
///
/// **Slice α scope.** `Phase1Merges` is the only populated field;
/// `Phase2Updates` is empty and `Rendered` carries V1-shape MERGE
/// statements (no change-detection predicate yet). Slice β adds the
/// CDC-aware predicate per `Profile.CdcAwareness`; slice δ adds the
/// two-phase insertion for FK cycles.
type DataInsertScript =
    {
        /// Phase 1: MERGE statements for kinds whose FK targets are
        /// either acyclic-prior or NULL-deferred. The dominant case
        /// at slice α (no cycle-breaking yet).
        Phase1Merges : DataInsertRow list

        /// Phase 2: UPDATE statements that populate Phase-1-deferred
        /// FK columns once their target rows exist. Empty at slice α.
        Phase2Updates : DataInsertRow list

        /// The rendered T-SQL output. Deterministic across runs with
        /// equal inputs (T1 byte-determinism). GO-batched per V1
        /// convention.
        Rendered : string
    }
