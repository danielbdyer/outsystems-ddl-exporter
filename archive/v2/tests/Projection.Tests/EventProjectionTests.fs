[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.EventProjectionTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// §16 egress-projection coverage — `Projection.Pipeline.EventProjection`
/// turns the pass chain's accumulated writers (Lineage trail +
/// Diagnostics stream) into `transform.*` LogSink envelopes per
/// `docs/logging-format.md` §7.4. The projection is pure, so these
/// tests assert directly on the returned `LogSink.Envelope` values
/// (no writer / serialization needed). `beginRunWith` fixes a runId so
/// `envelope` reads a stable run context.

let private key (basis: string) : SsKey =
    SsKey.synthesized "TEST" basis |> Result.value

let private lineageEvent (basis: string) (kind: TransformKind) : LineageEvent =
    { PassName       = "TestPass"
      PassVersion    = 1
      SsKey          = key basis
      TransformKind  = kind
      Classification = DataIntent }

let private codes (envs: LogSink.Envelope list) : string list =
    envs |> List.map (fun e -> e.Code)

let private payloadStr (env: LogSink.Envelope) (k: string) : string =
    match Map.tryFind k env.Payload with
    | Some v -> string v
    | None   -> failwithf "missing payload key %s in %s" k env.Code

[<Fact>]
let ``§7.4: an enforced nullability decision projects to transform.applied (info)`` () =
    LogSink.beginRunWith "TEST"
    let ev =
        lineageEvent "Mod.Ent.Col"
            (Annotated (NullabilityDecision ("nb-1", NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)))
    let env = EventProjection.ofLineageEvent ev
    Assert.Equal("transform.applied", env.Code)
    Assert.Equal(LogSink.Info, env.Level)
    Assert.Equal(LogSink.Transform, env.Category)
    Assert.Equal(Some (key "Mod.Ent.Col"), env.SsKey)
    Assert.Equal("nb-1", payloadStr env "interventionId")
    Assert.True(env.Payload.ContainsKey "decision")

[<Fact>]
let ``§7.4: a kept nullability decision projects to transform.declined with rationale`` () =
    LogSink.beginRunWith "TEST"
    let ev =
        lineageEvent "Mod.Ent.Col"
            (Annotated (NullabilityDecision ("nb-1", NullabilityOutcome.KeepNullable NoTighteningSignal)))
    let env = EventProjection.ofLineageEvent ev
    Assert.Equal("transform.declined", env.Code)
    Assert.Equal(LogSink.Info, env.Level)
    Assert.True(env.Payload.ContainsKey "rationale")
    Assert.False(env.Payload.ContainsKey "decision")

[<Fact>]
let ``§7.4: a non-decision event projects to transform.lineage (debug)`` () =
    LogSink.beginRunWith "TEST"
    let env = EventProjection.ofLineageEvent (lineageEvent "Mod.Ent" Touched)
    Assert.Equal("transform.lineage", env.Code)
    Assert.Equal(LogSink.Debug, env.Level)
    Assert.Equal("touched", payloadStr env "transformKind")
    Assert.Equal("dataIntent", payloadStr env "classification")

[<Fact>]
let ``trail projection is one envelope per event, in trail order`` () =
    LogSink.beginRunWith "TEST"
    let trail =
        [ lineageEvent "A" Touched
          lineageEvent "B" (Annotated (NullabilityDecision ("nb", NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey)))
          lineageEvent "C" Created ]
    let envs = EventProjection.ofLineageTrail trail
    Assert.Equal(3, List.length envs)
    Assert.Equal<string list>(
        [ "transform.lineage"; "transform.applied"; "transform.lineage" ],
        codes envs)

[<Fact>]
let ``§7.4: a diagnostic entry projects to transform.diagnostic at its severity`` () =
    LogSink.beginRunWith "TEST"
    let entry =
        { DiagnosticEntry.create "TestPass" DiagnosticSeverity.Warning "tightening.nullability.opportunity" "has nulls"
            with SsKey = Some (key "Mod.Ent.Col") }
    let env = EventProjection.ofDiagnosticEntry entry
    Assert.Equal("transform.diagnostic", env.Code)
    Assert.Equal(LogSink.Warn, env.Level)
    Assert.Equal(Some (key "Mod.Ent.Col"), env.SsKey)
    Assert.Equal("tightening.nullability.opportunity", payloadStr env "code")

[<Fact>]
let ``L3-X12: a diagnostic carrying SuggestedConfig surfaces it under the suggestedConfig key`` () =
    LogSink.beginRunWith "TEST"
    let cfg = SuggestedConfig.create "$.tightening.nullBudget" "0.05" |> Result.value
    let entry =
        { DiagnosticEntry.create "TestPass" DiagnosticSeverity.Warning "tightening.nullability.opportunity" "has nulls"
            with SuggestedConfig = Some cfg }
    let env = EventProjection.ofDiagnosticEntry entry
    // The §11 rollup's suggestedConfigEdits counter keys off this payload
    // presence (LogSink.updateAccumulator), so the key MUST be present.
    Assert.True(env.Payload.ContainsKey "suggestedConfig")

[<Fact>]
let ``a diagnostic without SsKey projects with no envelope ssKey`` () =
    LogSink.beginRunWith "TEST"
    let entry = DiagnosticEntry.create "TestPass" DiagnosticSeverity.Info "x.y" "z"
    let env = EventProjection.ofDiagnosticEntry entry
    Assert.Equal(None, env.SsKey)

let private meta (name: string) (stage: StageBinding) (sites: TransformSite list) : RegisteredTransformMetadata =
    { Name = name; Domain = Schema; StageBinding = stage; Sites = sites; Status = Active }

[<Fact>]
let ``§7.4: a registered transform projects to transform.registered (debug, start) carrying per-site classification`` () =
    LogSink.beginRunWith "TEST"
    let m =
        meta "TestPass" Pass
            [ TransformSite.dataIntent "siteA" "schema mandate"
              TransformSite.operatorIntent "siteB" Tightening "operator chose to tighten" ]
    let env = EventProjection.ofRegisteredTransform m
    Assert.Equal("transform.registered", env.Code)
    Assert.Equal(LogSink.Debug, env.Level)
    Assert.Equal(LogSink.Start, env.Phase)
    Assert.Equal("TestPass", payloadStr env "transformId")
    Assert.Equal("pass", payloadStr env "stage")
    Assert.Equal("active", payloadStr env "status")
    // per-site classification is carried (not one lossy intent)
    Assert.True(env.Payload.ContainsKey "sites")

[<Fact>]
let ``ofRegistry projects exactly one event per registered transform`` () =
    LogSink.beginRunWith "TEST"
    let registry =
        [ meta "A" Pass [ TransformSite.dataIntent "s" "r" ]
          meta "B" Emitter [ TransformSite.operatorIntent "s" Emission "r" ] ]
    let envs = EventProjection.ofRegistry registry
    Assert.Equal(2, List.length envs)
    Assert.All(envs, fun e -> Assert.Equal("transform.registered", e.Code))
