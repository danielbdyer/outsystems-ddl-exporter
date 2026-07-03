namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// The resumable/wipe-and-load write INFRASTRUCTURE for the transfer leg:
/// the FK-ordered wipe (D10 — the operator-selected full refresh clears targets
/// child-first) and the durable completion marker (G10 — a plan-signature row
/// that makes a re-run of a completed transfer a no-op, replaying the prior
/// drop verdict). Lifted out of the `module Transfer` god-module: both serve
/// `writePlanResumable` and depend only on Catalog / DataLoadPlan / the sink
/// connection. `module Transfer` consumes them as `TransferResume.*`.
[<RequireQualifiedAccess>]
module TransferResume =

    /// FK-ordered wipe: DELETE every target table CHILD-FIRST (reverse
    /// topological order) so a foreign-key constraint never blocks the clear.
    /// (`TRUNCATE` is refused by SQL Server on an FK-referenced table regardless
    /// of order, so the child-first DELETE is the FK-safe realization of the
    /// wipe — same end state, the `2·|rows|` CDC cost `EmissionMode` documents.)
    /// The kinds the wipe will DELETE, child-first — the pure core of
    /// `wipeFkOrdered`. The wipe never touches two classes of kind (PE-1 /
    /// P-REKEY — golden user-exclusion holds under *any* strategy, not just
    /// Incremental):
    /// (1) a **`ReconciledByRule`** kind — its sink rows are the sink's OWN
    /// (matched by business key); deleting them would destroy the sink's
    /// inventory (e.g. its users) and the zeroed plan would not re-insert them;
    /// (2) a kind outside `loadSet` (the declared golden subset) — untouched,
    /// not refreshed. `loadSet = None` wipes every non-reconciled loaded kind.
    let wipeTargets (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : SsKey list =
        let loaded =
            plan.Loads
            |> List.filter (fun l -> l.Disposition <> IdentityDisposition.ReconciledByRule)
            |> List.map (fun l -> l.Kind)
            |> Set.ofList
        let inScope =
            match loadSet with
            | Some ls -> Set.intersect loaded ls
            | None    -> loaded
        List.rev topo.Order |> List.filter (fun k -> Set.contains k inScope)

    let wipeFkOrdered (sink: SqlConnection) (catalog: Catalog) (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : Task<unit> =
        task {
            for k in wipeTargets plan topo loadSet do
                match Catalog.tryFindKind k catalog with
                | None      -> ()
                | Some kind ->
                    // PL-6 (S14): one GO-free DELETE — one pre-split segment.
                    do! Deploy.executeSegments sink
                            [ System.String.Concat("DELETE FROM ", Render.tableQualified kind.Physical, ";") ]  // LINT-ALLOW: terminal SQL-text boundary; table name is a validated TableId via Render.tableQualified
        }

    /// The durable phase-marker table — records which transfers completed, so a
    /// re-run of an already-finished transfer is a no-op (idempotent).
    ///
    /// L4 — G10 on the ledger contract (R3 / RI-3): this is the DEGENERATE
    /// single-quantum instance, retired as a separate ledger mechanism. One
    /// entry ("the whole run"), fingerprint = the plan signature
    /// (`planMarker`, recomputed from the live plan on every run — the
    /// grain's ResumeAdmit, with equality realized as the SQL set-membership
    /// `isMarked` answers), WriteAdmit positional at `markComplete` (after
    /// `writePlan`, the same control-flow witness as the journal's append).
    /// It exercises NOTHING of the contract's replay machinery, honestly: a
    /// single full-state quantum has no partial sums to rebuild — the sink's
    /// rows ARE the state, and the admitted re-run's no-op IS the resume.
    /// The streaming realization's chunk-grain journal (`CaptureJournal`) is
    /// the non-degenerate sibling; the two stay distinct REALIZATIONS of one
    /// contract, not two mechanisms.
    /// NM-53 — the marker now persists the prior run's DROP COUNT, not just the
    /// completion fact. A transfer that legitimately dropped FK-orphans on its
    /// first run (exit 9) and then re-runs hits the completion marker; without
    /// the persisted count the no-op return is `SkippedReferences = []` → exit 0,
    /// so a refresh wrapper re-running to confirm sees a misleading clean. The
    /// `DropCount` column lets the no-op path REPLAY the prior drop verdict.
    /// `ADD`-guarded so a marker table from a pre-NM-53 run gains the column.
    let progressTableSql : string =
        "IF OBJECT_ID('dbo.__projection_transfer_progress') IS NULL \
           CREATE TABLE dbo.__projection_transfer_progress \
             ( Marker NVARCHAR(450) NOT NULL PRIMARY KEY, \
               CompletedAt DATETIME2 NOT NULL CONSTRAINT DF___ptp_at DEFAULT SYSUTCDATETIME(), \
               DropCount INT NOT NULL CONSTRAINT DF___ptp_drops DEFAULT 0 ); \
         IF COL_LENGTH('dbo.__projection_transfer_progress', 'DropCount') IS NULL \
           ALTER TABLE dbo.__projection_transfer_progress \
             ADD DropCount INT NOT NULL CONSTRAINT DF___ptp_drops DEFAULT 0;"

    /// A deterministic signature of a plan — the sorted set of target tables it
    /// loads. Two re-runs of the same transfer share it; a different transfer
    /// (different tables) does not.
    let planMarker (catalog: Catalog) (plan: DataLoadPlan) : string =
        plan.Loads
        |> List.choose (fun l -> Catalog.tryFindKind l.Kind catalog)
        |> List.map (fun k -> Render.tableQualified k.Physical)
        |> List.sort
        |> String.concat "|"

    /// NM-53 — `None` when the marker is absent (not yet complete); `Some n` when
    /// the transfer completed, carrying the DROP COUNT it recorded. The no-op
    /// re-run replays that count so a prior exit-9 (FK-orphan drops) is not
    /// silently re-reported as a clean exit-0.
    let markedDropCount (sink: SqlConnection) (marker: string) : Task<int option> =
        task {
            use cmd = sink.CreateCommand()
            cmd.CommandText <- "SELECT DropCount FROM dbo.__projection_transfer_progress WHERE Marker = @m;"
            cmd.Parameters.AddWithValue("@m", marker) |> ignore
            let! v = cmd.ExecuteScalarAsync()
            return
                if isNull v || v = box System.DBNull.Value then None
                else Some (System.Convert.ToInt32 v)
        }

    let markComplete (sink: SqlConnection) (marker: string) (dropCount: int) : Task<unit> =
        task {
            use cmd = sink.CreateCommand()
            cmd.CommandText <- "INSERT INTO dbo.__projection_transfer_progress (Marker, DropCount) VALUES (@m, @d);"
            cmd.Parameters.AddWithValue("@m", marker) |> ignore
            cmd.Parameters.AddWithValue("@d", dropCount) |> ignore
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }
