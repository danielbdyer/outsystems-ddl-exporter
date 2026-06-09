namespace Projection.Pipeline

// LINT-ALLOW-FILE: preflight diagnostic prose + function-local accumulators at the boundary;
//   operator-facing preflight messages compose typed segments, the check output
//   is immutable. Terminal operator-facing text is the allowed exception.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// The pre-flight gate suite — the fail-loud checks a `migrate` / `transfer`
/// `--execute` must pass BEFORE any mutation. Each gate names a refusal rather
/// than corrupting the target half-way.
///
/// - **6.B.1 (T-V orthogonality) — Decision↔Data.** A tightening that makes a
///   column NOT NULL on NULL-bearing data would crash the two-phase load
///   mid-write. `tighteningPreflight` refuses (`migrate.dataViolatesTightening`)
///   given the overlay + a source null-count probe.
/// - **A1 (T-VI spanning) — Connection.** Both endpoints live + credentialed
///   before mutation. `connectionPreflight` refuses
///   (`migrate.connectionUnavailable`) rather than failing mid-load.
/// - **A2 (T-VI spanning) — Permission.** The sink grant covers the planned
///   writes. `permissionViolations` is the pure gate; `captureGrantEvidence`
///   the (survey-gated) `sys.fn_my_permissions` probe.
/// - **A3 (T-VI spanning) — Transactionality.** A mid-load failure must leave
///   the target unchanged or resumable. Scaffold documented below; the live
///   write-wrapper granularity is survey-gated (OPEN-2 / P6 / P11).
///
/// The gates are composable siblings (`Preflight.all` runs them in order,
/// short-circuiting on the first refusal) so a caller assembles exactly the
/// gates it has inputs for. The data plane already refuses loudly (transfer
/// exit-9, the execute-gate); this closes the spanning axes (T-VI) — the only
/// place a target could still be silently corrupted (write-denied sink → zero
/// rows; mid-load crash → half-populated; dead endpoint → mutation begins
/// blind).
[<RequireQualifiedAccess>]
module Preflight =

    /// One column the tightening would break: it is tightened to NOT NULL
    /// (`DecisionOverlay.EnforceNotNull`) but the source data carries
    /// `NullCount` NULL rows.
    type TighteningViolation =
        {
            KindKey      : SsKey
            AttributeKey : SsKey
            NullCount    : int64
        }

    /// Pure: each `EnforceNotNull` column whose source data carries NULLs (the
    /// Sink would reject the NULL into the tightened NOT-NULL column at load
    /// time). Reads the LiveProfiler null-count evidence; no I/O. Deterministic
    /// — sorted by attribute identity (T1).
    let dataViolatesTightening
        (cache: EvidenceCache)
        (overlay: DecisionOverlay)
        : TighteningViolation list =
        cache.Kinds
        |> Map.toList
        |> List.collect (fun (kindKey, ck) ->
            ck.NullCounts
            |> Map.toList
            |> List.choose (fun (attrKey, nullCount) ->
                if nullCount > 0L && Set.contains attrKey overlay.EnforceNotNull then
                    Some { KindKey = kindKey; AttributeKey = attrKey; NullCount = nullCount }
                else None))
        |> List.sortBy (fun v -> SsKey.rootOriginal v.AttributeKey)

    /// Render the violations as the operator-facing refusal message.
    let private describe (violations: TighteningViolation list) : string =
        match violations with
        | [] -> "no tightening violations"
        | first :: _ ->
            sprintf
                "%d column(s) carry NULLs but a Decision tightens them to NOT NULL; the load would fail mid-write. First: attribute %s in kind %s has %d NULL row(s). Remediate the data or relax the tightening before executing."
                (List.length violations)
                (SsKey.rootOriginal first.AttributeKey)
                (SsKey.rootOriginal first.KindKey)
                first.NullCount

    /// Pure: the attribute `SsKey`s whose Nullability facet *narrows* (source
    /// nullable → target NOT NULL) between two catalogs. This is the tightened
    /// set the migrate verbs gate on: a `Changed` survivor whose nullability
    /// went `true → false`, NOT a newly-`Added` NOT-NULL column (an Added column
    /// has no source rows to violate the tightening). Match is by attribute
    /// `SsKey` across the two catalogs (A1-stable identity), so a renamed kind
    /// or column still pairs. Deterministic — returns a `Set`.
    let tightenedToNotNull (source: Catalog) (target: Catalog) : Set<SsKey> =
        let srcAttrs =
            Catalog.allKinds source
            |> List.collect (fun k -> k.Attributes)
            |> List.map (fun a -> a.SsKey, a.Column.IsNullable)
            |> Map.ofList
        Catalog.allKinds target
        |> List.collect (fun k -> k.Attributes)
        |> List.choose (fun t ->
            match Map.tryFind t.SsKey srcAttrs with
            | Some srcNullable when srcNullable && not t.Column.IsNullable -> Some t.SsKey
            | _ -> None)
        |> Set.ofList

    /// The tightening overlay derived from a migration's A→B displacement: the
    /// attributes that narrow to NOT NULL become `EnforceNotNull`. An *empty*
    /// overlay (no narrowing) is the signal the verb uses to skip the
    /// self-probing pre-flight entirely (a non-tightening migration must not pay
    /// the LiveProfiler null-count survey cost).
    let tighteningOverlay (source: Catalog) (target: Catalog) : DecisionOverlay =
        { DecisionOverlay.empty with EnforceNotNull = tightenedToNotNull source target }

    /// Run the pre-flight against a live source: capture the per-attribute
    /// null-count evidence (read-only — safe before any write) and refuse with
    /// `migrate.dataViolatesTightening` if the overlay tightens any NULL-bearing
    /// column. A clean source returns `Ok ()`; the named refusal replaces the
    /// silent mid-load crash.
    let tighteningPreflight
        (cnn: SqlConnection)
        (catalog: Catalog)
        (overlay: DecisionOverlay)
        : Task<Result<unit>> =
        task {
            // `LiveProfiler` skips `Modality.Static` kinds — but `ReadSide`
            // marks every row-carrying reconstructed table Static, which would
            // skip exactly the kinds we need to probe. The pre-flight cares
            // about the LIVE source data, not the modeling classification, so
            // clear Modality before capture (it does not affect the SQL probe).
            let profileCatalog = catalog |> Catalog.mapKinds (fun k -> { k with Modality = [] })
            match! LiveProfiler.captureEvidenceCache cnn profileCatalog with
            | Error es -> return Result.failure es
            | Ok cache ->
                match dataViolatesTightening cache overlay with
                | [] -> return Ok ()
                | violations ->
                    return
                        Result.failureOf
                            (ValidationError.create "migrate.dataViolatesTightening" (describe violations))
        }

    // -- A1 — connection pre-flight (T-VI spanning) --------------------------

    /// Per-endpoint liveness evidence, captured from the live connection. Pure
    /// data — the decision (`connectionViolations`) is DB-free and testable.
    type ConnectionEvidence =
        {
            Role        : string
            IsReachable : bool
            Login       : string option
        }

    /// One endpoint that fails the connection gate.
    type ConnectionViolation =
        {
            Role   : string
            Reason : string
        }

    /// Pure: an endpoint violates the gate if it is unreachable OR carries no
    /// authenticated login (`SUSER_SNAME()` returned NULL — e.g. a connection
    /// that opened but under no recognised principal). Deterministic — input
    /// order preserved (T1).
    let connectionViolations (evidence: ConnectionEvidence list) : ConnectionViolation list =
        evidence
        |> List.choose (fun e ->
            if not e.IsReachable then
                Some { Role = e.Role; Reason = "endpoint is not reachable" }
            elif Option.isNone e.Login then
                Some { Role = e.Role; Reason = "no authenticated login (SUSER_SNAME() returned NULL)" }
            else None)

    let private describeConnection (violations: ConnectionViolation list) : string =
        violations
        |> List.map (fun v -> sprintf "%s: %s" v.Role v.Reason)
        |> String.concat "; "
        |> sprintf "connection pre-flight failed before any write — %s. Verify both endpoints are live and credentialed before executing."

    /// Probe one endpoint: ensure it is open and capture the authenticated
    /// login. A throwing open / probe yields an unreachable evidence (the gate
    /// refuses) rather than propagating the exception — the refusal is the
    /// contract, fail-loud but structured.
    let private probeConnection (role: string) (cnn: SqlConnection) : Task<ConnectionEvidence> =
        task {
            try
                if cnn.State <> System.Data.ConnectionState.Open then do! cnn.OpenAsync()
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- "SELECT SUSER_SNAME()"
                let! result = cmd.ExecuteScalarAsync()
                let login =
                    match result with
                    | null -> None
                    | :? string as s when not (System.String.IsNullOrWhiteSpace s) -> Some s
                    | _ -> None
                return { Role = role; IsReachable = true; Login = login }
            with _ ->
                return { Role = role; IsReachable = false; Login = None }
        }

    /// A1 — both endpoints live + credentialed before any mutation. Refuses
    /// `migrate.connectionUnavailable` rather than letting a dead/misconfigured
    /// endpoint surface as a mid-load failure. Read-only (a `SELECT`), safe to
    /// run before the write.
    let connectionPreflight (source: SqlConnection) (sink: SqlConnection) : Task<Result<unit>> =
        task {
            let! srcEv = probeConnection "source" source
            let! sinkEv = probeConnection "sink" sink
            match connectionViolations [ srcEv; sinkEv ] with
            | [] -> return Ok ()
            | violations ->
                return
                    Result.failureOf
                        (ValidationError.create "migrate.connectionUnavailable" (describeConnection violations))
        }

    // -- A2 — permission pre-flight (T-VI spanning) --------------------------

    /// The kind of write a planned operation needs at the sink.
    type WriteAction =
        | Insert
        | Delete
        | Alter
        | CreateTable

    /// Every write action — the closed vocabulary, enumerated so a derived
    /// capability catalog (the capability survey) stays total over it by
    /// construction rather than by a hand-maintained parallel list.
    let allWriteActions : WriteAction list = [ Insert; Delete; Alter; CreateTable ]

    /// One object + action the plan will perform at the sink. Built from the
    /// migrate schema differential (`ALTER`/`ADD` → Alter/CreateTable) and the
    /// transfer's target kinds (`INSERT`/`DELETE`).
    type PlannedWrite =
        {
            Schema : string
            Table  : string
            Action : WriteAction
        }

    /// One planned write the sink grant does not cover.
    type PermissionViolation =
        {
            Object : string
            Action : WriteAction
        }

    /// The permissions the login holds, captured from `sys.fn_my_permissions`
    /// as a set of `(object-key, permission-name)` pairs (object-key =
    /// `"schema.table"`; database-wide grants carry the empty object-key "").
    ///
    /// **Survey-gated (OPEN-2 / P1).** The exact permission-name vocabulary the
    /// managed UAT login exposes — and whether grants land at object vs schema
    /// vs database scope — is confirmed by the capability survey. The pure
    /// decision (`permissionViolations`) and the probe (`captureGrantEvidence`)
    /// are the buildable scaffold; the survey refines the name mapping.
    type GrantEvidence =
        {
            Granted : Set<string * string>
        }

    /// The `sys.fn_my_permissions` permission name a write action holds at. Public
    /// so the capability survey can name the source-read (SELECT) permission in the
    /// same vocabulary the sink-write permissions are probed under.
    let permissionName (a: WriteAction) : string =
        match a with
        | Insert      -> "INSERT"
        | Delete      -> "DELETE"
        | Alter       -> "ALTER"
        | CreateTable -> "CREATE TABLE"

    let private objectKey (w: PlannedWrite) : string =
        sprintf "%s.%s" w.Schema w.Table

    /// Pure: each planned write the grant does not cover at either the object
    /// scope or the database scope (object-key ""). Deterministic — sorted by
    /// object then action. DB-free and testable.
    let permissionViolations (planned: PlannedWrite list) (grant: GrantEvidence) : PermissionViolation list =
        let covered (w: PlannedWrite) =
            let perm = permissionName w.Action
            Set.contains (objectKey w, perm) grant.Granted
            || Set.contains ("", perm) grant.Granted
        planned
        |> List.filter (fun w -> not (covered w))
        |> List.map (fun w -> { Object = objectKey w; Action = w.Action })
        |> List.distinct
        |> List.sortBy (fun v -> v.Object, permissionName v.Action)

    /// Does the captured grant hold a named permission at **database scope** —
    /// the place-wide grant (object-key ""). The primitive the capability survey
    /// reconciles every required capability (write actions AND the source-read
    /// SELECT) against. Pure + DB-free.
    let coversPermissionAtDatabaseScope (permission: string) (grant: GrantEvidence) : bool =
        Set.contains ("", permission) grant.Granted

    /// Does the captured grant cover this write action at **database scope** —
    /// the place-wide grant (object-key "") the capability survey checks an
    /// environment's declared `grant` facet against. Pure + DB-free.
    let coversAtDatabaseScope (action: WriteAction) (grant: GrantEvidence) : bool =
        coversPermissionAtDatabaseScope (permissionName action) grant

    let private describePermission (violations: PermissionViolation list) : string =
        violations
        |> List.map (fun v -> sprintf "%s on %s" (permissionName v.Action) v.Object)
        |> String.concat "; "
        |> sprintf "permission pre-flight failed before any write — the sink grant does not cover: %s. A write-denied sink would otherwise transfer zero rows and exit clean."

    /// A2 — refuse `migrate.insufficientGrant` when the captured grant does not
    /// cover the planned writes, BEFORE any write. Takes pre-captured evidence
    /// (separating the pure gate from the survey-gated `sys.fn_my_permissions`
    /// probe — `captureGrantEvidence`).
    let permissionPreflight (grant: GrantEvidence) (planned: PlannedWrite list) : Result<unit> =
        match permissionViolations planned grant with
        | [] -> Ok ()
        | violations ->
            Result.failureOf
                (ValidationError.create "migrate.insufficientGrant" (describePermission violations))

    /// Capture the sink's effective permissions from `sys.fn_my_permissions`.
    ///
    /// **Survey-gated (OPEN-2 / P1).** This probes the database-scope grants
    /// today (`sys.fn_my_permissions(NULL, 'DATABASE')`), keyed under the empty
    /// object-key. Object-scope refinement (per-table grants) lands once the
    /// survey (P1) confirms the managed login's grant shape; the pure gate
    /// already consumes object-keyed evidence, so only this capture changes.
    /// Read every database-scope permission name as a `("", name)` pair (module
    /// level so the recursive task state machine is statically compilable —
    /// a nested `let rec` inside a `task { }` is not, FS3511).
    let rec private readGrantRows (reader: SqlDataReader) (acc: (string * string) list) : Task<(string * string) list> =
        task {
            let! hasRow = reader.ReadAsync()
            if hasRow then return! readGrantRows reader (("", reader.GetString(0)) :: acc)
            else return acc
        }

    let captureGrantEvidence (sink: SqlConnection) : Task<Result<GrantEvidence>> =
        task {
            try
                if sink.State <> System.Data.ConnectionState.Open then do! sink.OpenAsync()
                use cmd = sink.CreateCommand()
                cmd.CommandText <- "SELECT permission_name FROM sys.fn_my_permissions(NULL, 'DATABASE')"
                use! reader = cmd.ExecuteReaderAsync()
                let! pairs = readGrantRows reader []
                return Ok { Granted = Set.ofList pairs }
            with ex ->
                return Result.failureOf (ValidationError.create "migrate.grantProbeFailed" ex.Message)
        }

    // -- A3 — transactional / resumable transfer (T-VI spanning) -------------
    //
    // SCAFFOLD (survey-gated, OPEN-2 / P6 / P11). A mid-transfer failure must
    // leave the target unchanged (atomic) or resumable, never half-populated.
    // The buildable-now default is a RESUMABLE idempotent-upsert keyed on the
    // surrogate remap (survives a managed login that forbids long transactions
    // AND a CDC-tracked sink hostile to one giant transaction); the full-
    // transaction alternative is chosen when the survey (P11 `BEGIN TRAN`,
    // P6 `DELETE`-vs-`TRUNCATE`) confirms it is permitted. The wrapper lands in
    // `TransferRun.writePlan` (the Phase-1 + Phase-2 loop) with a Docker
    // witness; it is deliberately NOT half-implemented here because its
    // granularity is the survey's to resolve and its correctness needs a live
    // container to witness. See `DEBRIEF_2026_06_02_…` cluster A (A3).

    // -- The composition -----------------------------------------------------

    /// Run a list of pre-flight gates in order, short-circuiting on the first
    /// refusal (fail-loud). The mandatory gate on every `--execute`: a caller
    /// assembles exactly the gates it has inputs for (connection + permission +
    /// tightening) and threads the result before any mutation.
    let rec all (gates: Task<Result<unit>> list) : Task<Result<unit>> =
        task {
            match gates with
            | [] -> return Ok ()
            | gate :: rest ->
                match! gate with
                | Ok () -> return! all rest
                | Error e -> return Error e
        }

    // -- G0: the reporting surface (`code → (exit, label)`) -------------------
    //
    // G0's criterion (THE_USE_CASE_ONTOLOGY.obligations.md §6, AC-G0): a SINGLE
    // mandatory `Preflight.all` composes the full gate suite so no caller can
    // skip a gate. §8 names the HAZARD: the composition is a `code→(exit,label)`
    // reporting refactor that RISKS regressing the distinct per-gate exit codes
    // the verbs produce today (each gate's refusal currently maps to its own CLI
    // exit). This surface is the discriminator against that regression: it pins
    // the `ValidationError.Code → (exit, label)` mapping to the EXISTING per-verb
    // exits so the composition collapses the ENTRY POINT (one mandatory `all`)
    // WITHOUT collapsing the exit-code SEMANTICS.
    //
    // The mapping is grounded in the verbs' reality (the parent wires the verbs
    // to consume it):
    //   - connection-unreachable    → exit 6  (transfer Program.fs G1)
    //   - permission / grant-probe   → exit 7  (migrate `migratePreflights`;
    //                                            transfer Program.fs G2)
    //   - reconcile / user-map       → exit 2  (transfer Program.fs)
    //   - dropped/unmapped identities,
    //     data-violates-tightening   → exit 9  (transfer DroppedReferencesExit;
    //                                            migrate RefusedByTightening)
    //   - schema-read failed         → exit 6  (migrate SchemaReadFailed)
    //   - any other refusal          → exit 3  (transfer generic; the named
    //                                            default — UnclassifiedRefusal)

    /// The operator-facing classification of a gate refusal — a CLOSED DU so the
    /// mapping is total over the gate vocabulary and a fresh gate code MUST be
    /// placed (or it falls to the named `UnclassifiedRefusal` default, never a
    /// silent miss). The label is the human-facing name of WHICH spanning/data
    /// axis refused; the paired exit is the CLI's distinct code for that axis.
    type GateLabel =
        | ConnectionUnavailable
        | InsufficientGrant
        | ReconciliationMismatch
        | UnmappedIdentities
        | DataViolatesTightening
        | CdcTrackedSink
        | SchemaReadFailed
        | UndeclaredDestructiveChange
        /// The named default for a code outside the known gate vocabulary — a
        /// generic refusal. NOT a silent pass: it still carries the generic
        /// non-zero refusal exit (3), so an unmapped gate fails loud.
        | UnclassifiedRefusal

    /// Render a `GateLabel` as the operator-facing axis name.
    let labelText (label: GateLabel) : string =
        match label with
        | ConnectionUnavailable       -> "connection unavailable"
        | InsufficientGrant           -> "insufficient grant"
        | ReconciliationMismatch      -> "reconciliation mismatch"
        | UnmappedIdentities          -> "unmapped identities"
        | DataViolatesTightening      -> "data violates tightening"
        | CdcTrackedSink              -> "CDC-tracked sink"
        | SchemaReadFailed            -> "schema read failed"
        | UndeclaredDestructiveChange -> "undeclared destructive change"
        | UnclassifiedRefusal         -> "unclassified refusal"

    /// The closed `code → (exit, label)` mapping. TOTAL over the gate code
    /// vocabulary: every known refusal maps to its DISTINCT per-verb exit code +
    /// operator label; any code outside the vocabulary falls to the named
    /// `(3, UnclassifiedRefusal)` default (a generic non-zero refusal — fail
    /// loud, never a silent exit 0). Matches on the `category.subject.problem`
    /// stem so both the `migrate.*` and `transfer.*` namespaces of the same axis
    /// classify identically (the transfer verb re-codes the migrate-named
    /// refusals under `transfer.*`; they are the SAME axis, SAME exit).
    let classify (code: string) : int * GateLabel =
        // Connection — exit 6 on transfer; the migrate verb's connection
        // pre-flight surfaces under exit 7 (permission/credential class), so the
        // connection axis is reported on its own (6) and the grant axis on (7).
        if code.StartsWith "transfer.connection" || code = "migrate.connectionUnavailable" then
            6, ConnectionUnavailable
        elif code = "transfer.insufficientGrant" || code = "transfer.grantProbeFailed"
             || code = "migrate.insufficientGrant" || code = "migrate.grantProbeFailed" then
            7, InsufficientGrant
        elif code.StartsWith "transfer.reconcile." || code.StartsWith "transfer.userMap." then
            2, ReconciliationMismatch
        elif code = "transfer.unmappedIdentities" then
            9, UnmappedIdentities
        elif code = "migrate.dataViolatesTightening" then
            9, DataViolatesTightening
        elif code = "transfer.cdcTrackedSink" then
            9, CdcTrackedSink
        elif code = "migrate.schemaReadFailed" then
            6, SchemaReadFailed
        elif code.StartsWith "migrate.undeclaredDestructive" then
            9, UndeclaredDestructiveChange
        else
            3, UnclassifiedRefusal

    /// A gate refusal as the composition reports it: the structured first-failure
    /// `ValidationError` together with its `(exit, label)` classification. The
    /// composition surfaces THIS (not a bare `Result<unit>`) so the caller has the
    /// distinct exit + operator label without re-deriving them — the single seam
    /// the verbs route through.
    type GateRefusal =
        {
            Error    : ValidationError
            ExitCode : int
            Label    : GateLabel
        }

    /// Classify the FIRST error of a refusal (the gate's primary code) into a
    /// `GateRefusal`. A refusal always carries at least one error; an empty list
    /// (which `Result.failure` forbids) classifies as the named default rather
    /// than throwing — total by construction.
    let refusalOf (errors: ValidationError list) : GateRefusal =
        let primary =
            match errors with
            | e :: _ -> e
            | []     -> ValidationError.create "preflight.emptyRefusal" "a gate refused with no error"
        let exit, label = classify primary.Code
        { Error = primary; ExitCode = exit; Label = label }

    /// G0's mandatory entry point WITH reporting: run the gate suite in order,
    /// short-circuiting on the FIRST refusal (identical composition semantics to
    /// `all`), and report that refusal as a `GateRefusal` carrying its distinct
    /// `(exit, label)`. This is the sibling the verbs route through so the
    /// composition collapses to ONE entry point while PRESERVING the per-gate
    /// exit codes. `all` is preserved unchanged for callers that only need the
    /// `Result<unit>`.
    let rec allReporting (gates: Task<Result<unit>> list) : Task<Result<unit, GateRefusal>> =
        task {
            match gates with
            | [] -> return Ok ()
            | gate :: rest ->
                match! gate with
                | Ok () -> return! allReporting rest
                | Error errors -> return Error (refusalOf errors)
        }
