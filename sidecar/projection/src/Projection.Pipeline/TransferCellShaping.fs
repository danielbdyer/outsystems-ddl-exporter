namespace Projection.Pipeline

open Projection.Core
open Projection.Targets.SSDT

/// The Projection-onto-Sink CELL/ROW shaping — pure projections of a kind's
/// post-substitution rows (materialized `StaticRow` and streaming `RowQuantum`
/// grains) into `SqlBulkCopy` cell rows and Phase-2 UPDATE statements. Lifted
/// out of `module Transfer` (the 2516-line god-module): these depend only on
/// Core / SSDT types — no transfer-run state — so they read and test as their
/// own concern. `module Transfer` consumes them as `TransferCellShaping.*`.
[<RequireQualifiedAccess>]
module TransferCellShaping =

    /// Project a kind's already-post-substitution rows into `SqlBulkCopy`
    /// cell rows. Deferred FK columns are emitted as the empty raw —
    /// `KeepNulls` maps that to SQL NULL — so Phase 1 satisfies a cycle;
    /// Phase 2 re-points them.
    let private toCellsOver (attrs: Attribute list) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        rows
        |> List.map (fun row ->
            attrs
            |> List.map (fun a ->
                let raw =
                    if Set.contains a.Name deferred then ""
                    else Map.tryFind a.Name row.Values |> Option.defaultValue ""
                { Column = ColumnRealization.columnNameText a.Column; Type = a.Type; Raw = raw }))

    let toCellRows (kind: Kind) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        toCellsOver kind.Attributes deferred rows

    /// The minted-bulk-lane projection: every attribute EXCEPT the IDENTITY
    /// PK (the Sink mints it; `Bulk.copyRowsSinkMinted` carries no
    /// `KeepIdentity`).
    let toCellRowsExcludingIdentity (kind: Kind) (deferred: Set<Name>) (rows: StaticRow list) : CellValue list list =
        toCellsOver
            (kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity)))
            deferred rows

    /// Q3 — the cell projections at the quantum grain (A40 siblings of
    /// `toCellsOver`; the streaming realization's lanes consume these):
    /// per-column getters are STAGED against the stream's (renamed) basis
    /// once per kind, then applied per row. Deferred FK columns emit the
    /// empty raw (SQL NULL under KeepNulls), exactly as the Map-carried
    /// projection does.
    /// PL-9 (S16): the getter staging (`cols`) binds BEFORE the returned
    /// closure, so a caller that stages once per kind pays the
    /// per-attribute basis scans once — the docstring's "once per kind"
    /// made true. The row application is unchanged.
    let private quantumCellsOverStaged (basis: RowBasis) (attrs: Attribute list) (deferred: Set<Name>) : RowQuantum list -> CellValue list list =
        let cols =
            attrs
            |> List.map (fun a ->
                let get =
                    if Set.contains a.Name deferred then (fun _ -> "")
                    else RowQuantum.cellGetter basis a.Name
                ColumnRealization.columnNameText a.Column, a.Type, get)
        fun rows ->
            rows
            |> List.map (fun q ->
                cols |> List.map (fun (col, ty, get) -> { Column = col; Type = ty; Raw = get q }))

    /// The staged per-kind projection (PL-9 / S16): stage once beside the
    /// kind's chunk loop, apply per chunk.
    let quantumCellRowsStaged (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) : RowQuantum list -> CellValue list list =
        quantumCellsOverStaged basis kind.Attributes deferred

    /// The staged minted-bulk-lane projection — every attribute EXCEPT the
    /// IDENTITY PK (the Sink mints it).
    let quantumCellRowsExcludingIdentityStaged (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) : RowQuantum list -> CellValue list list =
        quantumCellsOverStaged basis
            (kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity)))
            deferred

    let quantumCellRows (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellRowsStaged basis kind deferred rows

    /// The minted-bulk-lane projection at the quantum grain — every
    /// attribute EXCEPT the IDENTITY PK (the Sink mints it).
    let quantumCellRowsExcludingIdentity (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellRowsExcludingIdentityStaged basis kind deferred rows

    /// Render one Phase-2 UPDATE from typed args through the SAME typed node the
    /// SSDT data lane uses (`ScriptDomBuild.buildUpdateStatement`) — not a
    /// hand-built `sprintf "UPDATE … SET … WHERE …;"`. `generateOne` omits the
    /// trailing `;` SQL Server requires after a single-statement render, so it is
    /// appended here — the executeBatch contract: `;`-terminated statements
    /// joined by `\n`. `CdcAware = true` appends the typed change-detection
    /// predicate so an idempotent re-point is a structural no-op (CDC-silent
    /// under `--allow-cdc`, write-minimal on a non-CDC sink) — the cross-emitter
    /// CDC-silence invariant the per-row string copy used to bypass.
    let private renderPhase2Update (args: UpdateBuildArgs) : string =
        System.String.Concat(  // LINT-ALLOW: terminal `;` statement terminator on a fully-typed UPDATE render; generateOne omits it on a single-statement render (same as the data lane's renderDataBatch)
            ScriptDomGenerate.generateOne
                ((ScriptDomBuild.buildUpdateStatement args).Value
                    :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";")

    /// `phase2UpdateSql` STAGED (PL-9 / S17 — the StaticRow twin of
    /// `phase2UpdateSqlQuantum`): the kind's PK/deferred attribute walks
    /// and per-attribute column-name texts resolve ONCE per (kind,
    /// deferred); the returned closure renders one row's UPDATE. A kind
    /// with no PK or no deferred columns resolves to a constant `None`
    /// closure once, never per row.
    let phase2UpdateSqlStaged (kind: Kind) (deferred: Set<Name>) : (StaticRow -> string option) =
        let pkAttrs = kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let deferredAttrs = kind.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        if List.isEmpty pkAttrs || List.isEmpty deferredAttrs then (fun _ -> None)
        else
            // Stage each attribute's (column text, name, type) once; per row
            // only the Map lookup + literal coercion remain.
            let cellGetterOf (a: Attribute) : StaticRow -> string * SqlLiteral =
                let col = ColumnRealization.columnNameText a.Column
                let name = a.Name
                let ty = a.Type
                fun row ->
                    col,
                    (Map.tryFind name row.Values
                     |> Option.defaultValue ""
                     |> SqlLiteral.ofRaw ty)
            let setGetters   = deferredAttrs |> List.map cellGetterOf
            let whereGetters = pkAttrs       |> List.map cellGetterOf
            fun row ->
                Some (renderPhase2Update
                    { Target     = kind.Physical
                      SetCells   = setGetters   |> List.map (fun g -> g row)
                      WhereCells = whereGetters |> List.map (fun g -> g row)
                      CdcAware   = true })

    /// Phase-2 UPDATE for one row: set the deferred FK columns to their
    /// (already remapped, plan-side) values, keyed by the kind's primary
    /// key. `None` when the kind has no PK or no deferred columns.
    /// (Stage-then-apply-once form of `phase2UpdateSqlStaged` — per-kind
    /// callers stage instead.)
    let phase2UpdateSql (kind: Kind) (deferred: Set<Name>) (row: StaticRow) : string option =
        phase2UpdateSqlStaged kind deferred row

    /// `phase2UpdateSql` at the quantum grain (Q3): the per-attribute
    /// literal getters are staged against the stream's basis once per
    /// kind; the returned closure renders one row's UPDATE. A kind with
    /// no PK or no deferred columns resolves to a constant `None` closure
    /// once, never per row.
    let phase2UpdateSqlQuantum (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) : (RowQuantum -> string option) =
        let pkAttrs = kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let deferredAttrs = kind.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        if List.isEmpty pkAttrs || List.isEmpty deferredAttrs then (fun _ -> None)
        else
            // Stage the per-attribute (column, literal) getters once per kind;
            // per row they build the typed cells the typed UPDATE node consumes.
            let cellGetterOf (a: Attribute) : RowQuantum -> string * SqlLiteral =
                let get = RowQuantum.cellGetter basis a.Name
                let col = ColumnRealization.columnNameText a.Column
                fun q -> col, (get q |> SqlLiteral.ofRaw a.Type)
            let setGetters   = deferredAttrs |> List.map cellGetterOf
            let whereGetters = pkAttrs       |> List.map cellGetterOf
            fun q ->
                Some (renderPhase2Update
                    { Target     = kind.Physical
                      SetCells   = setGetters   |> List.map (fun g -> g q)
                      WhereCells = whereGetters |> List.map (fun g -> g q)
                      CdcAware   = true })
