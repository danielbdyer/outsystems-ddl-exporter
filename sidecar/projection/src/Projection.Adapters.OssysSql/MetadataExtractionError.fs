namespace Projection.Adapters.OssysSql

open System
open Microsoft.Data.SqlClient
open Projection.Core

/// Classified failure modes for `MetadataSnapshotRunner.runAsync`.
///
/// V1's `Osm.Pipeline.SqlExtraction.MetadataSnapshotRunner` catches three
/// distinct exception classes (`MetadataRowMappingException`,
/// `MetadataResultSetMissingException`, `DbException`) and emits a per-class
/// `ValidationError` code. V2 lifts the same axis as a closed DU at the
/// adapter boundary: the typed surface makes the operator-distinguishable
/// failure modes structurally visible (pillar 9 DataIntent classification —
/// every adapter-boundary error is one of these named structural shapes),
/// and downstream consumers can pattern-match without re-parsing message text.
///
/// **Per V2 pattern #11 (ternary outcome space + named keep-reason
/// variants):** the variants are exhaustive over the production-wiring
/// failure axes V2 observes today; closed-DU expansion is the discipline
/// for adding new variants when their producer ships.
///
/// Matrix rows 32 (exception classification) + 34 (transient-retry; the
/// `TransientSqlError` variant is what's left after Polly retry
/// exhaustion) + 35 (result-set count contract — `ResultSetMissing`
/// variant produced by `resultSetContractCheck`).
[<RequireQualifiedAccess>]
type MetadataExtractionError =
    /// A row-mapper closure raised an exception while parsing one row of a
    /// known result set. `ResultSetName` names the V2-consumed rowset
    /// (`"modules"`, `"entities"`, …); `RowIndex` is the zero-based row
    /// position within that result set; `Inner` is the original exception
    /// from the mapper (typically `InvalidCastException` / `InvalidOperationException`
    /// from the typed column readers).
    | RowMappingFailure of resultSetName: string * rowIndex: int * inner: exn
    /// The carbon-copied OSSYS rowsets script emitted fewer result sets
    /// than V2's runner expects. `ExpectedCount` is the script's
    /// documented contract (22 user-visible result sets per V1's
    /// `outsystems_metadata_rowsets.sql`); `ActualCount` is what V2
    /// observed via `NextResultAsync`. Surfaced when the SQL contract
    /// drifts (e.g., a V1 trunk refactor drops a rowset). Distinct from
    /// `RowMappingFailure` because the failure is at the *script-shape*
    /// level, not the per-row level; operator response is "investigate
    /// SQL-contract drift," not "investigate per-column mismatch."
    | ResultSetMissing of expectedCount: int * actualCount: int
    /// Polly's retry pipeline exhausted its transient-SQL retries on the
    /// command-execute boundary. `SqlNumber` is the final
    /// `SqlException.Number` observed; `Message` is its message text.
    /// **Distinct from `OtherSqlError`** — transient classification means
    /// the runner attempted retries before surfacing this; operator
    /// response is typically "retry the full extraction" (e.g., re-run the
    /// canary against a different OSSYS replica) rather than "investigate
    /// the SQL contract."
    | TransientSqlError of sqlNumber: int * message: string
    /// Non-transient SQL exception, or any other exception that escaped
    /// classification. `Message` carries the exception's message text. No
    /// retry was attempted (the predicate refused to classify it as
    /// transient).
    | OtherSqlError of message: string

/// Internal F# exception type tagged at the row-mapping boundary inside
/// `readResultSet` to carry the `resultSetName + rowIndex` context the
/// outer classifier needs. Not exposed in `MetadataExtractionError`
/// (the DU's `RowMappingFailure` is the public form). Translation happens
/// in `MetadataExtractionError.classify`.
exception RowMappingException of resultSetName: string * rowIndex: int * inner: exn

[<RequireQualifiedAccess>]
module MetadataExtractionError =

    /// Stable error-code routing prefix per V2 diagnostic conventions
    /// (`<domain>.<subject>.<problem>`; lower-dot). Each DU variant maps
    /// to a distinct code so consumers can route by `ValidationError.Code`
    /// without parsing message text.
    [<Literal>]
    let CodeRowMapping = "adapter.ossysSql.rowMapping"

    [<Literal>]
    let CodeResultSetContractBreach = "adapter.ossysSql.resultSetContractBreach"

    [<Literal>]
    let CodeTransient = "adapter.ossysSql.transient"

    [<Literal>]
    let CodeOtherSql = "adapter.ossysSql.runFailed"

    /// Classify an exception into the closed-DU `MetadataExtractionError`.
    /// Row-mapping exceptions tagged by `readResultSet` lift to
    /// `RowMappingFailure`; `SqlException` matched by the transient
    /// predicate lifts to `TransientSqlError`; anything else lifts to
    /// `OtherSqlError`.
    let classify (isTransient: exn -> bool) (ex: exn) : MetadataExtractionError =
        match ex with
        | RowMappingException (resultSetName, rowIndex, inner) ->
            MetadataExtractionError.RowMappingFailure (resultSetName, rowIndex, inner)
        | :? SqlException as sql when isTransient sql ->
            MetadataExtractionError.TransientSqlError (sql.Number, sql.Message)
        | _ ->
            MetadataExtractionError.OtherSqlError ex.Message

    /// Project a classified error to V2's boundary-adapter
    /// `ValidationError` surface. Codes are pinned (no interpolation in
    /// the code surface); the message embeds dynamic context.
    let toValidationError (error: MetadataExtractionError) : ValidationError =
        match error with
        | MetadataExtractionError.RowMappingFailure (resultSetName, rowIndex, inner) ->
            let message =
                sprintf
                    "Failed to map row %d of result set '%s': %s"
                    rowIndex
                    resultSetName
                    inner.Message
            ValidationError.createWithMetadata
                CodeRowMapping
                message
                (Map.ofList [
                    "resultSet", Some resultSetName
                    "rowIndex", Some (string rowIndex)
                    "innerType",
                        (match inner.GetType().FullName with
                         | null -> None
                         | name -> Some name)
                ])
        | MetadataExtractionError.ResultSetMissing (expectedCount, actualCount) ->
            let prose =
                sprintf
                    "OSSYS rowsets script emitted %d result set(s); expected %d. SQL-contract drift suspected."
                    actualCount
                    expectedCount
            ValidationError.createWithMetadata
                CodeResultSetContractBreach
                prose
                (Map.ofList [
                    "expectedCount", Some (string expectedCount)
                    "actualCount",   Some (string actualCount)
                ])
        | MetadataExtractionError.TransientSqlError (sqlNumber, message) ->
            let prose =
                sprintf
                    "Transient SQL error %d after retry exhaustion: %s"
                    sqlNumber
                    message
            ValidationError.createWithMetadata
                CodeTransient
                prose
                (Map.ofList [
                    "sqlNumber", Some (string sqlNumber)
                ])
        | MetadataExtractionError.OtherSqlError message ->
            ValidationError.create
                CodeOtherSql
                (sprintf "MetadataSnapshotRunner.runAsync failed: %s" message)

    /// Assert observed result-set count matches the script's contract.
    /// Returns `Ok ()` on match; `Error [ValidationError]` carrying
    /// `ResultSetMissing` on mismatch. Pure — separated from `runAsync`
    /// so the assertion can be unit-tested without a live SqlConnection.
    let resultSetContractCheck (expectedCount: int) (actualCount: int) : Result<unit> =
        if actualCount = expectedCount then
            Result.success ()
        else
            MetadataExtractionError.ResultSetMissing (expectedCount, actualCount)
            |> toValidationError
            |> Result.failureOf
