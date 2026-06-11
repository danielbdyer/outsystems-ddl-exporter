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

    // Physical-identifier comparison is case-insensitive (SQL default
    // collation) via the one named policy — `TableId.tableTextEquals` /
    // `ColumnRealization.columnNameEquals`. N3: `CatalogResolution`'s
    // physical lookups now share this name rather than re-deciding case.
    let private findKindByTable (table: string) (catalog: Catalog) : Kind option =
        Catalog.allModulesKinds catalog
        |> List.map snd
        |> List.tryFind (fun k -> TableId.tableTextEquals table k.Physical)

    let private findAttributeByColumn (column: string) (kind: Kind) : Attribute option =
        kind.Attributes
        |> List.tryFind (fun a -> ColumnRealization.columnNameEquals column a.Column)

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

    /// A parsed `--user-map` CSV row: the physical table to reconcile, a
    /// Source surrogate key, and the pre-existing Sink surrogate it maps to.
    /// Generalizes V1's `UserMapLoader` (the User-FK reflow override file)
    /// to any kind: `table,sourceKey,assignedKey` per line.
    type UserMapEntry =
        {
            Table    : string
            Source   : string
            Assigned : string
        }

    /// Parse a `--user-map` CSV body into typed rows. Blank lines are
    /// skipped; a leading header line beginning `table,` is skipped; every
    /// remaining line must carry exactly three non-blank comma-separated
    /// fields. Aggregates per-line errors so the operator sees them at once.
    let parseUserMapCsv (csv: string) : Result<UserMapEntry list> =
        let lines =
            csv.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            |> Array.toList
            |> List.map (fun l -> l.Trim())
            |> List.filter (fun l -> l <> "")
        let dataLines =
            match lines with
            | header :: rest when header.ToLowerInvariant().StartsWith "table," -> rest
            | _ -> lines
        let parseLine (lineNo: int) (line: string) : Result<UserMapEntry> =
            let parts = line.Split(',') |> Array.map (fun p -> p.Trim())
            if parts.Length <> 3 then
                Result.failureOf
                    (specInvalid "transfer.userMap.shape"
                        (sprintf "user-map line %d ('%s') must be 'table,sourceKey,assignedKey' (3 fields)." lineNo line))
            elif parts |> Array.exists String.IsNullOrWhiteSpace then
                Result.failureOf
                    (specInvalid "transfer.userMap.fieldEmpty"
                        (sprintf "user-map line %d ('%s') has an empty field." lineNo line))
            else
                Result.success { Table = parts.[0]; Source = parts.[1]; Assigned = parts.[2] }
        let parsed = dataLines |> List.mapi (fun i l -> parseLine (i + 1) l)
        let errors = parsed |> List.collect (function Ok _ -> [] | Error es -> es)
        if not (List.isEmpty errors) then Result.failure errors
        else Result.success (parsed |> List.choose (function Ok e -> Some e | _ -> None))

    /// Resolve parsed `UserMapEntry`s against the reconstructed `Catalog`
    /// into a `Map<SsKey, ReconciliationStrategy>` of `ManualOverride`
    /// strategies (one per table; the per-table `Map<SourceKey,
    /// AssignedKey>` is the explicit operator surrogacy map). Aggregates
    /// table-not-found errors; a duplicate (table, sourceKey) is rejected.
    let resolveUserMap
        (catalog: Catalog)
        (entries: UserMapEntry list)
        : Result<Map<SsKey, ReconciliationStrategy>> =
        let resolveTable (table: string, rows: UserMapEntry list) : Result<SsKey * ReconciliationStrategy> =
            match findKindByTable table catalog with
            | None ->
                Result.failureOf
                    (specInvalid "transfer.userMap.tableNotFound"
                        (sprintf "user-map: no kind found for table '%s' in the contract." table))
            | Some k ->
                let dupSources =
                    rows
                    |> List.groupBy (fun r -> r.Source)
                    |> List.choose (fun (s, g) -> if g.Length > 1 then Some s else None)
                if not (List.isEmpty dupSources) then
                    Result.failure
                        (dupSources
                         |> List.map (fun s ->
                            specInvalid "transfer.userMap.duplicateSource"
                                (sprintf "user-map: table '%s' maps source key '%s' more than once." table s)))
                else
                    let overrides =
                        rows
                        |> List.map (fun r -> SourceKey.ofString r.Source, AssignedKey.ofString r.Assigned)
                        |> Map.ofList
                    Result.success (k.SsKey, ReconciliationStrategy.ManualOverride overrides)
        let resolved = entries |> List.groupBy (fun e -> e.Table) |> List.map resolveTable
        let errors = resolved |> List.collect (function Ok _ -> [] | Error es -> es)
        if not (List.isEmpty errors) then Result.failure errors
        else Result.success (resolved |> List.choose (function Ok p -> Some p | _ -> None) |> Map.ofList)

    /// Combine `--reconcile` (MatchByColumn) and `--user-map`
    /// (ManualOverride) strategies into one reconciliation map. A kind
    /// reconciled by BOTH a match-column AND an explicit override is an
    /// operator conflict (two strategies for one kind) and is rejected.
    let resolveAllReconciliation
        (catalog: Catalog)
        (reconcileEntries: ReconcileEntry list)
        (userMapEntries: UserMapEntry list)
        : Result<Map<SsKey, ReconciliationStrategy>> =
        let collect = function Ok _ -> [] | Error es -> es
        match resolveReconciliation catalog reconcileEntries, resolveUserMap catalog userMapEntries with
        | Ok byColumn, Ok byOverride ->
            let conflicts =
                byColumn |> Map.toList |> List.map fst
                |> List.filter (fun k -> Map.containsKey k byOverride)
            if not (List.isEmpty conflicts) then
                Result.failure
                    (conflicts
                     |> List.map (fun k ->
                        specInvalid "transfer.reconcile.strategyConflict"
                            (sprintf "kind %s is reconciled by both --reconcile and --user-map; choose one." (SsKey.rootOriginal k))))
            else
                Result.success (Map.fold (fun acc k v -> Map.add k v acc) byColumn byOverride)
        | r1, r2 ->
            Result.failure (collect r1 @ collect r2)
