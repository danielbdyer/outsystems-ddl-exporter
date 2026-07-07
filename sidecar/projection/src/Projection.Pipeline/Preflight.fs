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

    /// Pure sibling of `dataViolatesTightening` over the SCOPED probe's
    /// shape (`LiveProfiler.nullCountsFor` — kind-key → attribute-key →
    /// exact null count, already restricted to the overlay's tightened
    /// attributes): each NULL-bearing tightened column, same assembly and
    /// same sort, so the two forms agree wherever the counts agree (PL-7,
    /// S10). No overlay filter here — the probe's scoping IS the filter.
    let violationsOfNullCounts
        (counts: Map<SsKey, Map<SsKey, int64>>)
        : TighteningViolation list =
        counts
        |> Map.toList
        |> List.collect (fun (kindKey, nullCounts) ->
            nullCounts
            |> Map.toList
            |> List.choose (fun (attrKey, nullCount) ->
                if nullCount > 0L then
                    Some { KindKey = kindKey; AttributeKey = attrKey; NullCount = nullCount }
                else None))
        |> List.sortBy (fun v -> SsKey.rootOriginal v.AttributeKey)

    /// A stable, human-readable identity for a tightening violation — the
    /// physical kind + column (`OSUSR_X_ORDER.Notes`). The key the relax-ALWAYS
    /// persistence writes to / matches against in `projection.json`, so a future
    /// headless run honors a previously-blessed relaxation without prompting.
    let violationKey (v: TighteningViolation) : string =
        sprintf "%s.%s" (SsKey.rootOriginal v.KindKey) (SsKey.rootOriginal v.AttributeKey)

    /// Render the violations as the operator-facing refusal message. Public so the
    /// interactive relax gate can title its prompt with the same §5 finding.
    let describe (violations: TighteningViolation list) : string =
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

    /// Relax a tightening: set the named attributes' columns back to NULLABLE in
    /// the target catalog, so the emitted schema FITS NULL-bearing data — the
    /// operator's "loosen the tightening to defer the source-data fix" (the
    /// steward override). Pure `Catalog → Catalog`; flips only `Column.IsNullable`
    /// on the named attributes (identity + count preserved, so the kind's
    /// smart-ctor invariants hold), so the migration's A→B diff no longer narrows
    /// them and `tighteningPreflight` passes. NOT a silent edit: the caller MUST
    /// record the relaxation as a tracked, named override (the stewardship
    /// principle — every departure from the team's model is a tracked exception).
    let relaxTightening (keys: Set<SsKey>) (target: Catalog) : Catalog =
        if Set.isEmpty keys then target
        else
            target
            |> Catalog.mapKinds (fun k ->
                { k with
                    Attributes =
                        k.Attributes
                        |> List.map (fun a ->
                            if Set.contains a.SsKey keys then
                                { a with Column = { a.Column with IsNullable = true } }
                            else a) })

    /// Like `tighteningPreflight` but RETURNS the violations (the attributes the
    /// caller may relax) rather than collapsing to a refusal string — the surface
    /// the interactive relax gate consumes. `Ok []` is a clean source.
    let tighteningViolations
        (cnn: SqlConnection)
        (catalog: Catalog)
        (overlay: DecisionOverlay)
        : Task<Result<TighteningViolation list>> =
        task {
            // `LiveProfiler` skips `Modality.Static` kinds — but `ReadSide`
            // marks every row-carrying reconstructed table Static, which would
            // skip exactly the kinds we need to probe. Strip the Static mark
            // ONLY: the previous `Modality = []` form also erased authored
            // marks (TenantScoped / SoftDeletable / SystemOwned / Temporal) —
            // the N2 over-erasure, closed 2026-06-11.
            let profileCatalog = catalog |> Catalog.stripStaticPopulations
            // PL-7 (S10): the gate consumes ONLY the tightened columns' null
            // counts — the scoped probe (one narrow aggregate per affected
            // kind) replaces the full `EvidenceCache` capture, whose per-kind
            // row streams were paid for a handful of counts.
            match! LiveProfiler.nullCountsFor cnn profileCatalog overlay.EnforceNotNull with
            | Error es -> return Result.failure es
            | Ok counts -> return Ok (violationsOfNullCounts counts)
        }

    /// Run the pre-flight against a live source: capture the per-attribute
    /// null-count evidence (read-only — safe before any write) and refuse with
    /// `migrate.dataViolatesTightening` if the overlay tightens any NULL-bearing
    /// column. A clean source returns `Ok ()`; the named refusal replaces the
    /// silent mid-load crash. (Delegates to `tighteningViolations`.)
    let tighteningPreflight
        (cnn: SqlConnection)
        (catalog: Catalog)
        (overlay: DecisionOverlay)
        : Task<Result<unit>> =
        task {
            match! tighteningViolations cnn catalog overlay with
            | Error es -> return Result.failure es
            | Ok [] -> return Ok ()
            | Ok violations ->
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
    /// `Update` joined 2026-07-07 (the go-board scoping program): the
    /// transfer's Phase-2 FK re-point and the MERGE capture lane genuinely
    /// UPDATE — SQL Server grants do not let UPDATE "ride" INSERT, so the
    /// prior INSERT-only gate passed sinks that then died mid-load.
    type WriteAction =
        | Insert
        | Update
        | Delete
        | Alter
        | CreateTable

    /// Every write action — the closed vocabulary, enumerated so a derived
    /// capability catalog (the capability survey) stays total over it by
    /// construction rather than by a hand-maintained parallel list.
    let allWriteActions : WriteAction list = [ Insert; Update; Delete; Alter; CreateTable ]

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
            /// The object keys (`"schema.table"`) the capture probed at
            /// OBJECT scope (2026-07-07). For a probed object,
            /// `fn_my_permissions('<obj>', 'OBJECT')` reports the
            /// EFFECTIVE permissions — database-scope grants inherited,
            /// object grants added, DENYs subtracted — so the object rows
            /// are AUTHORITATIVE and the database-scope fallback must not
            /// apply (a table-level DENY under a database-scope GRANT
            /// would otherwise read as covered: the pinned G1 gap).
            /// Empty for a database-scope-only capture — the historical
            /// evidence shape, where the fallback is all there is.
            ProbedObjects : Set<string>
        }

    /// The `sys.fn_my_permissions` permission name a write action holds at. Public
    /// so the capability survey can name the source-read (SELECT) permission in the
    /// same vocabulary the sink-write permissions are probed under.
    let permissionName (a: WriteAction) : string =
        match a with
        | Insert      -> "INSERT"
        | Update      -> "UPDATE"
        | Delete      -> "DELETE"
        | Alter       -> "ALTER"
        | CreateTable -> "CREATE TABLE"

    let private objectKey (w: PlannedWrite) : string =
        sprintf "%s.%s" w.Schema w.Table

    /// Pure: does the captured grant cover `permission` on `schema.table`?
    /// A probed object's rows are authoritative (effective permissions —
    /// see `GrantEvidence.ProbedObjects`); an unprobed object falls back
    /// to object-row OR database-scope coverage (the historical rule).
    /// DB-free and testable.
    let coversPermissionOn (schema: string) (table: string) (permission: string) (grant: GrantEvidence) : bool =
        let key = sprintf "%s.%s" schema table
        if Set.contains key grant.ProbedObjects then
            Set.contains (key, permission) grant.Granted
        else
            Set.contains (key, permission) grant.Granted
            || Set.contains ("", permission) grant.Granted

    /// Pure: each planned write the grant does not cover (per
    /// `coversPermissionOn`'s scope rules). Deterministic — sorted by
    /// object then action. DB-free and testable.
    let permissionViolations (planned: PlannedWrite list) (grant: GrantEvidence) : PermissionViolation list =
        let covered (w: PlannedWrite) =
            coversPermissionOn w.Schema w.Table (permissionName w.Action) grant
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
    /// Two probe grains (2026-07-07 — the object-scope refinement the P1
    /// survey gated on landed under live cloud evidence: managed estates
    /// carry object/column-scope DML with NO database-scope grant):
    ///   - the DATABASE probe (`fn_my_permissions(NULL, 'DATABASE')`),
    ///     keyed under the empty object-key — the historical capture;
    ///   - one OBJECT probe per planned table
    ///     (`fn_my_permissions('[schema].[table]', 'OBJECT')`, object
    ///     grain only — `subentity_name = ''`; column-grain rows are a
    ///     named residual), keyed `"schema.table"` and recorded in
    ///     `ProbedObjects` so the pure gate treats them as authoritative
    ///     effective permissions (DENYs visible — the G1 closure for
    ///     planned tables).
    /// Read every permission name under the supplied key (module level so
    /// the recursive task state machine is statically compilable — a
    /// nested `let rec` inside a `task { }` is not, FS3511).
    let rec private readGrantRows (key: string) (reader: SqlDataReader) (acc: (string * string) list) : Task<(string * string) list> =
        task {
            let! hasRow = reader.ReadAsync()
            if hasRow then return! readGrantRows key reader ((key, reader.GetString(0)) :: acc)
            else return acc
        }

    /// One object's effective-permission probe. `Some pairs` when the
    /// object EXISTS (its rows are authoritative — see `ProbedObjects`);
    /// `None` when it does not: a missing table and a deny-everything
    /// table both report zero permission rows, but absence is a SHAPE
    /// fact (the shape gate / drift detection own it), not a grant fact —
    /// claiming probe authority over it would misreport a missing sink
    /// table as `insufficientGrant`, so an absent object stays UNPROBED
    /// (database-scope fallback applies). Module-level helper so the
    /// capture's per-object loop is one reducible `let!` (FS3511 —
    /// a conditional `use!` inside a task-CE loop is not statically
    /// compilable in Release).
    let private probeObjectGrant (sink: SqlConnection) (key: string) (securable: string) : Task<(string * string) list option> =
        task {
            use existsCmd = sink.CreateCommand()
            existsCmd.CommandText <- "SELECT CASE WHEN OBJECT_ID(@obj) IS NULL THEN 0 ELSE 1 END"
            existsCmd.Parameters.AddWithValue("@obj", securable) |> ignore
            let! existsScalar = existsCmd.ExecuteScalarAsync()
            if System.Convert.ToInt32 existsScalar = 1 then
                use objCmd = sink.CreateCommand()
                objCmd.CommandText <-
                    "SELECT permission_name FROM sys.fn_my_permissions(@obj, 'OBJECT') WHERE subentity_name = ''"
                objCmd.Parameters.AddWithValue("@obj", securable) |> ignore
                use! objReader = objCmd.ExecuteReaderAsync()
                let! objPairs = readGrantRows key objReader []
                return Some objPairs
            else
                return None
        }

    /// Walk the planned objects recursively — a `let!` inside a `for` in
    /// a task CE is not statically compilable in Release (FS3511), so the
    /// loop is the same module-level recursion shape as `readGrantRows`.
    let rec private probeObjects
        (sink: SqlConnection)
        (remaining: (string * string) list)
        (pairs: (string * string) list)
        (probed: Set<string>)
        : Task<GrantEvidence> =
        task {
            match remaining with
            | [] -> return { Granted = Set.ofList pairs; ProbedObjects = probed }
            | entry :: rest ->
                let schema = fst entry
                let table = snd entry
                let key = sprintf "%s.%s" schema table
                let securable = sprintf "[%s].[%s]" schema table  // LINT-ALLOW: securable-name string for fn_my_permissions/OBJECT_ID at the probe boundary; bracketed identifier text is the functions' input format
                let! objPairs = probeObjectGrant sink key securable
                match objPairs with
                | Some ps -> return! probeObjects sink rest (ps @ pairs) (Set.add key probed)
                | None    -> return! probeObjects sink rest pairs probed
        }

    /// The probe body without the exception envelope — the public capture
    /// wraps ONE `let!` in its `try` (FS3511 discipline).
    let private captureGrantPairs (objects: (string * string) list) (sink: SqlConnection) : Task<GrantEvidence> =
        task {
            if sink.State <> System.Data.ConnectionState.Open then do! sink.OpenAsync()
            // The DATABASE-probe reader must close before the per-object
            // probes run — one connection, no MARS.
            let! dbPairs =
                task {
                    use cmd = sink.CreateCommand()
                    cmd.CommandText <- "SELECT permission_name FROM sys.fn_my_permissions(NULL, 'DATABASE')"
                    use! reader = cmd.ExecuteReaderAsync()
                    return! readGrantRows "" reader []
                }
            return! probeObjects sink (List.distinct objects) dbPairs Set.empty
        }

    /// Capture database-scope grants PLUS per-object effective permissions
    /// for the supplied `(schema, table)` list — the planned-write tables.
    /// An empty list is the historical database-scope-only capture.
    let captureGrantEvidenceFor (objects: (string * string) list) (sink: SqlConnection) : Task<Result<GrantEvidence>> =
        task {
            try
                let! evidence = captureGrantPairs objects sink
                return Ok evidence
            with ex ->
                return Result.failureOf (ValidationError.create "migrate.grantProbeFailed" ex.Message)
        }

    /// The database-scope-only capture — the historical shape (empty
    /// `ProbedObjects`; the pure gate's database fallback applies).
    let captureGrantEvidence (sink: SqlConnection) : Task<Result<GrantEvidence>> =
        captureGrantEvidenceFor [] sink

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
    //
    // THE VECTOR §5.1 / M20 — the HONEST-NAMING half. The live atomic wrapper
    // above stays deferred (the A3 scaffold is the survey's to resolve). What
    // lands NOW is the named refusal: the typed protection capability, the pure
    // gate that turns an unprotected destructive write path into one named
    // violation per planned write, and the paired `migrate.midWriteNotProtected`
    // refusal. This is the classification half — it does not wrap
    // `MigrationRun.execute`; the live `BEGIN TRAN` is still the survey's (A3,
    // above) to resolve. The pure gate is the buildable scaffold the wrapper
    // will consume once its granularity is fixed.

    /// The write path's protection against a mid-write crash — the typed
    /// capability the transactionality gate reasons over. `Atomic`: the whole
    /// write commits or rolls back (a `BEGIN TRAN` wrapper). `Resumable`: a
    /// partial write is idempotently re-runnable (the surrogate-keyed upsert).
    /// `Unprotected`: neither — a crash part-way through Phase 2 leaves a
    /// half-populated target. Closed DU so the gate stays total over it.
    type TransactionProtection =
        | Atomic
        | Resumable
        | Unprotected

    /// One planned destructive write that, on an unprotected path, would be left
    /// half-applied by a mid-write crash. Mirrors `PermissionViolation`: the
    /// object the violation is located on, and the action that would not be
    /// protected.
    type TransactionalityViolation =
        {
            Object : string
            Action : WriteAction
        }

    /// Pure: under an `Unprotected` write path, every planned destructive write
    /// is a violation — a mid-write crash would leave it half-applied. `Atomic`
    /// (whole-write rollback) and `Resumable` (idempotent re-run) protect the
    /// path, so neither yields a violation. Deterministic — sorted by object then
    /// action. DB-free and testable. Mirrors `permissionViolations`.
    let transactionalityViolations
        (protection: TransactionProtection)
        (planned: PlannedWrite list)
        : TransactionalityViolation list =
        match protection with
        | Atomic | Resumable -> []
        | Unprotected ->
            planned
            |> List.map (fun w -> { Object = objectKey w; Action = w.Action })
            |> List.distinct
            |> List.sortBy (fun v -> v.Object, permissionName v.Action)

    let private describeTransactionality (violations: TransactionalityViolation list) : string =
        violations
        |> List.map (fun v -> sprintf "%s on %s" (permissionName v.Action) v.Object)
        |> String.concat "; "
        |> sprintf "transactionality pre-flight failed before any write — this would leave a half-populated target on a mid-write crash; the write path is neither atomic nor resumable, so these planned writes would be left half-applied: %s. Wrap the write path in a transaction (atomic) or make it resumable before executing."

    /// A3 — refuse `migrate.midWriteNotProtected` when the planned destructive
    /// writes run on an `Unprotected` path (neither atomic nor resumable), BEFORE
    /// any write — a mid-write crash would otherwise leave a half-populated
    /// target rather than a named refusal. Pure: takes the typed protection
    /// capability + the planned writes, no I/O. Mirrors `permissionPreflight`.
    ///
    /// This is the HONEST-NAMING half (THE VECTOR §5.1 / M20). The live atomic
    /// `BEGIN TRAN` wrapper that would make the path `Atomic` stays survey-gated
    /// (the A3 scaffold above); this gate names the refusal so an unprotected
    /// path classifies as the destructive-failure axis (exit 9) rather than the
    /// generic `UnclassifiedRefusal` (exit 3).
    let transactionalityPreflight
        (protection: TransactionProtection)
        (planned: PlannedWrite list)
        : Result<unit> =
        match transactionalityViolations protection planned with
        | [] -> Ok ()
        | violations ->
            Result.failureOf
                (ValidationError.create "migrate.midWriteNotProtected" (describeTransactionality violations))

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
        /// The named destructive class for a mid-write crash on an unprotected
        /// (non-atomic, non-resumable) write path: a failure part-way through
        /// Phase 2 leaves a half-populated target rather than an unchanged or
        /// resumable one. Naming it replaces the generic `UnclassifiedRefusal`
        /// (exit 3) with the destructive-failure axis (exit 9). The honest name;
        /// the live atomic `BEGIN TRAN` wrapper stays survey-gated (see the A3
        /// scaffold above) — this is the classification half only.
        | MidWriteNotProtected
        /// The peer leg's SS_KEY-keyed schema-compatibility gate: the source
        /// and sink models are not one shape over the transferred set
        /// (`PeerTransfer.shapeGate`). The same verdict class `check shape`
        /// reports, so it carries the same exit (5) — a schema divergence, not
        /// a connection/grant/argument failure.
        | ShapeDivergence
        /// The peer leg's subset-FK gate: a declared table subset carries FK
        /// edges to kinds outside it with no strategy chosen (reconcile /
        /// widen / --allow-drops). The drop-set class — a live run would lose
        /// or dangle rows — so it rides the destructive-failure exit (9).
        | SubsetFkEscape
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
        | MidWriteNotProtected        -> "mid-write not protected"
        | ShapeDivergence             -> "schema shapes diverge"
        | SubsetFkEscape              -> "relationships escape the subset"
        | UnclassifiedRefusal         -> "unclassified refusal"

    /// The distinct CLI exit code for each gate axis — TOTAL over the closed
    /// `GateLabel` DU (the compiler forbids an axis without an exit), so the
    /// operator's exit-code contract is ONE greppable mapping rather than numbers
    /// scattered through a prefix chain. The exit IS a function of the axis: every
    /// destructive-failure axis is 9, the connection/schema-read axis 6, the
    /// permission axis 7, the argument axis 2, the named default 3.
    let exitOf (label: GateLabel) : int =
        match label with
        | ConnectionUnavailable       -> 6
        | InsufficientGrant           -> 7
        | ReconciliationMismatch      -> 2
        | UnmappedIdentities          -> 9
        | DataViolatesTightening      -> 9
        | CdcTrackedSink              -> 9
        | SchemaReadFailed            -> 6
        | UndeclaredDestructiveChange -> 9
        | MidWriteNotProtected        -> 9
        | ShapeDivergence             -> 5
        | SubsetFkEscape              -> 9
        | UnclassifiedRefusal         -> 3

    /// Route a refusal code onto its gate axis (`GateLabel`). The explicit
    /// aliasing of the `migrate.*` / `transfer.*` namespaces of one axis: the two
    /// verbs re-code the SAME axis under their own prefixes, and route together
    /// here. A code outside the vocabulary is the named `UnclassifiedRefusal`
    /// default (never a silent miss). Separated from the exit (`exitOf`) so the
    /// routing and the exit-code policy are each independently total + testable.
    let labelOf (code: string) : GateLabel =
        // Connection — exit 6 on transfer; the migrate verb's connection
        // pre-flight surfaces under exit 7 (permission/credential class), so the
        // connection axis is reported on its own (6) and the grant axis on (7).
        if code.StartsWith "transfer.connection" || code = "migrate.connectionUnavailable" then
            ConnectionUnavailable
        elif code = "transfer.insufficientGrant" || code = "transfer.grantProbeFailed"
             || code = "migrate.insufficientGrant" || code = "migrate.grantProbeFailed" then
            InsufficientGrant
        elif code.StartsWith "transfer.reconcile." || code.StartsWith "transfer.userMap." then
            ReconciliationMismatch
        elif code = "transfer.unmappedIdentities" then
            UnmappedIdentities
        elif code = "migrate.dataViolatesTightening" then
            DataViolatesTightening
        elif code = "transfer.cdcTrackedSink" || code = "migrate.cdcTrackedSink" then
            // The same axis under both namespaces — the migrate verb's
            // RefusedByCdc routes through the gate surface under its own name.
            CdcTrackedSink
        elif code = "migrate.schemaReadFailed" then
            SchemaReadFailed
        elif code.StartsWith "migrate.undeclaredDestructive" then
            UndeclaredDestructiveChange
        elif code.StartsWith "transfer.midWriteNotProtected" || code = "migrate.midWriteNotProtected" then
            // The transactionality axis (A3): a mid-write crash on an
            // unprotected write path. The same destructive-failure class as the
            // other exit-9 axes — a half-populated target is a destructive
            // outcome, not a generic refusal.
            MidWriteNotProtected
        elif code = "transfer.peer.shapeDivergence" then
            ShapeDivergence
        elif code = "transfer.peer.subsetFkEscapes" || code = "transfer.subsetFkEscapes" then
            // The peer face's rich-narration refusal AND the engine-level
            // backstop (the parity sweep — legacy/forward legs) ride ONE
            // axis: exit 9, the drop-set class.
            SubsetFkEscape
        elif code.StartsWith "source.ossys." then
            // The peer leg's contract acquisition — an unreadable OSSYS
            // metamodel is the schema-read failure class (exit 6), same axis
            // the migrate verb's schema read reports under its own name.
            SchemaReadFailed
        elif code.StartsWith "adapter.ossysSql." || code.StartsWith "adapter.osm." then
            // 2026-07-06 (the phase-2 mock-env program): a metamodel
            // extraction that fails INSIDE the adapter (a rowset shape /
            // row-mapping failure — e.g. a VIEW-DEFINITION-less principal
            // NULLing a definition column) carries its own adapter code
            // through the Result plane, so it never hits the
            // `source.ossys.readFailed` exception wrapper. It is the SAME
            // operator situation — the schema could not be read — so it
            // rides the same axis (exit 6), never the unclassified 3.
            SchemaReadFailed
        else
            UnclassifiedRefusal

    /// The closed `code → (exit, label)` mapping: route the code onto its axis
    /// (`labelOf`), then read the axis's exit (`exitOf`). TOTAL over the gate code
    /// vocabulary — a code outside it falls to the named `(3, UnclassifiedRefusal)`
    /// default (fail loud, never a silent exit 0). One dispatcher now, routing
    /// (`labelOf`) split from exit-code policy (`exitOf`).
    let classify (code: string) : int * GateLabel =
        let label = labelOf code
        exitOf label, label

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
