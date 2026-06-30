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
    let private quantumCellsOver (basis: RowBasis) (attrs: Attribute list) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        let cols =
            attrs
            |> List.map (fun a ->
                let get =
                    if Set.contains a.Name deferred then (fun _ -> "")
                    else RowQuantum.cellGetter basis a.Name
                ColumnRealization.columnNameText a.Column, a.Type, get)
        rows
        |> List.map (fun q ->
            cols |> List.map (fun (col, ty, get) -> { Column = col; Type = ty; Raw = get q }))

    let quantumCellRows (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellsOver basis kind.Attributes deferred rows

    /// The minted-bulk-lane projection at the quantum grain — every
    /// attribute EXCEPT the IDENTITY PK (the Sink mints it).
    let quantumCellRowsExcludingIdentity (basis: RowBasis) (kind: Kind) (deferred: Set<Name>) (rows: RowQuantum list) : CellValue list list =
        quantumCellsOver basis
            (kind.Attributes |> List.filter (fun a -> not (a.IsPrimaryKey && a.IsIdentity)))
            deferred rows

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

    /// Phase-2 UPDATE for one row: set the deferred FK columns to their
    /// (already remapped, plan-side) values, keyed by the kind's primary
    /// key. `None` when the kind has no PK or no deferred columns.
    let phase2UpdateSql (kind: Kind) (deferred: Set<Name>) (row: StaticRow) : string option =
        let pkAttrs = kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let deferredAttrs = kind.Attributes |> List.filter (fun a -> Set.contains a.Name deferred)
        if List.isEmpty pkAttrs || List.isEmpty deferredAttrs then None
        else
            let cellOf (a: Attribute) : string * SqlLiteral =
                let lit =
                    Map.tryFind a.Name row.Values
                    |> Option.defaultValue ""
                    |> SqlLiteral.ofRaw a.Type
                ColumnRealization.columnNameText a.Column, lit
            Some (renderPhase2Update
                { Target     = kind.Physical
                  SetCells   = deferredAttrs |> List.map cellOf
                  WhereCells = pkAttrs |> List.map cellOf
                  CdcAware   = true })

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
