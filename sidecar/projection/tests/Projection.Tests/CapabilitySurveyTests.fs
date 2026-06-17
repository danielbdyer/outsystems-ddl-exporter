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
    { Name = name; Access = Access.Direct (ConnectionRef.EnvVar (name + "_CONN")); Grant = grant; Store = None; Rendition = None; Archetype = None; AtomicDeploy = None; Revert = None }

let private flow (name: string) (from: FlowSource) (toEnv: string) : Flow =
    { Name = name; From = from; To = toEnv; Rekey = None; Tables = []; Reconcile = []; Scope = None; Shape = None; Shaping = None; Strategy = None; Resumable = false; Streaming = false; Journal = None }

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

[<Fact>]
let ``NM-57: an unspecified-grant sink requires NOTHING (conservative None default, symmetric with requiredFor)`` () =
    // A place with no declared `grant` used to be bundled with
    // `Some Grant.SchemaAndData` and demanded the LARGEST write set — a spurious
    // "missing capability" / exit-7 advisory. The fix gives `None` its own arm:
    // it requires nothing, regardless of the source shape. Pinned across every
    // source kind so the conservative default cannot silently regress.
    let sinkUndeclared = env "mystery" None
    let liveSource = env "staging" (Some Grant.SchemaAndData)
    for src, label in
        [ FlowSource.Env "staging", "live"
          FlowSource.Model,         "model"
          FlowSource.Synthetic (None, None), "synthetic"
          FlowSource.NoData,        "no-data" ] do
        let cfg = cfgWith [ sinkUndeclared; liveSource ] [ flow ("f-" + label) src "mystery" ]
        let f = Map.find ("f-" + label) cfg.Flows
        Assert.Equal<Set<CapabilitySurvey.Capability>>(
            Set.empty,
            CapabilitySurvey.requiredBy cfg f SubstrateRole.Sink)
    // Symmetry witness: `requiredFor : Grant -> _` has no `None` case, so the
    // unspecified grant demands nothing on EITHER surface — they agree.

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
      Connected = connected; Reachable = reachable; Missing = missing
      GrantUnreadable = false; CdcTracked = false; CdcProbeFailed = false
      UserDirectory = Projection.Adapters.Sql.ReadSide.UserDirectoryProbe.absent
      ArchetypeFindings = [] }

[<Fact>]
let ``NM-55: a reachable place with an unreadable grant is blocked (coverage unverified, not covered)`` () =
    // grantEv = Error _ used to collapse to Missing = [] and report "covered".
    let r = { report "cloud-uat" true true [] with GrantUnreadable = true }
    Assert.True(CapabilitySurvey.blocked r, "grant-unreadable place must be blocked")
    let lines = CapabilitySurvey.advisoryLines [ r ]
    Assert.Contains(lines, fun (l: string) -> l.Contains "grant unreadable")

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

// --- Slice B — the survey VERIFIES the archetype (A44; the J5 covenant) -------

let private envArch (name: string) (archetype: Archetype option) : Projection.Pipeline.Environment =
    { Name = name; Access = Access.Direct (ConnectionRef.EnvVar (name + "_CONN")); Grant = None; Store = None; Rendition = None; Archetype = archetype; AtomicDeploy = None; Revert = None }

/// A database-scope `GrantEvidence` from a permission-name list (object-key "").
let private grantOf (perms: string list) : Preflight.GrantEvidence =
    { Granted = perms |> List.map (fun p -> ("", p)) |> Set.ofList }

[<Fact>]
let ``Slice B (Part 1): the survey routes through the archetype's derived grant — an archetype-declared sink yields the SAME required set as the equivalent grant (byte-identical)`` () =
    // A `full-rights` archetype derives `schema+data`; a `managed-dml` archetype
    // derives `data` — so `requiredBy` is identical to the hand-set grant.
    let liveSource = env "staging" (Some Grant.SchemaAndData)
    let archetypeSink = envArch "uat-arch" (Some Archetype.FullRights)
    let grantSink     = env "uat-grant" (Some Grant.SchemaAndData)
    let cfgA = cfgWith [ liveSource; archetypeSink ] [ flow "a" (FlowSource.Env "staging") "uat-arch" ]
    let cfgG = cfgWith [ liveSource; grantSink ]     [ flow "g" (FlowSource.Env "staging") "uat-grant" ]
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        CapabilitySurvey.requiredBy cfgG (Map.find "g" cfgG.Flows) SubstrateRole.Sink,
        CapabilitySurvey.requiredBy cfgA (Map.find "a" cfgA.Flows) SubstrateRole.Sink)
    // The ManagedDml mirror: derives `data` ⇒ the DML-only required set.
    let dmlSink = envArch "cloud" (Some Archetype.ManagedDml)
    let cfgD = cfgWith [ liveSource; dmlSink ] [ flow "d" (FlowSource.Env "staging") "cloud" ]
    Assert.Equal<Set<CapabilitySurvey.Capability>>(
        set [ Performs Preflight.Insert; Performs Preflight.Delete ],
        CapabilitySurvey.requiredBy cfgD (Map.find "d" cfgD.Flows) SubstrateRole.Sink)

