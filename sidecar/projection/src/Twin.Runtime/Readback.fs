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
/// Three twin-specific projections happen here, all removing objects that
/// are not estate DATA before the wipe/mint sees the catalog:
///   - the `[twin]` STATE SCHEMA is excluded (law 5 — the twin's own
///     single-row `__state` table is the tool's self-description, never
///     part of the estate);
///   - VIEWS are excluded — a view carries no data to wipe or mint, and a
///     view over multiple base tables is not even deletable (it refuses
///     `DELETE`); it stays published in the schema but is never a
///     data-bearing kind (`DECISIONS 2026-07-20`);
///   - kinds whose rows the estate itself seeded (the static-data lanes)
///     become the K1 provided pools: their PK values feed child FK draws,
///     and σ neither generates nor wipes them.
[<RequireQualifiedAccess>]
module Readback =

    /// The `[twin]` state schema — the twin's own `__state` table (law 5,
    /// its self-description), which lives outside the estate.
    let private isTwinStateSchema (k: Kind) : bool =
        TableId.schemaTextEquals "twin" k.Physical

    /// Remove every kind matching `drop` from a read-back catalog. Plain
    /// record surgery — the reconstructed catalog is a single module, so a
    /// module left empty is removed rather than left hollow.
    let private stripKinds (drop: Kind -> bool) (catalog: Catalog) : Catalog =
        { catalog with
            Modules =
                catalog.Modules
                |> List.map (fun m -> { m with Kinds = m.Kinds |> List.filter (fun k -> not (drop k)) })
                |> List.filter (fun m -> not (List.isEmpty m.Kinds)) }

    /// The normalized `schema.table` keys of every VIEW in the twin
    /// database. Read from `sys.views` so a view can be told from a base
    /// table (the read-back catalog does not distinguish them — both
    /// arrive as `Kind`s). A view has no rows to wipe or mint.
    let private readViewKeys (twinCnn: SqlConnection) : Task<Set<string>> =
        task {
            use cmd = twinCnn.CreateCommand()
            cmd.CommandText <- "SELECT SCHEMA_NAME([schema_id]) AS s, [name] AS n FROM sys.views;"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.HashSet<string>()
            let mutable more = true
            while more do
                let! has = reader.ReadAsync()
                if has then acc.Add(TableId.normalizedKeyOf (reader.GetString 0) (reader.GetString 1)) |> ignore
                else more <- false
            return Set.ofSeq acc
        }

    /// Exclude the `[twin]` state schema and every view from a read-back
    /// catalog, so the wipe and the mint see estate data only.
    let private stripNonEstate (views: Set<string>) (catalog: Catalog) : Catalog =
        let isView (k: Kind) = Set.contains (TableId.normalizedKey k.Physical) views
        stripKinds (fun k -> isTwinStateSchema k || isView k) catalog

    /// Read the twin database's catalog, rows lifted (`ReadSide.read` —
    /// row-carrying kinds arrive with `Modality.Static` populations, the
    /// provided-pool source); the state schema and views excluded.
    let read (twinCnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            let! catalog = ReadSide.read twinCnn
            let! views = readViewKeys twinCnn
            return catalog |> Result.map (stripNonEstate views)
        }

    /// Read the twin database's schema only (no row drain) — the
    /// status/check path; the state schema and views excluded.
    let readSchema (twinCnn: SqlConnection) : Task<Result<Catalog>> =
        task {
            let! catalog = ReadSide.readSchema twinCnn
            let! views = readViewKeys twinCnn
            return catalog |> Result.map (stripNonEstate views)
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
