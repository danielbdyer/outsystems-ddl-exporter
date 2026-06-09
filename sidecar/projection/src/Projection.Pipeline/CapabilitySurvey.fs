namespace Projection.Pipeline

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Adapters.Sql

/// THE CAPABILITY SURVEY — prototype (2026-06-09; see
/// `HANDOFF_CAPABILITY_SURVEY_2026_06_09.md`). Live-probe every connected
/// environment **in parallel** and reconcile each place's **declared** grant
/// facet (`projection.json` `grant`) against its **actual** grant
/// (`sys.fn_my_permissions`) — the dual of the per-actor permission profile:
/// *is every environment actually able to do what the pipeline asks of it?*
///
/// Reuses the verified pre-flight probes — `connectionPreflight`-class
/// reachability (the open), `captureGrantEvidence` (the grant), and
/// `ReadSide.cdcTrackedTables` (the CDC axis). The required-capability catalog
/// is derived here from the coarse `grant` facet (the MVP); the full
/// per-use-case obligations matrix (`THE_USE_CASE_ONTOLOGY.obligations.md`) is
/// the deferred refinement (the handoff scopes it). Read-only — no event moves.
[<RequireQualifiedAccess>]
module CapabilitySurvey =

    /// What a declared grant facet **promises** a place can do — the activities
    /// the survey holds the live grant to. (Prototype: derived from the coarse
    /// `grant` facet; the per-use-case catalog refines this — see the handoff.)
    let requiredFor (grant: Grant) : Set<Preflight.WriteAction> =
        match grant with
        | Grant.SchemaAndData -> set [ Preflight.Insert; Preflight.Delete; Preflight.Alter; Preflight.CreateTable ]
        | Grant.DataOnly      -> set [ Preflight.Insert; Preflight.Delete ]

    /// The survey's verdict for one environment.
    type EnvironmentReport =
        {
            Name       : string
            Grant      : Grant option
            /// The place has a live address (`Access.Direct`) — it is probeable.
            Connected  : bool
            Reachable  : bool
            /// The activities the declared grant promises that the live grant
            /// does not actually cover (at database scope) — empty = covered.
            Missing    : Preflight.WriteAction list
            CdcTracked : bool
        }

    /// Pure: the activities a place's declared grant promises that its live
    /// grant does not cover at database scope. The reconciliation core.
    let reconcile (grant: Grant) (evidence: Preflight.GrantEvidence) : Preflight.WriteAction list =
        requiredFor grant
        |> Set.toList
        |> List.filter (fun a -> not (Preflight.coversAtDatabaseScope a evidence))

    /// Probe ONE environment (boundary): reachability, the grant reconciliation,
    /// the CDC axis. A `Bundle` / `Docker` place has no live address — reported
    /// as not-connected (nothing to probe), never an error; a `Direct` place
    /// that will not resolve / open is connected-but-unreachable.
    let private probeEnvironment (env: Environment) : Task<EnvironmentReport> =
        task {
            let baseReport =
                { Name = env.Name; Grant = env.Grant
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
                            match env.Grant, grantEv with
                            | Some g, Ok ev -> reconcile g ev
                            | _             -> []
                        return
                            { baseReport with
                                Connected = true; Reachable = true
                                Missing = missing; CdcTracked = not (List.isEmpty tracked) }
                    with _ -> return { baseReport with Connected = true }
        }

    /// Probe EVERY environment in the config — completely **in parallel**
    /// (`Task.WhenAll`): the whole estate surveyed at once, ordered by name for a
    /// deterministic board.
    let survey (config: ProjectionConfig) : Task<EnvironmentReport list> =
        task {
            let envs =
                config.Environments |> Map.toList |> List.map snd |> List.sortBy (fun e -> e.Name)
            let! reports = envs |> List.map probeEnvironment |> Task.WhenAll
            return List.ofArray reports
        }
