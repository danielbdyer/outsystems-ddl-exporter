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
        | (Some Grant.SchemaAndData | None), FlowSource.Env _              -> Set.union schemaPublish dataLoad
        | (Some Grant.SchemaAndData | None), (FlowSource.Model | FlowSource.NoData) -> schemaPublish
        | (Some Grant.SchemaAndData | None), FlowSource.Synthetic _        -> Set.empty

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
            let targetGrant =
                Map.tryFind flow.To config.Environments |> Option.bind (fun e -> e.Grant)
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
            CdcTracked : bool
        }

    /// Probe ONE environment (boundary): reachability, the required-vs-actual
    /// reconciliation, the CDC axis. A `Bundle` / `Docker` place has no live
    /// address — reported as not-connected (nothing to probe), never an error; a
    /// `Direct` place that will not resolve / open is connected-but-unreachable.
    let private probeEnvironment (config: ProjectionConfig) (env: Projection.Pipeline.Environment) : Task<EnvironmentReport> =
        task {
            let required = requiredOf config env.Name
            let baseReport =
                { Name = env.Name; Grant = env.Grant; Required = required
                  Connected = false; Reachable = false; Missing = []; CdcTracked = false }
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
                        let! tracked = ReadSide.cdcTrackedTables cnn
                        let missing =
                            match grantEv with
                            | Ok ev -> reconcile required ev
                            | Error _ -> []
                        return
                            { baseReport with
                                Connected = true; Reachable = true
                                Missing = missing; CdcTracked = not (List.isEmpty tracked) }
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
