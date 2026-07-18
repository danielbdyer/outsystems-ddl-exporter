namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Twin.Core

/// THE TWIN — read the twin back (Twin.Runtime).
///
/// The twin catalog is obtained by reading the deployed twin database
/// through the kernel's `ReadSide` — the identity ACL's ground truth:
/// absent `Projection.SsKey` extended properties (the ejected estate
/// carries none), `ReadSide` synthesizes deterministic name-based keys,
/// so coordinate binding is stable run to run by construction.
///
/// Two twin-specific projections happen here:
///   - the `[twin]` furniture is excluded (law 5 — the state table is
///     the tool's own, never part of the estate);
///   - kinds whose rows the estate itself seeded (the static-data
///     lanes) become the K1 provided pools: their PK values feed child
///     FK draws, and σ neither generates nor wipes them.
[<RequireQualifiedAccess>]
module Readback =

    let private isTwinFurniture (k: Kind) : bool =
        TableId.schemaTextEquals "twin" k.Physical

    /// Drop the `[twin]` furniture from a read-back catalog. Plain
    /// record surgery — the reconstructed catalog is a single module, so
    /// emptied modules are removed rather than left hollow.
    let private stripTwinFurniture (catalog: Catalog) : Catalog =
        { catalog with
            Modules =
                catalog.Modules
                |> List.map (fun m -> { m with Kinds = m.Kinds |> List.filter (fun k -> not (isTwinFurniture k)) })
                |> List.filter (fun m -> not (List.isEmpty m.Kinds)) }

    /// Read the twin database's catalog, rows lifted (`ReadSide.read` —
    /// row-carrying kinds arrive with `Modality.Static` populations, the
    /// provided-pool source), `[twin]` furniture excluded.
    let read (twinCnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            let! catalog = ReadSide.read twinCnn
            return catalog |> Result.map stripTwinFurniture
        }

    /// Read the twin database's schema only (no row drain) — the
    /// status/check path.
    let readSchema (twinCnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            let! catalog = ReadSide.readSchema twinCnn
            return catalog |> Result.map stripTwinFurniture
        }

    /// The K1 provided pools of a read-back catalog: for every kind
    /// carrying a static population (rows the estate's own lanes
    /// seeded), the PK raw values of those rows. A kind without a PK
    /// contributes an empty pool (nothing can reference it).
    let providedPools (catalog: Catalog) : Map<SsKey, string list> =
        Catalog.allKinds catalog
        |> List.choose (fun kind ->
            let rows =
                kind.Modality
                |> List.tryPick (function ModalityMark.Static populations -> Some populations | _ -> None)
            match rows with
            | None | Some [] -> None
            | Some populations ->
                let pool =
                    match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
                    | None -> []
                    | Some pk ->
                        populations
                        |> List.choose (fun r -> Map.tryFind pk.Name r.Values |> Option.flatten)
                Some (kind.SsKey, pool))
        |> Map.ofList
