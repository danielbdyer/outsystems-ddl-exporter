namespace Projection.Pipeline

open Projection.Core
open Projection.Core.Passes
open FsToolkit.ErrorHandling

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

    let private bindingError = Binding.error ConfigAxis.RenameBinding

    let private bindLogicalKey (l: Config.LogicalName) : Result<TableRename.RenameKey> =
        validation {
            let! m = Name.create l.Module
            and! e = Name.create l.Entity
            return TableRename.Logical (m, e)
        }

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
        validation {
            let! k = bindKey cr.From
            and! t = bindTarget cr.To
            return { TableRename.Key = k; TableRename.Target = t }
        }

    /// Convert a list of config-shape renames into typed Core-shape
    /// `RenameSpec`s. Errors aggregate.
    let fromConfig (renames: Config.TableRename list) : Result<TableRename.RenameSpec list> =
        renames |> List.map bindOne |> Result.aggregate
