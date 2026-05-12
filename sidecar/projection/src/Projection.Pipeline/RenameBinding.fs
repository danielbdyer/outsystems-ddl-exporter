namespace Projection.Pipeline

open Projection.Core
open Projection.Core.Passes

/// Boundary translation from `Config.TableRename` (JSON-shape, raw strings)
/// to `TableRename.RenameSpec` (Core-shape, typed value objects).
///
/// Per V2_PRODUCTION_CUTOVER.md §3.4 / §5.6: Config types live at the
/// Pipeline boundary and carry JSON-shape primitives; Core types carry
/// the ubiquitous-language value objects (`Name`, `TableId`). This
/// module is the binding step between the two. Lives in Pipeline
/// because it depends on `Config.TableRename` (Pipeline) and produces
/// `TableRename.RenameSpec` (Core).
///
/// Errors aggregate across entries so operators see every malformed
/// rename in one pass rather than first-error-only.
[<RequireQualifiedAccess>]
module RenameBinding =

    let private bindingError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.renameBinding.%s" code) message

    let private bindLogicalKey (l: Config.LogicalName) : Result<TableRename.RenameKey> =
        let moduleNameR = Name.create l.Module
        let entityNameR = Name.create l.Entity
        match moduleNameR, entityNameR with
        | Ok m,    Ok e    -> Result.success (TableRename.Logical (m, e))
        | Error a, Error b -> Result.failure (a @ b)
        | Error a, _       -> Result.failure a
        | _,       Error b -> Result.failure b

    let private bindPhysicalKey (p: Config.PhysicalName) : Result<TableRename.RenameKey> =
        TableId.create p.Schema p.Table
        |> Result.map TableRename.Physical

    let private bindKey (source: Config.RenameSource) : Result<TableRename.RenameKey> =
        match source with
        | Config.LogicalSource l  -> bindLogicalKey l
        | Config.PhysicalSource p -> bindPhysicalKey p

    let private bindTarget (p: Config.PhysicalName) : Result<TableId> =
        TableId.create p.Schema p.Table

    let private bindOne (cr: Config.TableRename) : Result<TableRename.RenameSpec> =
        let keyR    = bindKey cr.From
        let targetR = bindTarget cr.To
        match keyR, targetR with
        | Ok k,    Ok t    -> Result.success { TableRename.Key = k; TableRename.Target = t }
        | Error a, Error b -> Result.failure (a @ b)
        | Error a, _       -> Result.failure a
        | _,       Error b -> Result.failure b

    /// Convert a list of config-shape renames into typed Core-shape
    /// `RenameSpec`s. Errors aggregate.
    let fromConfig (renames: Config.TableRename list) : Result<TableRename.RenameSpec list> =
        renames |> List.map bindOne |> Result.aggregate
