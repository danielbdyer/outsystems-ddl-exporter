namespace Projection.Adapters.Sql

// LINT-ALLOW-FILE-MUTATION: SQL reader drain loops — the per-command
//   accumulator + reader cursor mutables mirror `ReadSide`'s reified
//   lifetime discipline; BCL's SqlDataReader is itself a mutable cursor.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// The **bridge-row staging acquisition boundary** — the three live reads the
/// dynamic staging companion (`BridgeRowStagingSeam`) feeds the pure delta
/// kernel (`BridgeRowDelta`) with:
///
///   1. `distinctColumnValues` — the referenced key values: every DISTINCT
///      non-null value a retargeting FK column holds (one call per in-scope
///      reference; the seam unions them);
///   2. `snapshotByKeys` — the keyed row snapshots of the SOURCE and BRIDGE
///      kinds for exactly those keys (the `ReadSide.readRowsKeyedStream` +
///      `materializeStream` composition, chunked at the shared 900-key
///      ceiling);
///   3. `wideKeyStats` — the bridge-TABLE-wide key nullness/duplication
///      aggregates the keyed snapshot cannot see (they inform the retarget's
///      `BridgeKeyNonNull` / `BridgeKeyUnique` evidence, which must hold over
///      the WHOLE bridge table, not just the referenced subset).
///
/// Boundary discipline: identifiers flow through `SqlIdentifier.quote`
/// (ScriptDom-verified); cell values format through `ReadSide.formatRawValue`
/// (the ONE canonical raw-form contract, so a key read here round-trips
/// through the keyed `WHERE … IN` parameters); command timeouts through
/// `CommandTimeoutPolicy.resolve`. Functions raise on connection/SQL failure —
/// the calling seam guards the whole acquisition into a NAMED refusal (the
/// `ClosureOracle` shape).
[<RequireQualifiedAccess>]
module BridgeSnapshotReader =

    /// The shared keyed-read chunk ceiling (`ClosureOracle.chunkSize`'s value —
    /// safely under SQL Server's parameter limit).
    [<Literal>]
    let private ChunkSize : int = 900

    let private quotedTable (kind: Kind) : string =
        System.String.Concat(
            SqlIdentifier.quote (TableId.schemaText kind.Physical),
            ".",
            SqlIdentifier.quote (TableId.tableText kind.Physical))  // LINT-ALLOW: terminal SQL-text boundary; both identifiers quoted via SqlIdentifier.quote

    let private quotedColumn (attr: Attribute) : string =
        SqlIdentifier.quote (ColumnRealization.columnNameText attr.Column)

    /// Every DISTINCT non-null value of `attr` on `kind`, in deterministic
    /// (server-collation) order, formatted to the canonical raw form. The
    /// referenced-key read: one call per in-scope retargeting FK column.
    let distinctColumnValues
        (cnn: SqlConnection)
        (kind: Kind)
        (attr: Attribute)
        : Task<string list> =
        task {
            use _ = Bench.scope "adapter.bridgeSnapshot.referencedKeys"
            use cmd = cnn.CreateCommand()
            cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            let col = quotedColumn attr
            cmd.CommandText <-
                System.String.Concat(
                    "SELECT DISTINCT ", col,
                    " FROM ", quotedTable kind,
                    " WHERE ", col, " IS NOT NULL ORDER BY ", col)  // LINT-ALLOW: terminal SQL-text boundary mirroring ReadSide's SELECT construction; identifiers quoted, no values interpolated
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<string>()
            let mutable go = true
            while go do
                let! hasRow = reader.ReadAsync()
                if hasRow then
                    if not (reader.IsDBNull 0) then
                        acc.Add(ReadSide.formatRawValue attr.Type (reader.GetValue 0))
                else go <- false
            return List.ofSeq acc
        }

    /// Snapshot the rows of `kind` whose `keyColumn` value is in `keys` —
    /// chunked keyed reads (900-key ceiling), materialized at the IR grain.
    /// The caller projects the cells it needs (data minimization happens at
    /// the persistence boundary, not the wire).
    let snapshotByKeys
        (cnn: SqlConnection)
        (kind: Kind)
        (keyColumn: Name)
        (keys: string list)
        : Task<StaticRow list> =
        task {
            use _ = Bench.scope "adapter.bridgeSnapshot.keyedRows"
            let chunks = List.chunkBySize ChunkSize keys
            let acc = System.Collections.Generic.List<StaticRow>()
            for chunk in chunks do
                let! rows =
                    ReadSide.readRowsKeyedStream cnn kind keyColumn chunk
                    |> ReadSide.materializeStream kind
                    |> AsyncStream.toList
                acc.AddRange rows
            return List.ofSeq acc
        }

    /// The bridge-TABLE-wide key soundness aggregates: how many rows carry a
    /// NULL key, and how many key values occur on more than one row — over the
    /// WHOLE table (the keyed snapshot cannot see beyond the referenced
    /// subset, and the retarget's key-uniqueness/nullness checks must hold
    /// globally for the FK constraint to be sound).
    let wideKeyStats
        (cnn: SqlConnection)
        (kind: Kind)
        (attr: Attribute)
        : Task<BridgeWideStats> =
        task {
            use _ = Bench.scope "adapter.bridgeSnapshot.wideKeyStats"
            let col = quotedColumn attr
            let table = quotedTable kind
            use nullCmd = cnn.CreateCommand()
            nullCmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            nullCmd.CommandText <-
                System.String.Concat(
                    "SELECT COUNT_BIG(*) FROM ", table, " WHERE ", col, " IS NULL")  // LINT-ALLOW: terminal SQL-text boundary; identifiers quoted, no values interpolated
            let! nullsObj = nullCmd.ExecuteScalarAsync()
            use dupCmd = cnn.CreateCommand()
            dupCmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            dupCmd.CommandText <-
                System.String.Concat(
                    "SELECT COUNT_BIG(*) FROM (SELECT ", col,
                    " FROM ", table,
                    " WHERE ", col, " IS NOT NULL GROUP BY ", col,
                    " HAVING COUNT_BIG(*) > 1) AS [dup]")  // LINT-ALLOW: terminal SQL-text boundary; identifiers quoted, no values interpolated
            let! dupsObj = dupCmd.ExecuteScalarAsync()
            return
                { KeyNullCount      = System.Convert.ToInt64 nullsObj
                  KeyDuplicateCount = System.Convert.ToInt64 dupsObj }
        }
