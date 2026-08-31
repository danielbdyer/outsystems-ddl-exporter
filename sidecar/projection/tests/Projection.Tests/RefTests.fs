[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.RefTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

/// The keystone connector — the revision algebra. Every ref resolves to the
/// SAME operand type, so verbs become `verb <ref>`. The decisive test: a
/// @runId resolves to a Catalog (the Run <-> Ref connection that wires the
/// two preconditions together).

let private minimalModel = """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
  "modules": [ { "name": "AppCore", "isSystem": false, "isActive": true,
    "entities": [ { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
      "isStatic": false, "isExternal": false, "isActive": true, "db_catalog": null, "db_schema": "dbo",
      "attributes": [ { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier",
        "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true,
        "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null,
        "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null,
        "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 } ],
      "relationships": [], "indexes": [], "triggers": [] } ] } ] }"""

[<Fact>]
let ``Ref: parse reads the revision syntax`` () =
    Assert.Equal(Ref.RunArtifact "01ABC", Ref.parse "@01ABC")
    Assert.Equal(Ref.Live "uat", Ref.parse "live:uat")
    Assert.Equal(Ref.Ossys "cloud-dev", Ref.parse "ossys:cloud-dev")
    Assert.Equal(Ref.Json "{}", Ref.parse "json:{}")
    Assert.Equal(Ref.File "model.json", Ref.parse "model.json")

[<Fact>]
let ``Ref: sink refs parse latest, pinned, and keep a malformed pin as label text (total, never misrouted)`` () =
    // `sink:<env>[@<syncId>]` (the data-sink chapter, S7). A non-numeric pin
    // stays part of the LABEL (labels are opaque operator strings), so the
    // malformed form fails downstream as the named `sink.envUnknown` naming
    // the whole text — total parse, no silent misroute to a file ref.
    Assert.Equal(Ref.Sink ("uat", None), Ref.parse "sink:uat")
    Assert.Equal(Ref.Sink ("uat", Some SyncOrdinal.genesis), Ref.parse "sink:uat@1")
    Assert.Equal(Ref.Sink ("uat@edge", None), Ref.parse "sink:uat@edge")
    // align-III.1: a NON-POSITIVE pin is a malformed pin — no such edition
    // can exist (the ordinal VO), so it stays label text exactly like a
    // non-numeric tail (and fails downstream as `sink.envUnknown`).
    Assert.Equal(Ref.Sink ("uat@0", None), Ref.parse "sink:uat@0")
    Assert.Equal(Ref.Sink ("uat@-2", None), Ref.parse "sink:uat@-2")
    Assert.Equal("sink:uat", Ref.identity (Ref.parse "sink:uat"))
    Assert.Equal("sink:uat@1", Ref.identity (Ref.parse "sink:uat@1"))

[<Fact>]
let ``Ref: a json ref resolves to a catalog`` () =
    match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.Json minimalModel)) with
    | Ok c    -> Assert.NotEmpty(Catalog.allKinds c)
    | Error e -> Assert.Fail(sprintf "%A" e)

[<Fact>]
let ``Ref: a runId ref resolves to the run's catalog (the Run-Ref connection)`` () =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    let prior = Environment.GetEnvironmentVariable "PROJECTION_RUNS_DIR"
    try
        Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", dir)
        let run : Run.Run =
            { RunId = "01HUB"; Ts = DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero); Command = "x"; InputDigest = "d"; Outcome = "succeeded"
              Canary = CanaryVerdict.NotRun; Registered = 0; Applied = 0; Declined = 0; Events = []
              Artifacts = Map.ofList [ "model.json", minimalModel ]
              Ledgers = []; Bench = None }
        Run.save dir run
        match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.parse "@01HUB")) with
        | Ok c    -> Assert.NotEmpty(Catalog.allKinds c)
        | Error e -> Assert.Fail(sprintf "%A" e)
    finally
        Environment.SetEnvironmentVariable("PROJECTION_RUNS_DIR", prior)
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``Ref: a live ref resolves through the adapter and fails loud as a NAMED Error on an unreachable endpoint`` () =
    // The live adapter shipped (Source.ofLive): a `live:` ref now resolves
    // through ReadSide over a real connection. An unreachable / malformed
    // endpoint must surface as the port's typed refusal — NOT a silent success
    // (the original "don't be silently wrong" guarantee) and NOT a raw
    // exception escaping the `Task<Result<_>>` boundary (which would crash the
    // CLI's `.GetAwaiter().GetResult()` instead of printing a clean error).
    // A short login timeout keeps the test fast without reaching any server.
    match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.Live "Server=127.0.0.1,1;Connect Timeout=1;TrustServerCertificate=True;Encrypt=False")) with
    | Ok _     -> Assert.Fail "live should fail on an unreachable endpoint, not silently succeed"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "source.live.connectionFailed")

