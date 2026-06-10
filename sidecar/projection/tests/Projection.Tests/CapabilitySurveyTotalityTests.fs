module Projection.Tests.CapabilitySurveyTotalityTests

open Xunit
open Projection.Core
open Projection.Pipeline
open type Projection.Pipeline.CapabilitySurvey.Capability

/// THE CAPABILITY SURVEY — the `required ⇔ surveyed` totality test (S2;
/// `HANDOFF_CAPABILITY_SURVEY_2026_06_09.md` adjustment 3). The sibling of the
/// transform registry's `registered ⇔ executed` and the voice `code ⇔ copy`:
/// every capability a configured flow can require of a place is one the survey
/// knows how to probe, and every capability the survey probes is reachable as a
/// real flow requirement (no dead probe). Completeness by construction — you
/// cannot add a flow that needs a capability the survey is blind to.
///
/// The structural half is the compiler's: `CapabilitySurvey.Capability.permissionOf` /
/// `surveyedBy` are total over the closed `Capability` DU, so a new variant
/// cannot land without a probe. This file pins both directions of the totality
/// and the coarse-upper-bound invariant that earns S2 its place.

// A representative config exercising EVERY flow shape that requires a capability:
// a cross-substrate transfer (Source Reads + Sink INSERT/DELETE) and a model
// publish to a schema+data target (Sink ALTER/CREATE TABLE). Together these
// reach all five capabilities — the "in-scope" surface the totality holds the
// catalog to (the analog of Voice's `inScopeCodes`).
let private env (name: string) (grant: Grant option) : Projection.Pipeline.Environment =
    { Name = name; Access = Access.Direct (ConnectionRef.EnvVar (name + "_CONN")); Grant = grant; Store = None; Rendition = None }

let private flow (name: string) (from: FlowSource) (toEnv: string) : Flow =
    { Name = name; From = from; To = toEnv; Rekey = None; Tables = []; Scope = None; Shape = None; Shaping = None }

let private representativeConfig : ProjectionConfig =
    { ProjectionConfig.empty with
        Environments =
            [ env "staging" (Some Grant.SchemaAndData)
              env "uat-data" (Some Grant.DataOnly)
              env "uat-full" (Some Grant.SchemaAndData) ]
            |> List.map (fun e -> e.Name, e) |> Map.ofList
        Flows =
            [ flow "load"    (FlowSource.Env "staging") "uat-data"   // Source: Reads;  Sink: INSERT/DELETE
              flow "publish" FlowSource.Model            "uat-full" ] // Sink: ALTER/CREATE TABLE
            |> List.map (fun f -> f.Name, f) |> Map.ofList }

/// Every capability the configured flows actually require, across every place
/// and role — the harvested requirement surface.
let private requiredCapabilities (cfg: ProjectionConfig) : Set<CapabilitySurvey.Capability> =
    cfg.Flows
    |> Map.toList
    |> List.collect (fun (_, f) ->
        CapabilitySurvey.touchedBy f
        |> List.map (fun (_, role) -> CapabilitySurvey.requiredBy cfg f role))
    |> List.fold Set.union Set.empty

let private surveyedCapabilities : Set<CapabilitySurvey.Capability> = Set.ofList CapabilitySurvey.Capability.all

// ---------------------------------------------------------------------------
// required ⇔ surveyed
// ---------------------------------------------------------------------------

[<Fact>]
let ``CapabilitySurvey.Capability totality: every required capability is one the survey knows to probe (required ⊆ surveyed)`` () =
    let unprobeable = Set.difference (requiredCapabilities representativeConfig) surveyedCapabilities
    Assert.True(
        Set.isEmpty unprobeable,
        sprintf "flows require capabilities the survey cannot probe: %A" (Set.toList unprobeable))

[<Fact>]
let ``CapabilitySurvey.Capability totality: every surveyed capability is reachable as a real flow requirement (surveyed ⊆ required)`` () =
    // No dead probe — every capability the catalog can probe is exercised by some
    // realizable flow shape. The representative config reaches all five.
    let dead = Set.difference surveyedCapabilities (requiredCapabilities representativeConfig)
    Assert.True(
        Set.isEmpty dead,
        sprintf "the survey probes capabilities no flow requires (dead probes): %A" (Set.toList dead))

[<Fact>]
let ``CapabilitySurvey.Capability totality: the catalog is exactly the required surface for the representative estate`` () =
    Assert.Equal<Set<CapabilitySurvey.Capability>>(surveyedCapabilities, requiredCapabilities representativeConfig)

[<Fact>]
let ``CapabilitySurvey.Capability totality: permission names are distinct (no two capabilities collide on one probe)`` () =
    let names = CapabilitySurvey.Capability.all |> List.map CapabilitySurvey.Capability.permissionOf
    Assert.Equal<string list>(List.distinct names, names)

[<Fact>]
let ``CapabilitySurvey.Capability totality: the probe is total — every capability resolves to a non-empty permission name`` () =
    for cap in CapabilitySurvey.Capability.all do
        Assert.False(
            System.String.IsNullOrWhiteSpace (CapabilitySurvey.Capability.permissionOf cap),
            sprintf "%A has no permission name" cap)

// ---------------------------------------------------------------------------
// the coarse upper bound — requiredBy(sink) ⊆ requiredFor(targetGrant)
// (the invariant that locates exactly where S2 earns its place: a flow needs at
//  most what the grant permits; S2 surfaces where it needs strictly less)
// ---------------------------------------------------------------------------

[<Fact>]
let ``CapabilitySurvey.Capability totality: a flow's sink requirement never exceeds its target grant's permitted writes`` () =
    let cfg = representativeConfig
    for KeyValue (_, f) in cfg.Flows do
        match Map.tryFind f.To cfg.Environments |> Option.bind (fun e -> e.Grant) with
        | Some grant ->
            let sink = CapabilitySurvey.requiredBy cfg f SubstrateRole.Sink
            Assert.True(
                Set.isSubset sink (CapabilitySurvey.requiredFor grant),
                sprintf "flow '%s' sink requirement %A exceeds grant %A" f.Name (Set.toList sink) grant)
        | None -> ()

[<Fact>]
let ``CapabilitySurvey.Capability totality: the over-refusal trigger is real — a model publish needs strictly less than the coarse facet`` () =
    // open-Q1's trigger, witnessed: the model→schema+data publish flow needs only
    // the schema activities, a PROPER subset of requiredFor(SchemaAndData). The
    // coarse declared-vs-actual reconciliation would over-refuse on INSERT/DELETE
    // the flow never performs; S2's flow-harvested requirement does not.
    let cfg = representativeConfig
    let publish = Map.find "publish" cfg.Flows
    let sink = CapabilitySurvey.requiredBy cfg publish SubstrateRole.Sink
    Assert.True(Set.isProperSubset sink (CapabilitySurvey.requiredFor Grant.SchemaAndData))
    Assert.DoesNotContain(Performs Preflight.Insert, sink)
    Assert.DoesNotContain(Performs Preflight.Delete, sink)
