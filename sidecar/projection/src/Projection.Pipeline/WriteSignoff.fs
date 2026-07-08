namespace Projection.Pipeline

// LINT-ALLOW-FILE: the write-signoff vocabulary composes operator-facing IMPACT
//   prose (the greenlight's default acknowledged-impact per destructive mode, and
//   the go-board remedy) at a config/reporting boundary, exactly as `GoBoard` /
//   `SupportingScope` do. THE_VOICE register: stative, agentless, evidence beneath.
//   The module is pure — the face supplies the run's destructive modes + scope.

/// THE WRITE SIGNOFF (2026-07-08, the greenlight program) — the first-class,
/// declarative APPROVAL for a destructive write mode. A flow that will wipe rows
/// (`replace` / `fresh`), drop rows (`drops`), flood CDC (`cdc`), write explicit
/// keys (`identity-insert`), or converge-delete (`delete-scope`) must ENUMERATE
/// that mode in its `signoff` array — the operator declaring "the impact is
/// understood and greenlit".
///
/// Unlike `supportingScope` (a STRUCTURAL claim the relationship graph confirms or
/// contradicts), a signoff is an AUTHORIZATION predicate: there is no graph fact to
/// verify — the go board goes RED and the engine refuses BY NAME until the
/// destructive mode a run actually performs is greenlit (and, when a table scope is
/// declared, until that scope covers the plan). It joins the
/// `PROJECTION_ALLOW_EXECUTE` / `--go` / `--allow-drops` / `--allow-cdc`
/// authorization family — but DURABLE, per-flow, and auditable in config.
[<RequireQualifiedAccess>]
module WriteSignoff =

    /// A destructive / high-impact write mode a run may perform. Closed so the
    /// enumerate + verify stay total — a new destructive mode becomes a compiler
    /// event that forces its signoff arm (and its impact statement).
    [<RequireQualifiedAccess>]
    type WriteMode =
        /// Child-first wipe of the transferred subset, then reload (WipeAndLoad).
        | Replace
        /// Genesis: an empty baseline assumed; every kind an Add, no losses computed.
        | Fresh
        /// FK-orphan / unmatched-reference rows are dropped (row loss).
        | Drops
        /// The write floods the CDC capture ledger (2×|rows| on a wipe-and-load).
        | Cdc
        /// Source surrogate keys written directly (SET IDENTITY_INSERT).
        | IdentityInsert
        /// A convergent `WHEN NOT MATCHED BY SOURCE … DELETE` arm (emission lane).
        | DeleteScope

    /// One authored `signoff` entry — the mode approved, an optional table scope
    /// (empty = the flow's whole set for this mode), the operator's acknowledged
    /// impact (echoed / verifiable), and optional audit fields.
    type WriteApproval =
        { Mode               : WriteMode
          Tables             : string list
          AcknowledgedImpact : string option
          ApprovedBy         : string option
          Date               : string option }

    /// The canonical config label for a mode.
    let modeLabel (m: WriteMode) : string =
        match m with
        | WriteMode.Replace        -> "replace"
        | WriteMode.Fresh          -> "fresh"
        | WriteMode.Drops          -> "drops"
        | WriteMode.Cdc            -> "cdc"
        | WriteMode.IdentityInsert -> "identity-insert"
        | WriteMode.DeleteScope    -> "delete-scope"

    /// Parse a config label to a mode (case/whitespace-insensitive).
    let parseMode (s: string) : WriteMode option =
        match s.Trim().ToLowerInvariant() with
        | "replace"         -> Some WriteMode.Replace
        | "fresh"           -> Some WriteMode.Fresh
        | "drops"           -> Some WriteMode.Drops
        | "cdc"             -> Some WriteMode.Cdc
        | "identity-insert" -> Some WriteMode.IdentityInsert
        | "delete-scope"    -> Some WriteMode.DeleteScope
        | _                 -> None

    /// The default IMPACT statement a mode carries — stative, evidence-grounded
    /// (THE_VOICE), the same register `SupportingScope.guaranteeOf` uses. The go
    /// board echoes this when a mode is ungreenlit, so the operator reads exactly
    /// what they are approving before they write the signoff.
    let impactOf (m: WriteMode) : string =
        match m with
        | WriteMode.Replace ->
            "Every row in the transferred subset is deleted child-first, then reloaded — a target row absent from the source is removed, not preserved."
        | WriteMode.Fresh ->
            "The target is assumed empty: every source row inserts, nothing is matched, and no losses are computed — a non-empty target's divergent rows are overwritten without a diff."
        | WriteMode.Drops ->
            "Rows whose foreign key points at an unmatched record are dropped — they do not load, and the count is reported, never recovered."
        | WriteMode.Cdc ->
            "A wipe-and-load emits a delete-image and an insert-image per row, so the sink's CDC capture ledger takes twice the row count — not the CDC-silent idempotent redeploy."
        | WriteMode.IdentityInsert ->
            "Source surrogate keys are written directly under SET IDENTITY_INSERT — explicit primary keys that can collide with keys the target already minted."
        | WriteMode.DeleteScope ->
            "A convergent MERGE arm deletes every target row absent from the source within the scope predicate — the target converges to the source, losing the rows outside it."

    /// The verdict of checking ONE destructive mode a run performs against the
    /// flow's approvals.
    type SignoffVerdict =
        /// Greenlit and (if a scope was declared) the scope covers the plan.
        | Confirmed of note: string
        /// No approval for a destructive mode the run performs.
        | Missing of reason: string * remedy: string
        /// Greenlit, but the declared `tables` do NOT cover the tables the plan
        /// actually touches for this mode — a stale approval cannot rubber-stamp a
        /// wider blast radius.
        | ScopeMismatch of reason: string * remedy: string

    /// Check one destructive mode the plan will perform against a flow's approvals.
    /// `planTables` are the LOGICAL table names the mode actually touches (from the
    /// forecast, in the same vocabulary the operator writes `tables`); `[]` skips the
    /// scope check (the mode is gated on presence alone). Total.
    let verify (flow: string) (approvals: WriteApproval list) (m: WriteMode) (planTables: string list) : SignoffVerdict =
        match approvals |> List.tryFind (fun a -> a.Mode = m) with
        | None ->
            Missing
                (sprintf "%s: %s" (modeLabel m) (impactOf m),
                 sprintf "declare it greenlit — add { \"mode\": \"%s\" } to flow %s's `signoff` after reviewing the impact above." (modeLabel m) flow)
        | Some a ->
            // An empty `tables` scope greenlights the whole flow's set for this mode;
            // a declared scope must COVER every table the plan actually touches.
            if List.isEmpty a.Tables || List.isEmpty planTables then
                Confirmed (defaultArg a.AcknowledgedImpact (impactOf m))
            else
                let declared = Set.ofList a.Tables
                match planTables |> List.filter (fun t -> not (Set.contains t declared)) with
                | [] -> Confirmed (defaultArg a.AcknowledgedImpact (impactOf m))
                | uncovered ->
                    ScopeMismatch
                        (sprintf "%s is greenlit only for [%s], but the run touches [%s] — the approval does not cover %s."
                            (modeLabel m) (String.concat ", " a.Tables) (String.concat ", " planTables) (String.concat ", " uncovered),
                         sprintf "widen the signoff's `tables` for %s to include %s, or drop `tables` to greenlit the whole flow."
                            (modeLabel m) (String.concat ", " uncovered))

    /// The modes greenlit for a flow (presence, ignoring scope) — the resolved set
    /// the engine reads to gate a live write.
    let approvedModes (approvals: WriteApproval list) : Set<WriteMode> =
        approvals |> List.map (fun a -> a.Mode) |> Set.ofList
