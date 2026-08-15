namespace Projection.Pipeline
// LINT-ALLOW-FILE: physical-residue probe at the SQL boundary (the data-sink
//   chapter, S12) — one INFORMATION_SCHEMA query literal and a result drain
//   at the DB boundary; the structural surface is the typed ClaimSet list
//   the adjudicator consumes.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Adapters.OssysSql

/// The physical-residue sweep (the data-sink chapter, S12 — the S8/O4
/// deferral's table grain, cashed in its stated shape): an
/// `INFORMATION_SCHEMA` probe beside the OSSYS read — the one place the
/// estate mode deliberately looks past OSSYS — subtracted by the tables the
/// witnessed edition claims. What remains exists on disk with NO metadata
/// claim at all: outside the modeled estate entirely, the K6
/// present-but-unclaimed case (`PhysicalUnclaimed`, DECIDE).
///
/// The sweep's UNIVERSE is the platform's entity-table shape (`OSUSR_%`) —
/// a named caveat, never silent: an arbitrarily-named external table is
/// indistinguishable from a non-OutSystems table, so it stays out of the
/// sweep (its claim, when one exists, still adjudicates normally). The
/// finer residue grains the deferral also names (columns / triggers /
/// computed columns) stay deferred under the same trigger.
[<RequireQualifiedAccess>]
module SinkResidue =

    /// The tables the witnessed edition claims — every entity row, active
    /// AND tombstoned (a tombstone still claims its table until DbCleaner
    /// drops it; only a table with NO metadata row at all is residue).
    let claimedTables (snapshot: MetadataSnapshotRunner.MetadataSnapshot) : Set<string> =
        snapshot.Entities
        |> List.map (fun e -> e.PhysicalTableName.ToUpperInvariant())
        |> Set.ofList

    /// Probe the environment's OutSystems data-table universe.
    let probeUniverse (cnn: SqlConnection) : Task<Result<(string * string) list>> =
        task {
            try
                use _ = Bench.scope "sink.residue.probe"
                use cmd = cnn.CreateCommand()
                cmd.CommandText <-
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES \
                     WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME LIKE 'OSUSR[_]%' \
                     ORDER BY TABLE_SCHEMA, TABLE_NAME"
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                use! reader = cmd.ExecuteReaderAsync()
                let found = System.Collections.Generic.List<string * string>()
                let mutable advanced = true
                while advanced do
                    let! more = reader.ReadAsync()
                    advanced <- more
                    if more then found.Add(reader.GetString 0, reader.GetString 1)
                return Result.success (List.ofSeq found)
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.create "sink.residue.probeFailed"
                            (System.String.Concat("the residue probe did not complete: ", ex.Message)))
        }

    /// The residue: the probed universe minus the witnessed edition's
    /// claims, shaped as assemble-empty claim sets — the adjudicator maps
    /// them to `Unclaimed` by construction, so the residue rides the SAME
    /// outcome plane as every other claim decision.
    let sweep
        (cnn: SqlConnection)
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        : Task<Result<PhysicalClaimRules.ClaimSet list>> =
        task {
            match! probeUniverse cnn with
            | Error es -> return Result.failure es
            | Ok universe ->
                let claimed = claimedTables snapshot
                return
                    universe
                    |> List.filter (fun (_, table) -> not (Set.contains (table.ToUpperInvariant()) claimed))
                    |> List.map (fun (schema, table) ->
                        ({ Schema = schema; Table = table; Claims = [] } : PhysicalClaimRules.ClaimSet))
                    |> Result.success
        }
