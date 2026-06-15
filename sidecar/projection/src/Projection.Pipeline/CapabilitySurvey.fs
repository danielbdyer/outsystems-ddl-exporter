namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// THE CAPABILITY SURVEY (S2 — flow-driven required capabilities; 2026-06-09; see
/// `HANDOFF_CAPABILITY_SURVEY_2026_06_09.md`). Live-probe every connected
/// environment **in parallel** and reconcile each place's **actual** grant
/// (`sys.fn_my_permissions`) against the capabilities the configured **flows**
/// actually require of it — the dual of the per-actor permission profile: *is
/// every environment actually able to do what the pipeline asks of it?*
///
/// S2 reifies the required-capability catalog from the **flow shape** (already in
/// `projection.json`: the target `grant`, the source kind). The required set is a
/// HARVESTED REGISTRY: every flow declares (by its shape) the capabilities it
/// exercises of each substrate role; the survey unions them per place. The
/// closed `Capability` DU + the total `Capability.surveyedBy` probe make the
/// `required ⇔ surveyed` totality structural — no flow can need a capability the
/// survey does not know to probe (the analog of the transform registry's
/// `registered ⇔ executed` and the voice `code ⇔ copy`). The obligations matrix
/// (`THE_USE_CASE_ONTOLOGY.obligations.md` §3/§6 AC-G1/AC-G2 — the gate plane) is
/// the spec the derivation checks against.
///
/// Reuses the verified read-only pre-flight probes — `connectionPreflight`-class
/// reachability (the open), `captureGrantEvidence` (the grant), and
/// `ReadSide.cdcTrackedTables` (the CDC axis). Read-only by construction — no
/// event moves, no schema touched, no write to find out (the load-bearing
/// posture; the write-test deep probe is opt-in only, never here).
[<RequireQualifiedAccess>]
module CapabilitySurvey =

    /// A capability — an activity a place must be able to perform for a flow to
    /// run against it in a given role. The survey's activity vocabulary: the
    /// **source** role reads (SELECT); the **sink** role performs the write
    /// actions (`Preflight.WriteAction`) the flow's shape exercises. Closed so
    /// `permissionOf` / `surveyedBy` is TOTAL over it — the survey cannot fail to
    /// know how to probe a capability a flow requires (the `required ⇔ surveyed`
    /// totality, the closed-DU analog of the registry's `registered ⇔ executed`).
    type Capability =
        | Reads
        | Performs of Preflight.WriteAction

    [<RequireQualifiedAccess>]
    module Capability =

        /// The central capability catalog — every capability the survey knows to
        /// probe. Harvest-central: derived from the closed `Capability` DU
        /// (Reads + every `Preflight.WriteAction`) so a new write action joins by
        /// construction, never by a parallel hand-maintained list.
        let all : Capability list =
            Reads :: (Preflight.allWriteActions |> List.map Performs)

        /// The `sys.fn_my_permissions` permission name a capability reconciles
        /// against. TOTAL over the closed DU — the compiler refuses a new variant
        /// without a probe name (this is what makes `required ⇔ surveyed`
        /// structural rather than vigilant).
        let permissionOf (cap: Capability) : string =
            match cap with
            | Reads      -> "SELECT"
            | Performs a -> Preflight.permissionName a

        /// Does the captured grant cover this capability at database scope? The
        /// per-capability probe — reuses the read-only `fn_my_permissions`
        /// evidence the survey already captures (no new probe; S2 is pure-core).
        let surveyedBy (cap: Capability) (grant: Preflight.GrantEvidence) : bool =
            Preflight.coversPermissionAtDatabaseScope (permissionOf cap) grant

        /// The operator-facing name of a capability (the permission verb).
        let text (cap: Capability) : string = permissionOf cap

    /// The coarse upper bound — the write capabilities a declared `grant` facet
    /// *permits* at a sink. The flow-refined `requiredBy` is always a subset of
    /// this (a flow needs at most what the grant permits); S2 earns its place
    /// exactly where a flow needs strictly LESS (open-Q1 — a model/no-data flow
    /// against a `schema+data` target needs ALTER/CREATE but no INSERT/DELETE, so
    /// the coarse facet would over-refuse).
    let requiredFor (grant: Grant) : Set<Capability> =
        match grant with
        | Grant.SchemaAndData ->
            set [ Performs Preflight.Insert; Performs Preflight.Delete
                  Performs Preflight.Alter; Performs Preflight.CreateTable ]
        | Grant.DataOnly ->
            set [ Performs Preflight.Insert; Performs Preflight.Delete ]

    /// The sink-role write capabilities a flow's shape exercises — faithful to
    /// `Command.planMovement`'s routing (the obligations matrix is the spec):
    ///  - data-only target + a live/synthetic source → the data leg
    ///    (`Transfer` / `SynthesizeAndLoad`): INSERT + DELETE.
    ///  - schema-bearing target + a live source → schema + data
    ///    (`MigrateWithData`): ALTER + CREATE TABLE + INSERT + DELETE.
    ///  - schema-bearing target + model / no data → schema only (`Migrate`):
    ///    ALTER + CREATE TABLE (strictly less than the coarse `schema+data`
    ///    facet — the S2 over-refusal the coarse reconciliation would cause).
    ///  - the residual shapes (`Refused` at plan time — a DML-only load with no
    ///    data source; a synthetic load against a schema target) ask no writes,
    ///    so the survey requires nothing (flow validity is `planFlow`'s gate, not
    ///    the permission survey's).
    let private sinkCapabilities (targetGrant: Grant option) (source: FlowSource) : Set<Capability> =
        let dataLoad = set [ Performs Preflight.Insert; Performs Preflight.Delete ]
        let schemaPublish = set [ Performs Preflight.Alter; Performs Preflight.CreateTable ]
        match targetGrant, source with
        | Some Grant.DataOnly, (FlowSource.Env _ | FlowSource.Synthetic _) -> dataLoad
        | Some Grant.DataOnly, (FlowSource.Model | FlowSource.NoData)       -> Set.empty
        | Some Grant.SchemaAndData, FlowSource.Env _                       -> Set.union schemaPublish dataLoad
        | Some Grant.SchemaAndData, (FlowSource.Model | FlowSource.NoData) -> schemaPublish
        | Some Grant.SchemaAndData, FlowSource.Synthetic _                 -> Set.empty
        // NM-57 — an UNSPECIFIED grant (`None`) gets its OWN conservative arm: it
        // requires NOTHING, rather than (the prior bug) being bundled with
        // `Some Grant.SchemaAndData` and demanding the LARGEST write set. A place
        // with no declared `grant` is one we cannot prove permits schema OR data,
        // so demanding the maximal set inverts the conservative default and fires
        // spurious "missing capability" / exit-7 advisories. This keeps the two
        // reconciliation surfaces SYMMETRIC on the unspecified-grant place: the
        // coarse peer `requiredFor : Grant -> _` has no `None` case (it demands
        // nothing of an undeclared grant), so neither does this.
        | None, _                                                         -> Set.empty

    /// The capabilities a flow requires of a substrate in the given role —
    /// derived purely from the flow's SHAPE (its `to` target's grant facet, its
    /// `from` source kind). The Source role reads (a live `from` env the flow
    /// reads rows from); the Sink role performs the writes the flow's scope +
    /// data origin exercise. Pure; the obligations matrix is the spec.
    let requiredBy (config: ProjectionConfig) (flow: Flow) (role: SubstrateRole) : Set<Capability> =
        match role with
        | SubstrateRole.Source ->
            match flow.From with
            | FlowSource.Env _ -> set [ Reads ]
            | FlowSource.Model | FlowSource.Synthetic _ | FlowSource.NoData -> Set.empty
        | SubstrateRole.Sink ->
            // Slice B — route through the ARCHETYPE's derived grant
            // (`Environment.effectiveArchetype` ∘ `Archetype.grant`) instead of
            // the raw `Grant` facet. Byte-identical for every existing config:
            // an undeclared archetype is inferred from `grant`, and
            // `Archetype.grant ∘ Archetype.ofGrant = id`, so the derived grant
            // equals the hand-set one. An archetype-DECLARED sink derives its
            // grant from the class (the archetype subsumes `Grant`).
            let targetGrant =
                Map.tryFind flow.To config.Environments
                |> Option.bind (fun e -> Environment.effectiveArchetype e |> Option.map Archetype.grant)
            sinkCapabilities targetGrant flow.From

    /// The (environment, role) bindings a flow exercises: its `to` is the Sink;
    /// its `from` (when a live environment) is the Source. The places a flow
    /// touches — role, not identity (a place is a Sink in one flow, a Source in
    /// another).
    let touchedBy (flow: Flow) : (string * SubstrateRole) list =
        [ yield flow.To, SubstrateRole.Sink
          match flow.From with
          | FlowSource.Env e -> yield e, SubstrateRole.Source
          | FlowSource.Model | FlowSource.Synthetic _ | FlowSource.NoData -> () ]

    /// The union of capabilities every configured flow requires of one
    /// environment, across every role it plays — the HARVESTED required set:
    /// "all the activities the use cases require" of this place (the operator's
    /// words). This is what the survey reconciles against the actual grant,
    /// subsuming the coarse declared facet.
    let requiredOf (config: ProjectionConfig) (envName: string) : Set<Capability> =
        config.Flows
        |> Map.toList
        |> List.collect (fun (_, flow) ->
            touchedBy flow
            |> List.filter (fun (e, _) -> e = envName)
            |> List.map (fun (_, role) -> requiredBy config flow role))
        |> List.fold Set.union Set.empty

    /// Pure: the capabilities a required set demands that the live grant does not
    /// cover at database scope. The reconciliation core — deterministic, sorted
    /// by permission name (T1).
    let reconcile (required: Set<Capability>) (evidence: Preflight.GrantEvidence) : Capability list =
        required
        |> Set.toList
        |> List.filter (fun c -> not (Capability.surveyedBy c evidence))
        |> List.sortBy Capability.permissionOf

    /// Slice B — a declared-vs-probed ARCHETYPE finding (A44; the J5 covenant
    /// generalized from a one-time spike into a standing per-class gate). The
    /// declared archetype's `CapabilityProfile` says what the engine will ASSUME;
    /// the probed grant says what is ACTUAL. A divergence is surfaced BY NAME,
    /// never trusted blindly. Closed so the surfacing is total.
    type ArchetypeFinding =
        /// The declared archetype REQUIRES this capability but the probe denies
        /// it — the engine would assume a path the grant cannot support (a
        /// declared `FullRights` lacking CREATE TABLE / IDENTITY_INSERT would
        /// have chosen `PreservedFromSource` + a sink-resident checkpoint).
        | RequiredCapabilityDenied of capability: string
        /// The declared archetype FORBIDS this capability but the probe permits
        /// it — safer-than-declared, but a divergence the operator should see (a
        /// `ManagedDml` sink that UNEXPECTEDLY permits IDENTITY_INSERT means the
        /// simpler `PreservedFromSource` path is actually available).
        | ForbiddenCapabilityPermitted of capability: string
        /// An INDEPENDENTLY-GRANTABLE capability the archetype expects but that is
        /// absent WITHOUT re-classing the archetype — the named SPLIT (the on-prem
        /// `FullRights`-minus-DMV, observed 2026-06-15; DATABASE_ARCHETYPES.md §5):
        /// surfaced + degrade-the-one-probe, NOT a refusal. This is exactly why
        /// `CapabilityProfile` carries each capability as its own verified flag.
        | SoftCapabilityAbsent of capability: string

    // The database-scope permission names the archetype reconciliation probes.
    // `ALTER` is the IDENTITY_INSERT viability proxy (IDENTITY_INSERT requires
    // ALTER on the table — probe sheet E3); `VIEW DATABASE PERFORMANCE STATE` is
    // the DMV-read the fast sizing probes need (the on-prem split, B1/B2).
    let private createTablePermission = "CREATE TABLE"
    let private identityInsertPermission = "ALTER"
    let private dmvReadPermission = "VIEW DATABASE PERFORMANCE STATE"

    /// Reconcile the DECLARED archetype against the probed grant (Slice B). Pure
    /// + DB-free, so the J5 covenant is testable without a connection. A
    /// `FullRights` declaration REQUIRES CREATE TABLE + IDENTITY_INSERT (a denial
    /// is a blocking mismatch) and EXPECTS DMV-read — but DMV-read is
    /// independently grantable, so its absence is the surfaced split, not a
    /// refusal. A `ManagedDml` declaration FORBIDS CREATE TABLE + IDENTITY_INSERT
    /// (their presence is surfaced — safer-than-declared). Deterministic order.
    let reconcileArchetype (declared: Archetype) (grant: Preflight.GrantEvidence) : ArchetypeFinding list =
        let has p = Preflight.coversPermissionAtDatabaseScope p grant
        match declared with
        | Archetype.FullRights ->
            [ if not (has createTablePermission)   then yield RequiredCapabilityDenied createTablePermission
              if not (has identityInsertPermission) then yield RequiredCapabilityDenied (identityInsertPermission + " (IDENTITY_INSERT)")
              if not (has dmvReadPermission)        then yield SoftCapabilityAbsent dmvReadPermission ]
        | Archetype.ManagedDml ->
            [ if has createTablePermission   then yield ForbiddenCapabilityPermitted createTablePermission
              if has identityInsertPermission then yield ForbiddenCapabilityPermitted (identityInsertPermission + " (IDENTITY_INSERT)") ]

    /// The operator-facing line for one archetype finding (the advisory surface).
    /// The finding TYPE already implies the class — `RequiredCapabilityDenied`
    /// arises only for a declared `FullRights`, `ForbiddenCapabilityPermitted`
    /// only for `ManagedDml` — so the line needs no separate class argument.
    let describeFinding (finding: ArchetypeFinding) : string =
        match finding with
        | RequiredCapabilityDenied cap     -> sprintf "archetype mismatch — the declared class REQUIRES %s but the probe DENIES it (the engine would assume a path this grant cannot support)" cap
        | ForbiddenCapabilityPermitted cap -> sprintf "archetype divergence — the declared class FORBIDS %s but the probe PERMITS it (safer than declared — a simpler path is available)" cap
        | SoftCapabilityAbsent cap         -> sprintf "archetype split — %s absent (an independently-grantable capability; that one probe degrades, the class is unchanged)" cap

    /// The survey's verdict for one environment.
    type EnvironmentReport =
        {
            Name       : string
            Grant      : Grant option
            /// The capabilities the configured flows require of this place
            /// (harvested across every role it plays).
            Required   : Set<Capability>
            /// The place has a live address (`Access.Direct`) — it is probeable.
            Connected  : bool
            Reachable  : bool
            /// The required capabilities the live grant does not actually cover
            /// (at database scope) — empty = covered.
            Missing    : Capability list
            /// NM-55 — the grant probe (`sys.fn_my_permissions`) could not be
            /// read on a reachable place (least-privilege / permission-denied).
            /// `true` means coverage is UNVERIFIED, not confirmed: a REPORT
            /// FIELD (like `UserDirectory`), and `blocked` treats it as blocking
            /// so the survey never claims "covered" for an unprobed grant.
            GrantUnreadable : bool
            CdcTracked : bool
            /// NM-54 — the CDC-tracked probe (`ReadSide.cdcTrackedTables`)
            /// could not be read on a reachable place (transient SqlException /
            /// `VIEW DEFINITION` denial). `true` means the CDC axis is
            /// UNVERIFIED, not "no CDC": a REPORT FIELD (like `GrantUnreadable`),
            /// surfaced advisory so the survey never fabricates a clean CDC
            /// verdict for an unprobed sink. `CdcTracked` is forced `false` when
            /// this is `true` (the axis was never observed).
            CdcProbeFailed : bool
            /// G0b (P10) — the user-directory readability verdict: is the
            /// platform user table the `golden`/`preview` re-key matches against
            /// SELECT-able, and does it expose an email-shaped key column? A
            /// REPORT FIELD, not a `Capability` variant — adding a variant would
            /// break the `required ⇔ surveyed` totality
            /// (`CapabilitySurveyTotalityTests`). `absent` for a place with no
            /// live address (nothing probed).
            UserDirectory : ReadSide.UserDirectoryProbe
            /// Slice B — the declared-vs-probed ARCHETYPE reconciliation findings,
            /// computed ONLY when the place EXPLICITLY declares an `archetype`
            /// (an inferred archetype is not a claim to verify). Empty for an
            /// undeclared place (byte-identical to the pre-Slice-B survey) or a
            /// place whose grant probe failed. A REPORT FIELD (like
            /// `UserDirectory`), not a `Capability` variant — surfaced via
            /// `advisoryLines`, so the `required ⇔ surveyed` totality is untouched.
            ArchetypeFindings : ArchetypeFinding list
        }

    /// Does a report name a *blocked* capability — a connected place that is
    /// unreachable, or reachable but missing a required capability? The SINGLE
    /// predicate the standalone `survey` verb's exit-7 gate AND the in-flow
    /// advisory both read for the MESSAGE, so the two cannot disagree on WHAT is
    /// blocked. They differ only on the EXIT: the verb stops (exit 7); the
    /// in-flow advisory warns and proceeds (R6 — V2 owns no production write
    /// path; the gate is advisory until the per-pair flip; CLAUDE.md R6;
    /// DECISIONS 2026-06-09 S3).
    let blocked (r: EnvironmentReport) : bool =
        // NM-55 — an unreadable grant on a reachable place is blocking:
        // coverage is unverified, so "covered" must not be claimed.
        r.Connected && (not r.Reachable || r.GrantUnreadable || not (List.isEmpty r.Missing))

    /// The advisory warning lines for a set of survey reports — the in-flow
    /// surface (G0c). Empty when nothing is blocked (no warning, the flow runs
    /// silently); otherwise one heading + one line per blocked place naming why
    /// (unreachable / missing the named capabilities). Reuses `blocked` for the
    /// selection and `Capability.text` for the names, so it reads the same set
    /// the verb's gate does. Pure over the reports; the caller emits to stderr
    /// and PROCEEDS regardless (the advisory never changes an exit).
    let advisoryLines (reports: EnvironmentReport list) : string list =
        let blockedReports = reports |> List.filter blocked
        // Slice B — places whose DECLARED archetype diverged from the probe.
        // Independent of `blocked` (a place can be fully covered yet carry a
        // FullRights-minus-DMV split, or a safer-than-declared ManagedDml).
        let findingReports = reports |> List.filter (fun r -> not (List.isEmpty r.ArchetypeFindings))
        if List.isEmpty blockedReports && List.isEmpty findingReports then []
        else
            [ yield "Advisory — capability survey found environment(s) that may not be able to do what this run asks (proceeding anyway; this is a warning, not a gate):"
              for r in blockedReports do
                  if not r.Reachable then
                      yield sprintf "  %s: unreachable" r.Name
                  elif r.GrantUnreadable then
                      yield sprintf "  %s: grant unreadable (coverage unverified)" r.Name
                  else
                      yield sprintf "  %s: missing %s" r.Name (r.Missing |> List.map Capability.text |> String.concat ", ")
              // The declared-vs-probed archetype reconciliation (the J5 covenant
              // generalized) — surfaced, never a gate; the run proceeds (R6).
              for r in findingReports do
                  for f in r.ArchetypeFindings do
                      yield sprintf "  %s: %s" r.Name (describeFinding f) ]

    /// Probe ONE environment (boundary): reachability, the required-vs-actual
    /// reconciliation, the CDC axis. A `Bundle` / `Docker` place has no live
    /// address — reported as not-connected (nothing to probe), never an error; a
    /// `Direct` place that will not resolve / open is connected-but-unreachable.
    let private probeEnvironment (config: ProjectionConfig) (env: Projection.Pipeline.Environment) : Task<EnvironmentReport> =
        task {
            let required = requiredOf config env.Name
            let baseReport =
                { Name = env.Name; Grant = env.Grant; Required = required
                  Connected = false; Reachable = false; Missing = []; GrantUnreadable = false; CdcTracked = false
                  CdcProbeFailed = false
                  UserDirectory = ReadSide.UserDirectoryProbe.absent
                  ArchetypeFindings = [] }
            match env.Access with
            | Access.Bundle _ | Access.Docker -> return baseReport
            | Access.Direct connRef ->
                match ConnectionResolver.resolve env.Name connRef with
                | Error _ -> return { baseReport with Connected = true }
                | Ok connStr ->
                    try
                        use cnn = new SqlConnection(connStr)
                        do! cnn.OpenAsync()
                        let! grantEv = Preflight.captureGrantEvidence cnn
                        let! trackedEv = ReadSide.cdcTrackedTables cnn
                        // G0b (P10) — the user-directory readability probe, next to
                        // the CDC axis. Conventional candidate names (configurable
                        // is the residual that pairs with OPEN-2's real-instance
                        // user-table identity); read-only.
                        let! userDir = ReadSide.userDirectoryReadability [] cnn
                        // NM-55 — a failed grant probe is NOT "no missing
                        // capabilities"; it is unverified coverage. Carry it as
                        // `GrantUnreadable` so `blocked` refuses the "covered"
                        // claim, rather than collapsing `Error _` to `[]`.
                        let missing, grantUnreadable =
                            match grantEv with
                            | Ok ev   -> reconcile required ev, false
                            | Error _ -> [], true
                        // Slice B — the archetype reconciliation runs ONLY on an
                        // EXPLICITLY declared archetype (the operator's claim to
                        // verify) and only when the grant probe succeeded; an
                        // inferred/undeclared place is byte-identical (no findings).
                        let archetypeFindings =
                            match env.Archetype, grantEv with
                            | Some declared, Ok ev -> reconcileArchetype declared ev
                            | _ -> []
                        // NM-54 — a failed CDC probe is NOT "no CDC"; it is an
                        // UNVERIFIED axis. Carry it as `CdcProbeFailed` (mirroring
                        // `GrantUnreadable`) so the survey surfaces the unreadable
                        // axis rather than fabricating a clean CDC verdict.
                        let cdcTracked, cdcProbeFailed =
                            match trackedEv with
                            | Ok tracked -> not (List.isEmpty tracked), false
                            | Error _    -> false, true
                        return
                            { baseReport with
                                Connected = true; Reachable = true
                                Missing = missing; GrantUnreadable = grantUnreadable
                                CdcTracked = cdcTracked; CdcProbeFailed = cdcProbeFailed
                                UserDirectory = userDir
                                ArchetypeFindings = archetypeFindings }
                    with _ -> return { baseReport with Connected = true }
        }

    /// Probe EVERY environment in the config — completely **in parallel**
    /// (`Task.WhenAll`): the whole estate surveyed at once, ordered by name for a
    /// deterministic board. Each place is reconciled against the capabilities the
    /// configured flows require of it (the flow-harvested union).
    let survey (config: ProjectionConfig) : Task<EnvironmentReport list> =
        task {
            let envs =
                config.Environments |> Map.toList |> List.map snd |> List.sortBy (fun e -> e.Name)
            let! reports = envs |> List.map (probeEnvironment config) |> Task.WhenAll
            return List.ofArray reports
        }
