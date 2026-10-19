namespace Projection.Pipeline

// LINT-ALLOW-FILE: estate remediation block shaping (wave A5) — terminal
//   SQL-text emission: locating SELECTs and commented repair candidates
//   composed from typed coordinates via `SqlIdentifier` at the terminal
//   boundary (the TransferRevert / RemediationEmitter precedent), plus the
//   provenance-header composition over the connection builder's typed
//   DataSource/InitialCatalog reads.

open System
open Projection.Core
open Projection.Targets.Data
open Projection.Targets.OperationalDiagnostics

/// The per-environment remediation blocks for `check estate` (wave A5):
/// every REPAIR-lane finding naming the environment that resolves to
/// physical coordinates in that environment's catalog earns one block —
/// the block id IS the finding's cross-artifact key, the locating SELECT
/// is active, every repair candidate is commented (the operator-safety
/// contract). The per-env logical catalogs retain their physical TableId
/// and column realizations (`Readiness.toLogicalShape` normalizes names
/// only), so the SQL locates real tables.
[<RequireQualifiedAccess>]
module EstateRemediation =

    let private tableOf (kind: Kind) : string =
        SqlIdentifier.qualified (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)

    let private columnOf (a: Attribute) : string =
        SqlIdentifier.quote (ColumnRealization.columnNameText a.Column)

    /// The repairable finding subjects are `Entity.Column` by construction
    /// (the same token the FindingKey carries).
    let private coordinateOf (subject: string) : (string * string) option =
        match subject.Split('.') with
        | [| entity; col |] -> Some (entity, col)
        | _ -> None

    let private findKind (catalog: Catalog) (entity: string) : Kind option =
        Catalog.allKinds catalog |> List.tryFind (fun k -> Name.value k.Name = entity)

    let private findAttr (kind: Kind) (col: string) : Attribute option =
        kind.Attributes |> List.tryFind (fun a -> Name.value a.Name = col)

    let private zeroSentinelCount (profile: Profile option) (attrKey: SsKey) : int64 =
        profile
        |> Option.bind (fun p ->
            Profile.tryFindCategorical attrKey p
            |> Option.bind (fun c ->
                c.Frequencies
                |> List.tryFind (fun (value, _) -> value = "0")
                |> Option.map snd))
        |> Option.defaultValue 0L

    /// Build one finding's block against one environment's catalog and
    /// profile evidence. `None` when the kind carries no prepared shape or
    /// the coordinates do not resolve (a lever is never backed by a block
    /// the artifact cannot stand behind).
    let private blockFor
        (catalog: Catalog)
        (profile: Profile option)
        (seed: Map<SsKey, StaticRow list>)
        (finding: Estate.Finding)
        : RemediationEmitter.EstateBlock option =
        let keyText = FindingKey.text finding.Key
        let subject = keyText.Substring(keyText.IndexOf ':' + 1)
        let blockOf (locate: string) (repairs: string list) : RemediationEmitter.EstateBlock option =
            Some
                { Title = FindingKey.readableLabel finding.Key
                  BlockId = keyText
                  Statement = finding.Statement
                  Locate = locate
                  Repairs = repairs }
        let resolve () : (Kind * Attribute) option =
            coordinateOf subject
            |> Option.bind (fun (entity, col) ->
                findKind catalog entity
                |> Option.bind (fun k -> findAttr k col |> Option.map (fun a -> k, a)))
        match finding.Kind with
        | EstateFindingKind.DataNotNull ->
            resolve ()
            |> Option.bind (fun (k, a) ->
                let t = tableOf k
                let c = columnOf a
                blockOf
                    (sprintf "SELECT * FROM %s WHERE %s IS NULL;" t c)
                    [ sprintf "UPDATE %s SET %s = <DEFAULT> WHERE %s IS NULL; -- operator: confirm the default value" t c c
                      sprintf "DELETE FROM %s WHERE %s IS NULL; -- operator: confirm row removal" t c ])
        | EstateFindingKind.DataUnique ->
            resolve ()
            |> Option.bind (fun (k, a) ->
                let t = tableOf k
                let c = columnOf a
                let dedupe =
                    match Kind.primaryKey k with
                    | [ pk ] ->
                        let pkCol = columnOf pk
                        [ sprintf "DELETE [d] FROM %s [d] JOIN %s [keep] ON [d].%s = [keep].%s AND [d].%s > [keep].%s; -- keeps the lowest key per colliding value"
                              t t c c pkCol pkCol ]
                    | _ -> []
                blockOf
                    (sprintf "SELECT %s, COUNT(*) AS [n] FROM %s GROUP BY %s HAVING COUNT(*) > 1;" c t c)
                    dedupe)
        | EstateFindingKind.DataOrphans ->
            resolve ()
            |> Option.bind (fun (k, a) ->
                k.References
                |> List.tryFind (fun r -> r.SourceAttribute = a.SsKey)
                |> Option.bind (fun r -> Catalog.tryFindKind r.TargetKind catalog)
                |> Option.bind (fun targetKind ->
                    match Kind.primaryKey targetKind with
                    | [ targetPk ] ->
                        let t = tableOf k
                        let c = columnOf a
                        let targetTable = tableOf targetKind
                        let targetCol = columnOf targetPk
                        let zeros = zeroSentinelCount profile a.SsKey
                        let sentinelRepair =
                            if zeros > 0L then
                                [ sprintf "UPDATE %s SET %s = NULL WHERE %s = 0; -- clears the unset references (value 0)" t c c ]
                            else []
                        blockOf
                            (sprintf "SELECT * FROM %s WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s);" t c c targetCol targetTable)
                            (sentinelRepair
                             @ [ sprintf "DELETE FROM %s WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s); -- operator: confirm row removal" t c c targetCol targetTable ])
                    | _ -> None))
        | EstateFindingKind.DataCollationCollision ->
            resolve ()
            |> Option.bind (fun (k, a) ->
                let t = tableOf k
                let c = columnOf a
                blockOf
                    (sprintf "SELECT LOWER(%s) AS [folded], COUNT(*) AS [n] FROM %s GROUP BY LOWER(%s) HAVING COUNT(*) > 1;" c t c)
                    [ sprintf "UPDATE %s SET %s = <NEW VALUE> WHERE %s = <COLLIDING VALUE>; -- operator: choose the survivor per folded group" t c c ])
        | EstateFindingKind.SchemaTrust ->
            resolve ()
            |> Option.bind (fun (k, _) ->
                let t = tableOf k
                blockOf
                    (sprintf "SELECT [name], [is_not_trusted] FROM sys.foreign_keys WHERE [parent_object_id] = OBJECT_ID(N'%s');" t)
                    [ sprintf "ALTER TABLE %s WITH CHECK CHECK CONSTRAINT ALL; -- re-trusts every constraint on the kind; scans the table" t ])
        | EstateFindingKind.DataStaticContent ->
            // D10 (wave A4β) — the subject is the static KIND (not
            // Entity.Column), so resolve the kind by name and align its
            // content to the model's declared seed, MATCHED BY BUSINESS KEY,
            // never rewriting the surrogate (that is D11's ruling). The
            // located SELECT shows the environment's current reference data;
            // the alignment MERGE — a REAL executable batch, rendered through
            // the shared MERGE engine and commented line-by-line by the emitter
            // — is the prepared repair: the surrogate PK stays out of the ON /
            // INSERT (the sink mints its own key), and there is no
            // delete-by-source (removing rows the seed omits is a separate
            // ruling, because they may be referenced).
            findKind catalog subject
            |> Option.bind (fun k ->
                match Estate.staticBusinessKey k, Estate.staticPk k, Map.tryFind k.SsKey seed with
                | Some bkAttr, Some pkAttr, Some seedRows when not (List.isEmpty seedRows) ->
                    let t = tableOf k
                    let bkCol = columnOf bkAttr
                    let merge =
                        MergeRender.renderAlignmentMerge
                            "emit.estateAlignment" bkAttr.Name pkAttr.Name k seedRows
                    if merge = "" then None
                    else
                        blockOf
                            (sprintf "SELECT * FROM %s ORDER BY %s;" t bkCol)
                            [ sprintf "align %s to the model's declared static seed, matched by %s (the business key); the surrogate primary key is never rewritten — the sink mints its own." t bkCol
                              merge
                              sprintf "rows present in %s but absent from the seed are visible in the SELECT above; removing them is a separate ruling — confirm no inbound references first." t ]
                | _ -> None)
        | EstateFindingKind.PostureRetirable ->
            // The retirement repair (wave A6): the reopen probe reads zero
            // in every evidenced environment. An FK-anchored subject earns
            // the re-trust path; a plain column earns the re-tighten path
            // (the `<DECLARED TYPE>` placeholder rides the house idiom —
            // the operator restates the type; the overlay entry's removal
            // re-tightens on the next publish either way).
            resolve ()
            |> Option.bind (fun (k, a) ->
                let t = tableOf k
                let c = columnOf a
                let anchorsReference =
                    k.References |> List.exists (fun r -> r.SourceAttribute = a.SsKey)
                if anchorsReference then
                    blockOf
                        (sprintf "SELECT [name], [is_not_trusted] FROM sys.foreign_keys WHERE [parent_object_id] = OBJECT_ID(N'%s');" t)
                        [ sprintf "ALTER TABLE %s WITH CHECK CHECK CONSTRAINT ALL; -- the reopen probe reads zero; re-trusts the relationship (remove overlay entry %s first)" t keyText ]
                else
                    blockOf
                        (sprintf "SELECT COUNT_BIG(*) AS [remaining] FROM %s WHERE %s IS NULL;" t c)
                        [ sprintf "ALTER TABLE %s ALTER COLUMN %s <DECLARED TYPE> NOT NULL; -- the reopen probe reads zero; operator: restate the declared type (remove overlay entry %s first)" t c keyText ])
        | _ -> None

    /// One environment's blocks: every REPAIR-lane finding naming the
    /// environment, resolved against ITS catalog (physical realizations
    /// retained through the logical normalization). `seed` carries the model's
    /// declared static rows per kind (`StaticContent.Seed`) so the D10 block
    /// renders a real alignment MERGE; `Map.empty` (offline / no static probe)
    /// simply mints no D10 block.
    let blocksFor
        (env: string)
        (logicalEnvCatalog: Catalog)
        (profile: Profile option)
        (seed: Map<SsKey, StaticRow list>)
        (report: Estate.EstateReport)
        : RemediationEmitter.EstateBlock list =
        report.Findings
        |> List.filter (fun f ->
            f.Lane = EstateLane.Repair && f.Envs |> List.exists (fun (e, _) -> e = env))
        |> List.choose (blockFor logicalEnvCatalog profile seed)

    /// The artifact file name — one convention, minted here and read by the
    /// lever copy and the board index alike.
    let fileNameFor (env: string) : string =
        sprintf "environments.remediation.%s.sql" env

    /// The provenance header (RT-12 — the wrong-environment mistake is
    /// structurally detectable): environment label, server + database read
    /// from the resolved connection string's TYPED builder fields (never an
    /// opened connection; never the raw string — secrets stay out), and the
    /// generation instant.
    let header (env: string) (resolvedConn: string) (generatedUtc: DateTimeOffset) : string list =
        let server, database =
            try
                let builder = Microsoft.Data.SqlClient.SqlConnectionStringBuilder(resolvedConn)
                (if builder.DataSource = "" then "unknown" else builder.DataSource),
                (if builder.InitialCatalog = "" then "unknown" else builder.InitialCatalog)
            with :? ArgumentException | :? FormatException -> "unknown", "unknown"
        [ sprintf "-- projection:environments-remediation env=%s server=%s database=%s generated=%s"
              env server database (generatedUtc.ToString "o") ]