[<Fact>]
let ``Slice B (Part 2): a declared FullRights whose probe COVERS DDL + IDENTITY_INSERT + DMV reconciles clean`` () =
    let grant = grantOf [ "CREATE TABLE"; "ALTER"; "VIEW DATABASE PERFORMANCE STATE"; "INSERT"; "DELETE" ]
    Assert.Empty(CapabilitySurvey.reconcileArchetype Archetype.FullRights grant)

[<Fact>]
let ``Slice B (Part 2): a declared FullRights missing CREATE TABLE / IDENTITY_INSERT is a NAMED required-capability mismatch`` () =
    let grant = grantOf [ "INSERT"; "DELETE" ]   // DML only — no DDL
    let findings = CapabilitySurvey.reconcileArchetype Archetype.FullRights grant
    Assert.Contains(CapabilitySurvey.RequiredCapabilityDenied "CREATE TABLE", findings)
    Assert.Contains(CapabilitySurvey.RequiredCapabilityDenied "ALTER (IDENTITY_INSERT)", findings)

[<Fact>]
let ``Slice B (Part 2): the on-prem FullRights-minus-DMV classifies correctly — DDL present, DMV-read absent surfaces as the SPLIT (not a misdeclaration)`` () =
    // The real on-prem target (2026-06-15): full DDL/DML, minus VIEW DATABASE
    // PERFORMANCE STATE. The class is FullRights; only the DMV probe degrades.
    let grant = grantOf [ "CREATE TABLE"; "ALTER"; "INSERT"; "DELETE" ]   // no DMV-read
    let findings = CapabilitySurvey.reconcileArchetype Archetype.FullRights grant
    Assert.Equal<CapabilitySurvey.ArchetypeFinding list>(
        [ CapabilitySurvey.SoftCapabilityAbsent "VIEW DATABASE PERFORMANCE STATE" ], findings)

[<Fact>]
let ``Slice B (Part 2): a declared ManagedDml that UNEXPECTEDLY permits IDENTITY_INSERT is surfaced (safer than declared)`` () =
    let grant = grantOf [ "INSERT"; "DELETE"; "ALTER"; "CREATE TABLE" ]   // more than DML-only
    let findings = CapabilitySurvey.reconcileArchetype Archetype.ManagedDml grant
    Assert.Contains(CapabilitySurvey.ForbiddenCapabilityPermitted "CREATE TABLE", findings)
    Assert.Contains(CapabilitySurvey.ForbiddenCapabilityPermitted "ALTER (IDENTITY_INSERT)", findings)

[<Fact>]
let ``Slice B (Part 2): a declared ManagedDml on the true J5 DML-only grant reconciles clean`` () =
    let grant = grantOf [ "INSERT"; "DELETE"; "SELECT" ]
    Assert.Empty(CapabilitySurvey.reconcileArchetype Archetype.ManagedDml grant)

[<Fact>]
let ``Slice B: a report carrying archetype findings surfaces them in advisoryLines (even when not blocked)`` () =
    let r =
        { report "onprem" true true [] with
            ArchetypeFindings = [ CapabilitySurvey.SoftCapabilityAbsent "VIEW DATABASE PERFORMANCE STATE" ] }
    Assert.False(CapabilitySurvey.blocked r)   // a split is NOT blocking
    let lines = CapabilitySurvey.advisoryLines [ r ]
    Assert.Contains(lines, fun (l: string) -> l.Contains "onprem" && l.Contains "VIEW DATABASE PERFORMANCE STATE")
