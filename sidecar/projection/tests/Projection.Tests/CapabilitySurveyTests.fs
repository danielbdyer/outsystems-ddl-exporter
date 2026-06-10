module Projection.Tests.CapabilitySurveyTests

open Xunit
open Projection.Core
open Projection.Pipeline
open type Projection.Pipeline.CapabilitySurvey.Capability

/// The capability survey (S2) — the pure core: the capability vocabulary, the
/// flow-shape-driven required-capability derivation, the harvest per place, and
/// the required-vs-actual reconciliation. The parallel live probe is witnessed in
/// the Docker pool; the `required ⇔ surveyed` totality lives in
/// `CapabilitySurveyTotalityTests`.

// --- the coarse upper bound (a grant's permitted writes) -------------------

[<Fact>]
let ``requiredFor: schema+data permits the schema activities; data-only just the DML`` () =
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete
              Performs Preflight.Alter; Performs Preflight.CreateTable ],
        CapabilitySurvey.requiredFor Grant.SchemaAndData)
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete ],
        CapabilitySurvey.requiredFor Grant.DataOnly)

// --- reconciliation --------------------------------------------------------

[<Fact>]
let ``reconcile: a data-only grant against a schema+data requirement misses the schema activities`` () =
    let evidence : Preflight.GrantEvidence = { Granted = set [ ("", "INSERT"); ("", "DELETE") ] }
    let missing = CapabilitySurvey.reconcile (CapabilitySurvey.requiredFor Grant.SchemaAndData) evidence
    Assert.Contains(Performs Preflight.Alter, missing)
    Assert.Contains(Performs Preflight.CreateTable, missing)
    Assert.DoesNotContain(Performs Preflight.Insert, missing)   // INSERT is actually granted

[<Fact>]
let ``reconcile: a full grant covers the schema+data requirement — nothing missing`` () =
    let evidence : Preflight.GrantEvidence =
        { Granted = set [ ("", "INSERT"); ("", "DELETE"); ("", "ALTER"); ("", "CREATE TABLE") ] }
    Assert.Empty(CapabilitySurvey.reconcile (CapabilitySurvey.requiredFor Grant.SchemaAndData) evidence)

[<Fact>]
let ``reconcile: a source-read requirement misses a grant that holds no SELECT`` () =
    let evidence : Preflight.GrantEvidence = { Granted = set [ ("", "INSERT"); ("", "DELETE") ] }
    Assert.Equal<CapabilitySurvey.Capability list>([ Reads ], CapabilitySurvey.reconcile (set [ Reads ]) evidence)

// --- the capability catalog (harvest-central) ------------------------------

[<Fact>]
let ``CapabilitySurvey.Capability.all is Reads plus every write action; permissionOf names each`` () =
    Assert.Equal<CapabilitySurvey.Capability list>(
        [ Reads; Performs Preflight.Insert; Performs Preflight.Delete
          Performs Preflight.Alter; Performs Preflight.CreateTable ],
        CapabilitySurvey.Capability.all)
    Assert.Equal<string>("SELECT", CapabilitySurvey.Capability.permissionOf Reads)
    Assert.Equal<string>("CREATE TABLE", CapabilitySurvey.Capability.permissionOf (Performs Preflight.CreateTable))

// --- the flow-shape derivation (requiredBy) --------------------------------

let private env (name: string) (grant: Grant option) : Projection.Pipeline.Environment =
    { Name = name; Access = Access.Direct (ConnectionRef.EnvVar (name + "_CONN")); Grant = grant; Store = None; Rendition = None }

let private flow (name: string) (from: FlowSource) (toEnv: string) : Flow =
    { Name = name; From = from; To = toEnv; Rekey = None; Tables = []; Scope = None; Shape = None }

let private cfgWith (envs: Projection.Pipeline.Environment list) (flows: Flow list) : ProjectionConfig =
    { ProjectionConfig.empty with
        Environments = envs |> List.map (fun e -> e.Name, e) |> Map.ofList
        Flows        = flows |> List.map (fun f -> f.Name, f) |> Map.ofList }

[<Fact>]
let ``requiredBy: a model-to-schema+data flow needs schema only — strictly less than the coarse facet`` () =
    // The S2 trigger (open-Q1): a no-data / model source against a schema+data
    // target publishes schema (ALTER/CREATE) but moves no data, so it needs
    // strictly LESS than requiredFor(SchemaAndData) — the over-refusal the coarse
    // reconciliation would cause.
    let target = env "uat" (Some Grant.SchemaAndData)
    let cfg = cfgWith [ target ] [ flow "publish" FlowSource.Model "uat" ]
    let sink = CapabilitySurvey.requiredBy cfg (Map.find "publish" cfg.Flows) SubstrateRole.Sink
    Assert.Equal<Set<CapabilitySurvey.Capability>>(set [ Performs Preflight.Alter; Performs Preflight.CreateTable ], sink)
    Assert.True(Set.isProperSubset sink (CapabilitySurvey.requiredFor Grant.SchemaAndData))

[<Fact>]
let ``requiredBy: a cross-substrate transfer reads the source and writes the data-only sink`` () =
    let source = env "staging" (Some Grant.SchemaAndData)
    let sink = env "uat" (Some Grant.DataOnly)
    let cfg = cfgWith [ source; sink ] [ flow "load" (FlowSource.Env "staging") "uat" ]
    let f = Map.find "load" cfg.Flows
    Assert.Equal<Set<CapabilitySurvey.Capability>>(set [ Reads ], CapabilitySurvey.requiredBy cfg f SubstrateRole.Source)
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete ],
        CapabilitySurvey.requiredBy cfg f SubstrateRole.Sink)

