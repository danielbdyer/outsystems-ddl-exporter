namespace Projection.Pipeline
// LINT-ALLOW-FILE-MUTATION: the deploy-feasibility gate's fixed-point
//   batch-apply driver at the ADO.NET boundary — the pending/applied/
//   progress/lastErrors loop state IS the dependency-blind fixed-point
//   algorithm (re-apply until no batch makes progress), and CommandText/
//   CommandTimeout assignment is the ADO idiom; the findings report the
//   caller consumes is immutable.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Targets.SSDT

/// The DEPLOY-FEASIBILITY gate — apply the EMITTED schema bundle to a
/// disposable database and report exactly what the server refused, per batch,
/// as named findings.
///
/// **Why it exists.** The engine reasons over the IR; the deploy target
/// enforces constraints the IR does not encode. One such gap already shipped
/// and was caught by hand: a retargeted FK to a column with no DECLARED unique
/// index clears every readiness check and then fails at deploy ("Could not
/// create constraint or index") — a green gate with a red deploy. That
/// instance became the `bridgeKeyDeclaredUnique` block; THIS module is the
/// systematic answer for the class: stop reasoning, apply the real artifact to
/// a real (disposable) server, and measure what it rejects. The gate runs over
/// the RENDERED bundle text — the exact bytes an operator would ship — not an
/// internal statement stream, so what is proven is what ships.
///
/// **Dependency-blind fixed-point application.** Bundle files carry no
/// dependency order here (parent tables may follow the children that reference
/// them). Rather than re-deriving topology, the gate applies every batch,
/// collects the failures, and RETRIES the failed set until a round makes no
/// progress: a batch that eventually applies was feasible all along (its
/// earlier failure was ordering); a batch still failing at the fixed point is
/// a GENUINE rejection, reported with the server's own error. Deterministic:
/// batches keep first-seen order across rounds.
[<RequireQualifiedAccess>]
module DeployFeasibility =

    /// One batch the server still refused at the fixed point.
    type Finding =
        { /// The bundle-relative file the batch came from.
          File         : string
          /// The batch's ordinal within its file (0-based).
          BatchOrdinal : int
          /// The server's error message, verbatim.
          Error        : string }

    /// The gate's verdict: how many batches applied, and every batch that did
    /// not — empty `Findings` is the green verdict.
    type Report =
        { BatchesApplied : int
          Findings       : Finding list }

    [<RequireQualifiedAccess>]
    module Report =
        let green (r: Report) : bool = List.isEmpty r.Findings

    /// One named batch: (file, ordinal-in-file, sql).
    type private Batch = { File: string; Ordinal: int; Sql: string }

    /// Split the bundle's files into named batches (the sqlcmd `GO` rule via
    /// the shared splitter; blank batches dropped).
    let private batchesOf (files: (string * string) list) : Batch list =
        files
        |> List.collect (fun (name, body) ->
            BatchSplitter.splitWithLoudFallback name body
            |> Array.toList
            |> List.mapi (fun i sql -> { File = name; Ordinal = i; Sql = sql })
            |> List.filter (fun b -> not (System.String.IsNullOrWhiteSpace b.Sql)))

    /// The fixed-point loop over an INJECTED executor (`exec sql` returns
    /// `None` on success, `Some error` on refusal) — the decision logic is
    /// testable without a server. Rounds repeat while at least one previously
    /// failing batch applies; the residue at the fixed point is the finding
    /// set, in first-seen order.
    let applyToFixedPoint
        (exec: string -> Task<string option>)
        (files: (string * string) list)
        : Task<Report> =
        task {
            let mutable pending = batchesOf files
            let mutable applied = 0
            let mutable progress = true
            let mutable lastErrors : (Batch * string) list = []
            while progress && not (List.isEmpty pending) do
                progress <- false
                let failures = System.Collections.Generic.List<Batch * string>()
                for b in pending do
                    let! outcome = exec b.Sql
                    match outcome with
                    | None ->
                        applied <- applied + 1
                        progress <- true
                    | Some error ->
                        failures.Add(b, error)
                pending <- failures |> Seq.map fst |> List.ofSeq
                lastErrors <- failures |> List.ofSeq
            return
                { BatchesApplied = applied
                  Findings =
                    lastErrors
                    |> List.map (fun (b, error) ->
                        { File = b.File; BatchOrdinal = b.Ordinal; Error = error }) }
        }

    /// The live executor: one batch, one `ExecuteNonQuery`, the server's error
    /// text on refusal. The CONNECTION defines the blast radius — the caller
    /// supplies a disposable database, and this module never creates or drops
    /// one (the sandbox is the test/verb layer's responsibility, not the
    /// gate's).
    let private execOn (cnn: SqlConnection) (sql: string) : Task<string option> =
        task {
            try
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- sql
                cmd.CommandTimeout <- 300
                let! _ = cmd.ExecuteNonQueryAsync()
                return None
            with ex ->
                return Some ex.Message
        }

    /// Apply the bundle to the supplied (disposable) database and report.
    let applyBundle (cnn: SqlConnection) (files: (string * string) list) : Task<Report> =
        task {
            use _ = Bench.scope "pipeline.deployFeasibility.applyBundle"
            return! applyToFixedPoint (execOn cnn) files
        }
