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
    /// "file:PATH") into a `ConnectionRef`. Re-exports the one decode that now
    /// lives in `ConnectionSpec` (recon #13 — one connection-acquisition
    /// discipline); kept as `TransferSpec.parseConnectionSpec` so the many
    /// transfer-surface callers and the `transfer.connection.*` error vocabulary
    /// they pin are preserved by construction.
    let parseConnectionSpec (spec: string) : Result<ConnectionRef> =
        ConnectionSpec.parseConnectionSpec spec

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

    /// Find the kind at the LOGICAL `Module.Entity` coordinate (espace-safe — the
    /// physical OSUSR table name differs per environment, so a reconcile keyed on
    /// it would not match across the estate). Reuses
    /// `CatalogResolution.tryKindByLogical`, then recovers the `Kind` by its
    /// stable `SsKey`.
    let private findKindByLogical (moduleName: string) (entityName: string) (catalog: Catalog) : Kind option =
        CatalogResolution.tryKindByLogical catalog moduleName entityName
        |> Option.bind (fun key ->
            Catalog.allModulesKinds catalog |> List.map snd |> List.tryFind (fun k -> k.SsKey = key))

    /// Find an attribute by its LOGICAL `Name` (case-insensitive). A logical
    /// reconcile names logical attributes; `findAttributeByColumn` is the
    /// physical fallback (columns are espace-stable either way).
    let private findAttributeByName (attrName: string) (kind: Kind) : Attribute option =
        kind.Attributes
        |> List.tryFind (fun a -> System.String.Equals(Name.value a.Name, attrName, System.StringComparison.OrdinalIgnoreCase))

    /// Resolve parsed `ReconcileEntry`s against the reconstructed
    /// `Catalog` into the `Map<SsKey, ReconciliationStrategy>` that
    /// `Transfer.runReconciling` consumes. Aggregates every spec error so
    /// the operator sees them all in one pass.
    let resolveReconciliation
        (catalog: Catalog)
        (entries: ReconcileEntry list)
        : Result<Map<SsKey, ReconciliationStrategy>> =
        let resolveOne (e: ReconcileEntry) : Result<SsKey * ReconciliationStrategy> =
            // The table ref resolves LOGICALLY ("Module.Entity", espace-safe) when
            // it carries a '.', else by PHYSICAL table name (the legacy form, kept
            // working). The match column resolves by logical attribute Name OR
            // physical Column — both espace-stable.
            let kindOpt =
                match e.Table.IndexOf '.' with
                | dot when dot > 0 ->
                    match findKindByLogical (e.Table.Substring(0, dot)) (e.Table.Substring(dot + 1)) catalog with
                    | Some _ as k -> k
                    | None -> findKindByTable e.Table catalog
                | _ -> findKindByTable e.Table catalog
            match kindOpt with
            | None ->
                Result.failureOf
                    (specInvalid "transfer.reconcile.tableNotFound"
                        (sprintf "reconcile: no kind found for '%s' (tried logical Module.Entity and physical table name). For a peer transfer between differently-named environments, use the logical 'Module.Entity:Column' form — a physical name written against the source will not resolve against the sink." e.Table))
            | Some k ->
                let attrOpt =
                    match findAttributeByName e.MatchColumn k with
                    | Some _ as a -> a
                    | None -> findAttributeByColumn e.MatchColumn k
                match attrOpt with
                | None ->
                    Result.failureOf
                        (specInvalid "transfer.reconcile.columnNotFound"
                            (sprintf "reconcile: kind for '%s' has no attribute with name/column '%s'." e.Table e.MatchColumn))
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
            // The table ref resolves LOGICALLY ("Module.Entity", espace-safe)
            // when it carries a '.', else by PHYSICAL table name — the SAME
            // two-form lookup `resolveReconciliation.resolveOne` performs.
            // Physical-only resolution was espace-UNSAFE for the peer leg
            // (source and sink physical names differ per environment;
            // PARTIAL_TRANSFER_READINESS_LOG entry 5's named gap, closed
            // 2026-07-06).
            let kindOpt =
                match table.IndexOf '.' with
                | dot when dot > 0 ->
                    match findKindByLogical (table.Substring(0, dot)) (table.Substring(dot + 1)) catalog with
                    | Some _ as k -> k
                    | None -> findKindByTable table catalog
                | _ -> findKindByTable table catalog
            match kindOpt with
            | None ->
                Result.failureOf
                    (specInvalid "transfer.userMap.tableNotFound"
                        (sprintf "user-map: no kind found for '%s' (tried logical Module.Entity and physical table name). For a peer transfer between differently-named environments, use the logical 'Module.Entity' form." table))
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
