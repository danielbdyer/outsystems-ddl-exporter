namespace Projection.Targets.SSDT

open Projection.Core

// Construction-arg records for the data-statement builders. Hoisted ahead of
// `Statement.fs` so the typed `Statement` stream can model MERGE / UPDATE (the
// `Statement.Merge` / `Statement.Update` variants carry these); the `build*`
// functions that consume them stay in `ScriptDomBuild`. Previously defined
// inside `module ScriptDomBuild`, which compiles AFTER `Statement.fs` — the
// compile-order wall that kept the data lane on per-emitter string rendering.
// Namespace-level (not in a module) so consumers that `open Projection.Targets
// .SSDT` construct them with bare field labels, exactly as they do `ColumnDef`
// / `ForeignKeyDef` from `Statement.fs`.

/// Declares the scope within which a `WHEN NOT MATCHED BY SOURCE THEN DELETE`
/// arm is permitted to delete. AC-D7 / AC-G4: an unscoped delete cannot happen —
/// the ONLY way to get a DELETE arm is to pass a `DeleteScope`. The scope
/// predicate gates eligibility: a target row not matched by any source row is
/// deleted ONLY when it ALSO satisfies the scope predicate; rows outside the
/// scope (in T−S but not in the scope) survive.
///
/// `terms` is a NON-EMPTY list of `(column, value)` equality terms over the
/// MERGE target (e.g. a tenant / partition gate), folded left-to-right with
/// `AND` into the predicate `Target.[col1] = <v1> [AND Target.[col2] = <v2> …]`.
/// Decoupled from ScriptDom so the scope is expressible from domain code.
///
/// Non-emptiness is enforced by the private field + `DeleteScope.create` smart
/// constructor: a scope with zero terms is not a scope — per AC-D7 it must never
/// degrade into an UNSCOPED delete, and `deleteScopePredicate`'s left-fold has no
/// identity for `[]`. `create` returns `None` for an empty term list, exactly the
/// faithful "no delete arm" semantics `DeleteScopePolicy.resolveFor` already
/// produces. The old public record permitted `{ Terms = [] }` — a representable
/// illegal state that would crash the fold; this makes it unconstructable.
type DeleteScope =
    private
        {
            Terms : (string * SqlLiteral) list
        }

[<RequireQualifiedAccess>]
module DeleteScope =

    /// Build a delete scope from equality terms. `None` for an empty list (no
    /// scope ⇒ no delete arm — the AC-D7-faithful rendering, never an unscoped
    /// delete); `Some` carrying the guaranteed-non-empty terms otherwise.
    let create (terms: (string * SqlLiteral) list) : DeleteScope option =
        match terms with
        | [] -> None
        | _  -> Some { Terms = terms }

    /// The guaranteed-non-empty equality terms.
    let terms (scope: DeleteScope) : (string * SqlLiteral) list = scope.Terms

/// Where a MERGE draws its source rows from — a closed dichotomy so the
/// inline/staged fork is compiler-checked, not encoded in a `string option`
/// whose `None` means one thing to one consumer and a crash to another.
///
/// `InlineValues` → `USING (VALUES (…), (…)) AS Source(cols)` — the default,
/// byte-identical to the pre-staging output. `Staged tempName` → the MERGE (and
/// the validate-before-apply guard) draw from a pre-staged `#temp` table
/// (`USING [#seed_X]`), the form that sidesteps SQL Server error 8623 (the
/// optimizer cannot plan a large row-value constructor). In the staged case
/// `Rows` still carries the rows so the caller can stage them into the `#temp`.
type MergeRowSource =
    | InlineValues
    | Staged of tempName: string

/// MERGE construction args. Decoupled from `Catalog`/`Kind`/`Attribute` (carry
/// `TableId` + name lists + typed `SqlLiteral` rows) so the builder is testable
/// in isolation and shared across every MERGE-emitting lane.
type MergeBuildArgs =
    {
        Target      : TableId
        AllColumns  : string list
        PkColumns   : string list
        UpdColumns  : string list
        Rows        : SqlLiteral list list
        CdcAware    : bool
        DeleteScope : DeleteScope option
        /// Whether the MERGE sources from the inline `VALUES` constructor or a
        /// pre-staged `#temp` (the error-8623-safe form for large kinds). See
        /// `MergeRowSource`.
        RowSource   : MergeRowSource
    }

/// UPDATE construction args. Decoupled from `Catalog`/`Kind`/`Attribute`
/// (mirrors `MergeBuildArgs`'s `TableId` + name-list shape) so the builder is
/// testable in isolation and reusable across UPDATE-emitting consumers
/// (StaticSeedsEmitter Phase-2 today; MigrationDependenciesEmitter /
/// BootstrapEmitter Phase-2 paths).
///
/// `SetCells`: column-name → typed-literal pairs the UPDATE assigns. Order
/// preserved in the emitted SET clause for T1 byte-determinism.
///
/// `WhereCells`: column-name → typed-literal pairs joined with AND in the WHERE
/// clause (typically the row's PK columns — composite PKs supported via the cell
/// list). Order preserved for T1 byte-determinism.
type UpdateBuildArgs =
    {
        Target     : TableId
        SetCells   : (string * SqlLiteral) list
        WhereCells : (string * SqlLiteral) list
        /// When `true`, append a change-detection predicate to the WHERE clause
        /// (`AND (<set-col-differs> OR ...)`) so a no-op UPDATE is structurally
        /// filtered before SQL Server observes it. Symmetric to
        /// `MergeBuildArgs.CdcAware`'s effect on the `WHEN MATCHED AND (...)`
        /// predicate. Per `DECISIONS 2026-05-18 (slice 5.13.cdc-silence-cross-
        /// emitter)`: V2 must structurally guarantee CDC silence for every
        /// emission delta variant — Phase-2 UPDATE cannot lean on SQL Server's
        /// no-op-MERGE optimization (which applies to MERGE WHEN MATCHED UPDATE,
        /// not standalone UPDATE). When `CdcAware = false` and `SetCells` is
        /// non-empty, the UPDATE fires unconditionally on PK match — the
        /// pre-slice shape, preserved for non-CDC-tracked tables.
        CdcAware   : bool
    }
