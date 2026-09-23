module Projection.Tests.ConfigProvenanceTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// align-I.9 (audit a3-F5) — THE CONFIG-PROVENANCE RULE, pinned.
//
// The rule (canonical statement: `Classification.fs`, the Classification
// DU docstring): a config-taking pass whose output lands in ARTIFACTS
// classifies OperatorIntent when its config can carry operator OPINION
// — a default the operator could have changed IS the operator's intent —
// and DataIntent only when its config is SOURCE-DERIVED (data about the
// data). Advisory analytics (Diagnostics-domain outputs that mutate no
// artifact) sit outside the rule's blast radius: tuning thresholds shift
// commentary, never artifacts.
//
// Before this slice the question "whose default is it?" had FOUR
// inconsistent prose answers (NamingMorphism / VisibilityMask /
// LogicalTableEmission / AdvisoryTuning). The table below is the one
// answer, per factory, asserted against the live registry — a NEW chain
// pass cannot land without a stance row (the completeness pin), and a
// factory whose classification drifts from its ruled stance fails by
// name.
//
// `Supplied<'T> = SourceDerived of 'T | OperatorDeclared of 'T` (the
// provenance wrapper that would let `registered` DERIVE classification)
// stays a NAMED DEFERRAL — trigger: the first mis-wiring this table
// catches. Until then the table IS the enforcement.
// ---------------------------------------------------------------------------

/// One ruled stance per chain pass: the pass name, the classifications
/// its Sites are RULED to carry, and the why (the rule's application).
type private Stance =
    { Pass : string
      Ruled : Classification list
      Why : string }

let private stances : Stance list =
    [ { Pass = "canonicalizeIdentity"
        Ruled = [ DataIntent ]
        Why = "identity-form normalization at the adapter boundary — structural, no config" }
      { Pass = "visibilityMask"
        Ruled = [ OperatorIntent Selection ]
        Why = "the mask IS operator opinion: which kinds surface" }
      { Pass = "selectionSuppression"
        Ruled = [ OperatorIntent Selection ]
        Why = "lifecycle-selection axes are the operator's scope choice (S9)" }
      { Pass = "physicalClaims"
        Ruled = [ DataIntent ]
        Why = "witnessed journal-assembled adjudications — evidence-carriage, not opinion (S13)" }
      { Pass = "namingMorphism"
        Ruled = [ DataIntent ]
        Why = "the morphism is a SOURCE-DERIVED name correspondence (data about the data); an operator-authored non-identity morphism would be a mis-use — the Supplied<'T> trigger names exactly this seam" }
      { Pass = "normalizeStaticPopulations"
        Ruled = [ DataIntent ]
        Why = "algorithm-internal closure, no config" }
      { Pass = "symmetricClosure"
        Ruled = [ DataIntent ]
        Why = "algorithm-internal closure, no config" }
      { Pass = "logicalTableEmission"
        Ruled = [ OperatorIntent Emission ]
        Why = "ships Enabled by default — default-on IS the operator's intent (the rule's worked example)" }
      { Pass = "logicalColumnEmission"
        Ruled = [ OperatorIntent Emission ]
        Why = "sibling of logicalTableEmission; same default-on ruling" }
      { Pass = "tableRename"
        Ruled = [ OperatorIntent Emission ]
        Why = "rename specs are operator opinion about emitted names" }
      { Pass = "topologicalOrder"
        Ruled = [ DataIntent; OperatorIntent Ordering ]
        Why = "sortKahn is structural (DataIntent); selfLoopHandling is the operator's ordering policy" }
      { Pass = "centrality"
        Ruled = [ DataIntent ]
        Why = "advisory analytics over the FK graph — outside the rule's artifact blast radius" }
      { Pass = "boundedContext"
        Ruled = [ DataIntent ]
        Why = "advisory analytics — outside the artifact blast radius" }
      { Pass = "profileAnomaly"
        Ruled = [ DataIntent ]
        Why = "evidence-driven advisory; AdvisoryTuning thresholds shift commentary, never artifacts (the ruled boundary case)" }
      { Pass = "schemaComplexity"
        Ruled = [ DataIntent ]
        Why = "advisory analytics — outside the artifact blast radius" }
      { Pass = "cascadeShockZones"
        Ruled = [ DataIntent ]
        Why = "advisory risk analytics — outside the artifact blast radius" }
      { Pass = "queryHint"
        Ruled = [ DataIntent ]
        Why = "evidence-driven advisory; same AdvisoryTuning boundary ruling as profileAnomaly" }
      { Pass = "nullability"
        Ruled = [ OperatorIntent Tightening ]
        Why = "interventions are operator opinion about constraint tightening" }
      { Pass = "uniqueIndex"
        Ruled = [ OperatorIntent Tightening ]
        Why = "interventions are operator opinion" }
      { Pass = "foreignKey"
        Ruled = [ OperatorIntent Tightening ]
        Why = "interventions are operator opinion" }
      { Pass = "categoricalUniqueness"
        Ruled = [ OperatorIntent Tightening ]
        Why = "interventions are operator opinion" }
      { Pass = "userFkReflow"
        Ruled = [ OperatorIntent OverlayAxis.Identity ]
        Why = "Policy.UserMatching rules which identity each reference resolves through (align-I.3)" }
      { Pass = "bridgeRetarget"
        Ruled = [ OperatorIntent OverlayAxis.Identity ]
        Why = "Policy.BridgeRetarget rules which identity each declared reference resolves through (align-I.3)" } ]

[<Fact>]
let ``align-I.9: every chain pass has exactly one stance row (the rule table is complete over the chain)`` () =
    let chainNames =
        RegisteredTransforms.chainSteps
        |> List.map (fun s -> s.Metadata.Name)
        |> Set.ofList
    let tableNames = stances |> List.map (fun s -> s.Pass) |> Set.ofList
    Assert.Equal<Set<string>>(chainNames, tableNames)

[<Fact>]
let ``align-I.9: every factory's registered classification matches its ruled stance (the config-provenance rule holds)`` () =
    let byName =
        RegisteredTransforms.chainSteps
        |> List.map (fun s -> s.Metadata.Name, s.Metadata)
        |> Map.ofList
    for stance in stances do
        match Map.tryFind stance.Pass byName with
        | None -> Assert.Fail(sprintf "stance row '%s' names no chain pass" stance.Pass)
        | Some metadata ->
            let actual =
                metadata.Sites
                |> List.map (fun site -> site.Classification)
                |> List.distinct
                |> List.sort
            Assert.True(
                (List.sort stance.Ruled) = actual,
                sprintf
                    "'%s' classification drifted from its ruled stance.\n  ruled:  %A\n  actual: %A\n  why ruled: %s"
                    stance.Pass stance.Ruled actual stance.Why)
