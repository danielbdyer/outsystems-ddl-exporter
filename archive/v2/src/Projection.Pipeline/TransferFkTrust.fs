namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.SSDT

/// FK-TRUST management across a bulk load — snapshot the enabled+trusted FKs on
/// the loaded tables before the load, then re-validate (`WITH CHECK CHECK
/// CONSTRAINT`) after, descending the capability ladder to the named
/// `FkTrustNotRestoredOnBulkLoad` tolerance on a DML-only (ManagedDml) sink.
/// Lifted out of the `module Transfer` god-module: a self-contained concern
/// over Catalog / DataLoadPlan / the sink connection. `module Transfer` consumes
/// it as `TransferFkTrust.*`.
[<RequireQualifiedAccess>]
module TransferFkTrust =

    /// The plan's loaded (inserted-into) tables, lower-cased `schema.table`.
    let private loadedTableKeys (catalog: Catalog) (plan: DataLoadPlan) : Set<string> =
        plan.Loads
        |> List.choose (fun l -> Catalog.tryFindKind l.Kind catalog)
        |> List.map (fun k -> TableId.normalizedKey k.Physical)
        |> Set.ofList

    /// Pre-load snapshot — the `(schema, table, fk)` of every ENABLED + TRUSTED
    /// FK on a loaded table, read from `sys.foreign_keys` (the deployed truth).
    /// An FK the schema deployed UNTRUSTED (`is_not_trusted = 1` — a `NoCheckFk`
    /// decision) is excluded, so the restore never touches it.
    let trustedFksOnLoadedTables (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) : Task<(string * string * string) list> =
        task {
            let loaded = loadedTableKeys catalog plan
            if Set.isEmpty loaded then return [] else
            let trusted = System.Collections.Generic.List<string * string * string>()
            use cmd = sink.CreateCommand()
            cmd.CommandText <- // LINT-ALLOW: ADO.NET command-text assignment on the sys.foreign_keys snapshot query; SqlCommand.CommandText is a settable BCL property, not a mutation to avoid
                "SELECT s.name, t.name, fk.name \
                 FROM sys.foreign_keys fk \
                 JOIN sys.tables t ON fk.parent_object_id = t.object_id \
                 JOIN sys.schemas s ON t.schema_id = s.schema_id \
                 WHERE fk.is_not_trusted = 0 AND fk.is_disabled = 0;"
            use! reader = cmd.ExecuteReaderAsync()
            let mutable go = true // LINT-ALLOW: ADO reader-drain loop condition over SqlDataReader.ReadAsync; the reader is the mutable-by-nature ADO.NET boundary, not core state
            while go do
                let! has = reader.ReadAsync()
                if has then trusted.Add(reader.GetString 0, reader.GetString 1, reader.GetString 2)
                else go <- false // LINT-ALLOW: ADO reader-drain loop condition over SqlDataReader.ReadAsync; the reader is the mutable-by-nature ADO.NET boundary, not core state
            reader.Close()
            return
                trusted
                |> Seq.filter (fun (sch, tbl, _) ->
                    Set.contains (TableId.normalizedKeyOf sch tbl) loaded)
                |> List.ofSeq
        }

    /// The CAPABILITY recognizer for the FK re-trust — the ALTER cannot run
    /// because the sink login holds no ALTER on the object (a `ManagedDml` /
    /// `grant: data` cloud sink, granted only SELECT/INSERT/UPDATE/DELETE).
    /// 1088 / 4902 ("cannot find the object … because it does not exist or you do
    /// not have permissions" — the ALTER-TABLE permission/visibility form) and
    /// 229 (ALTER permission denied) DESCEND to the named
    /// `FkTrustNotRestoredOnBulkLoad` tolerance. Everything else — notably a
    /// constraint conflict (547) — PROPAGATES: a re-validation that fails on the
    /// DATA is the loud fidelity signal, never masked (mirrors
    /// `SurrogateCapture.isCapabilityRefusal`).
    let private isAlterCapabilityRefusal (ex: SqlException) : bool =
        CapabilityRefusal.isRefusal Capability.AlterConstraintTrust ex

    /// Restore the trust the bulk load stripped — re-validate each FK in the
    /// pre-load snapshot (`wasTrusted`) with `ALTER TABLE … WITH CHECK CHECK
    /// CONSTRAINT` (one child×parent-PK semi-join per FK). After a faithful
    /// transfer the data satisfies each FK so it succeeds; a CONSTRAINT failure
    /// is a LOUD signal the load was not faithful — a post-load integrity
    /// assertion, never silent corruption.
    ///
    /// A sink that cannot ALTER at all — the `ManagedDml` cloud archetype
    /// (`grant: data`; no ALTER anywhere in the write path) — DESCENDS the
    /// capability ladder: the re-trust is skipped, the FKs stay as the bulk load
    /// left them (untrusted), and the disposition is surfaced via the
    /// `retrust-skipped` stage — the named `ToleratedDivergence.FkTrustNotRestoredOnBulkLoad`,
    /// never silent. (Re-trust is a `FullRights` capability; on a DML-only login
    /// the ALTER is not available, exactly as the J5 archetype model records.)
    /// The gate is `WriteOptions.RetrustForeignKeys` (default on); the explicit
    /// opt-out is the same named tolerance.
    let restoreFkTrust (sink: SqlConnection) (wasTrusted: (string * string * string) list) : Task<unit> =
        task {
            if List.isEmpty wasTrusted then return () else
            let stmts =
                wasTrusted
                |> List.map (fun (sch, tbl, fk) ->
                    // LINT-ALLOW: terminal SQL-text boundary; identifiers are sys.* catalog-view
                    // names (deployed truth), each quoted via Render.quote.
                    System.String.Concat( // LINT-ALLOW: terminal SQL-text boundary; identifiers are sys.* catalog-view names quoted via Render.quote, BCL String.Concat is the irreducible primitive for this multi-segment ALTER statement
                        "ALTER TABLE ", Render.quote sch, ".", Render.quote tbl,
                        " WITH CHECK CHECK CONSTRAINT ", Render.quote fk, ";"))
            try
                do! Deploy.executeBatch sink (String.concat "\n" stmts) // LINT-ALLOW: terminal SQL-batch join at the ADO.NET execute boundary; each stmt is already terminal SQL text, String.concat is the irreducible primitive for newline-joining a batch
                LogSink.recordStageProgress "retrust" (List.length wasTrusted) (List.length wasTrusted) 0L
            with :? SqlException as ex when isAlterCapabilityRefusal ex ->
                // Capability descent — the sink login cannot ALTER (a ManagedDml /
                // data-grant cloud sink). No ALTER ⇒ no re-validation: descend to
                // the named FkTrustNotRestoredOnBulkLoad tolerance, surfaced via
                // the retrust-skipped stage, never silent.
                LogSink.recordStageProgress "retrust-skipped" 0 (List.length wasTrusted) 0L
        }
