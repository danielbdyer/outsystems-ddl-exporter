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
    { Name = name; From = from; To = toEnv; Rekey = None; Tables = [] }

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
