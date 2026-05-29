namespace Projection.Pipeline

open System
open Projection.Core

/// Operator-facing `transfer`-verb spec parsing — turn the string-shaped
/// CLI arguments (`env:NAME` / `file:PATH` for connections; `<table>:<col>`
/// for reconcile entries) into typed `ConnectionRef` / per-kind
/// `ReconciliationStrategy` values the orchestrator consumes. Pure; lives
/// in Pipeline so the test pool reaches it without depending on the CLI
/// executable.
[<RequireQualifiedAccess>]
module TransferSpec =

    /// A parsed `--reconcile` entry: the physical table to reconcile + the
    /// column to match Source identities against the pre-existing Sink ones.
    type ReconcileEntry =
        {
            Table       : string
            MatchColumn : string
        }

    let private specInvalid (code: string) (message: string) : ValidationError =
        ValidationError.create code message

    /// Parse a `--source-conn` / `--sink-conn` spec ("env:NAME" or
    /// "file:PATH") into a `ConnectionRef`.
    let parseConnectionSpec (spec: string) : Result<ConnectionRef> =
        if String.IsNullOrWhiteSpace spec then
            Result.failureOf (specInvalid "transfer.connection.specEmpty" "connection spec is empty.")
        else
            let trimmed = spec.Trim()
            match trimmed.IndexOf ':' with
            | -1 ->
                Result.failureOf
                    (specInvalid "transfer.connection.specShape"
                        (sprintf "connection spec '%s' missing 'env:' or 'file:' prefix." trimmed))
            | i ->
                let prefix = trimmed.Substring(0, i).ToLowerInvariant()
                let value  = trimmed.Substring(i + 1).Trim()
                if String.IsNullOrWhiteSpace value then
                    Result.failureOf
                        (specInvalid "transfer.connection.specEmptyValue"
                            (sprintf "connection spec '%s' has an empty value after '%s:'." trimmed prefix))
                else
                    match prefix with
                    | "env"  -> Result.success (ConnectionRef.EnvVar value)
                    | "file" -> Result.success (ConnectionRef.File value)
                    | other  ->
                        Result.failureOf
                            (specInvalid "transfer.connection.specPrefix"
                                (sprintf "connection spec '%s' unknown prefix '%s' (expected 'env' or 'file')." trimmed other))

    /// Parse a `--reconcile` spec ("<table>:<match-column>") into a
    /// `ReconcileEntry`.
    let parseReconcileSpec (spec: string) : Result<ReconcileEntry> =
        if String.IsNullOrWhiteSpace spec then
            Result.failureOf (specInvalid "transfer.reconcile.specEmpty" "reconcile spec is empty.")
        else
            let trimmed = spec.Trim()
            match trimmed.IndexOf ':' with
            | -1 ->
                Result.failureOf
                    (specInvalid "transfer.reconcile.specShape"
                        (sprintf "reconcile spec '%s' missing ':' (expected <table>:<match-column>)." trimmed))
            | i ->
                let table = trimmed.Substring(0, i).Trim()
                let col   = trimmed.Substring(i + 1).Trim()
                if String.IsNullOrWhiteSpace table then
                    Result.failureOf
                        (specInvalid "transfer.reconcile.tableEmpty"
                            (sprintf "reconcile spec '%s' has an empty table name." trimmed))
                elif String.IsNullOrWhiteSpace col then
                    Result.failureOf
                        (specInvalid "transfer.reconcile.columnEmpty"
                            (sprintf "reconcile spec '%s' has an empty match-column name." trimmed))
                else
                    Result.success { Table = table; MatchColumn = col }

    let private findKindByTable (table: string) (catalog: Catalog) : Kind option =
        Catalog.allModulesKinds catalog
        |> List.map snd
        |> List.tryFind (fun k -> k.Physical.Table.Equals(table, StringComparison.OrdinalIgnoreCase))

    let private findAttributeByColumn (column: string) (kind: Kind) : Attribute option =
        kind.Attributes
        |> List.tryFind (fun a -> a.Column.ColumnName.Equals(column, StringComparison.OrdinalIgnoreCase))

    /// Resolve parsed `ReconcileEntry`s against the reconstructed
    /// `Catalog` into the `Map<SsKey, ReconciliationStrategy>` that
    /// `Transfer.runReconciling` consumes. Aggregates every spec error so
    /// the operator sees them all in one pass.
    let resolveReconciliation
        (catalog: Catalog)
        (entries: ReconcileEntry list)
        : Result<Map<SsKey, ReconciliationStrategy>> =
        let resolveOne (e: ReconcileEntry) : Result<SsKey * ReconciliationStrategy> =
            match findKindByTable e.Table catalog with
            | None ->
                Result.failureOf
                    (specInvalid "transfer.reconcile.tableNotFound"
                        (sprintf "reconcile: no kind found for table '%s' in the contract." e.Table))
            | Some k ->
                match findAttributeByColumn e.MatchColumn k with
                | None ->
                    Result.failureOf
                        (specInvalid "transfer.reconcile.columnNotFound"
                            (sprintf "reconcile: kind for '%s' has no attribute with column '%s'." e.Table e.MatchColumn))
                | Some a ->
                    Result.success (k.SsKey, ReconciliationStrategy.MatchByColumn a.Name)
        let resolved = entries |> List.map resolveOne
        let errors =
            resolved
            |> List.collect (function Ok _ -> [] | Error es -> es)
        if not (List.isEmpty errors) then Result.failure errors
        else
            let pairs = resolved |> List.choose (function Ok p -> Some p | _ -> None)
            let dups =
                pairs
                |> List.groupBy fst
                |> List.choose (fun (k, g) -> if g.Length > 1 then Some k else None)
            if not (List.isEmpty dups) then
                Result.failure
                    (dups
                     |> List.map (fun k ->
                        specInvalid "transfer.reconcile.duplicateKind"
                            (sprintf "reconcile: kind for table mapped to %s specified more than once." (SsKey.rootOriginal k))))
            else
                Result.success (Map.ofList pairs)