[<Fact>]
let ``requiredBy: a live source to a schema+data target migrates schema and data`` () =
    let source = env "staging" (Some Grant.SchemaAndData)
    let sink = env "uat" (Some Grant.SchemaAndData)
    let cfg = cfgWith [ source; sink ] [ flow "migrate" (FlowSource.Env "staging") "uat" ]
    let f = Map.find "migrate" cfg.Flows
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete
              Performs Preflight.Alter; Performs Preflight.CreateTable ],
        CapabilitySurvey.requiredBy cfg f SubstrateRole.Sink)

// --- the harvest (requiredOf) ----------------------------------------------

[<Fact>]
let ``requiredOf: a place is the union of what every flow asks of it across roles`` () =
    // `staging` is a Source for the load flow (Reads) and a Sink for the publish
    // flow (schema publish) — the harvest unions both roles.
    let staging = env "staging" (Some Grant.SchemaAndData)
    let uat = env "uat" (Some Grant.DataOnly)
    let cfg =
        cfgWith
            [ staging; uat ]
            [ flow "publish" FlowSource.Model "staging"
              flow "load" (FlowSource.Env "staging") "uat" ]
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Reads; Performs Preflight.Alter; Performs Preflight.CreateTable ],
        CapabilitySurvey.requiredOf cfg "staging")
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete ],
        CapabilitySurvey.requiredOf cfg "uat")

[<Fact>]
let ``requiredOf: an environment no flow touches is asked for nothing`` () =
    let unused = env "spare" (Some Grant.SchemaAndData)
    let cfg = cfgWith [ unused ] []
    Assert.Equal<Set<CapabilitySurvey.Capability>>(Set.empty, CapabilitySurvey.requiredOf cfg "spare")

// --- G0c: the in-flow advisory (R6 warn, never hard-stop) ------------------

let private report name connected reachable missing : CapabilitySurvey.EnvironmentReport =
    { Name = name; Grant = Some Grant.DataOnly; Required = Set.empty
      Connected = connected; Reachable = reachable; Missing = missing; CdcTracked = false
      UserDirectory = Projection.Adapters.Sql.ReadSide.UserDirectoryProbe.absent }

// --- G0b: EnvironmentReport carries the user-directory probe field ---------

[<Fact>]
let ``EnvironmentReport carries the UserDirectory P10 field (report field, not a Capability variant)`` () =
    // The P10 probe surfaces as a NEW FIELD on EnvironmentReport, NOT a new
    // Capability DU variant — adding a variant would break the required ⇔
    // surveyed totality. This pins the field's presence + that it round-trips.
    let emailKeyed : Projection.Adapters.Sql.ReadSide.UserDirectoryProbe =
        { Found = true; EmailKeyed = true; TableName = Some "dbo.OSSYS_USER" }
    let r = { report "cloud-uat" true true [] with UserDirectory = emailKeyed }
    Assert.True(r.UserDirectory.Found)
    Assert.True(r.UserDirectory.EmailKeyed)
    Assert.Equal(Some "dbo.OSSYS_USER", r.UserDirectory.TableName)
    // The Capability DU is untouched — `Capability.all` is still exactly the
    // five Reads/Performs variants (the totality stays structural).
    Assert.Equal(5, List.length CapabilitySurvey.Capability.all)

[<Fact>]
let ``blocked: a connected place missing a required capability is blocked; an all-clear place is not`` () =
    Assert.True(CapabilitySurvey.blocked (report "missing-insert" true true [ Performs Preflight.Insert ]))
    Assert.True(CapabilitySurvey.blocked (report "unreachable" true false []))
    Assert.False(CapabilitySurvey.blocked (report "covered" true true []))
    // A not-connected place (bundle/docker — no live gate) is never "blocked".
    Assert.False(CapabilitySurvey.blocked (report "no-gate" false false []))

[<Fact>]
let ``advisoryLines: a blocked place yields a warning naming the missing capability — but no exit`` () =
    let reports =
        [ report "cloud-uat" true true []                                // covered
          report "prod"      true true [ Performs Preflight.Insert ] ]   // missing INSERT
    let lines = CapabilitySurvey.advisoryLines reports
    // The warning is emitted (non-empty) and names the blocked place + its gap...
    Assert.NotEmpty(lines)
    Assert.Contains(lines, fun l -> l.Contains "prod" && l.Contains "INSERT")
    // ...and it is MESSAGE-ONLY: `advisoryLines : EnvironmentReport list -> string
    // list` returns the warning text, never an exit code, so the in-flow path
    // cannot borrow the verb's exit-7. The run's own exit stands (R6 — advisory
    // until the per-pair flip). The "covered" place raises no line.
    Assert.DoesNotContain(lines, fun l -> l.Contains "cloud-uat")

[<Fact>]
let ``advisoryLines: an all-clear estate emits no warning (the flow runs silently)`` () =
    let reports = [ report "cloud-uat" true true []; report "onprem" false false [] ]
    Assert.Empty(CapabilitySurvey.advisoryLines reports)

[<Fact>]
let ``advisoryLines: the advisory reads the SAME blocked predicate the verb's gate reads`` () =
    // The MESSAGE (advisoryLines) and the gate (`blocked`) cannot disagree on
    // WHAT is blocked — the advisory names exactly the reports `blocked` selects.
    let reports =
        [ report "a" true true []
          report "b" true false []                              // unreachable
          report "c" true true [ Performs Preflight.Delete ] ]  // missing DELETE
    let blockedNames = reports |> List.filter CapabilitySurvey.blocked |> List.map (fun r -> r.Name)
    let lines = CapabilitySurvey.advisoryLines reports
    for n in blockedNames do
        Assert.Contains(lines, fun l -> l.Contains n)