[<Fact>]
let ``Ref: an ossys ref reads the OSSYS metamodel and fails loud (never silent) on an unreachable spec`` () =
    // The `ossys:` operand reads the model from the OutSystems OSSYS metamodel
    // (LiveModelRead → native GUID `OssysOriginal` SsKey at kind AND attribute
    // grain) — the espace-safe identity source for cross-environment readiness
    // (CROSS_ENVIRONMENT_READINESS.md). D9 amended 2026-06-28 (operator decision;
    // see DECISIONS.md): the OSSYS model source now opens through the ONE
    // `ConnectionSpec.openSpec` opener, uniform with `transfer`/`slice` — a bare
    // (non-out-of-band) spec is no longer refused at parse, it is OPENED, and an
    // unreachable / malformed one surfaces as the one opener's typed
    // `connection.openFailed` refusal (the bare spec is treated as a connection
    // string and fails at open), NEVER a silent resolve. `env:`/`file:` remain
    // the recommended out-of-band form.
    match TaskSync.run (fun () -> Ref.resolveCatalog (Ref.parse "ossys:not-a-ref")) with
    | Ok _    -> Assert.Fail "ossys should fail loud on an unreachable spec, not silently succeed"
    | Error es -> Assert.Contains(es, fun e -> e.Code = "connection.openFailed")

[<Fact>]
let ``Ref: espaceSafe / bothLive classify the cross-environment operand posture`` () =
    // Espace-safe operands carry NATIVE (OssysOriginal GUID) identity:
    // `ossys:` live model reads and `sink:` witnessed states (S7 — the sink
    // persists the same rowsets the ossys read parses, K2). Both espace-safe
    // ⇒ the `compare`/`diff` run faces normalize to the logical shape; both
    // `live:` ⇒ espace-UNSAFE (a named advisory fires).
    Assert.True(Ref.espaceSafe (Ref.parse "ossys:cloud-dev"))
    Assert.True(Ref.espaceSafe (Ref.parse "sink:uat"))
    Assert.False(Ref.espaceSafe (Ref.parse "live:a"))
    Assert.False(Ref.espaceSafe (Ref.parse "model.json"))
    Assert.False(Ref.espaceSafe (Ref.parse "@01ABC"))
    Assert.True(Ref.bothEspaceSafe (Ref.parse "ossys:cloud-dev") (Ref.parse "ossys:cloud-qa"))
    Assert.True(Ref.bothEspaceSafe (Ref.parse "ossys:cloud-dev") (Ref.parse "sink:uat"))
    Assert.True(Ref.bothEspaceSafe (Ref.parse "sink:uat@1") (Ref.parse "sink:uat@2"))
    Assert.False(Ref.bothEspaceSafe (Ref.parse "ossys:cloud-dev") (Ref.parse "live:cloud-qa"))
    Assert.True(Ref.bothLive (Ref.parse "live:a") (Ref.parse "live:b"))
    Assert.False(Ref.bothLive (Ref.parse "ossys:a") (Ref.parse "live:b"))
    Assert.False(Ref.bothLive (Ref.parse "file:a.json") (Ref.parse "file:b.json"))
